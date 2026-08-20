using System.Collections.Generic;
using System.IO;
using Inkform.Level;
using Inkform.LevelGraph;
using Inkform.LevelGraph.EditorTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Inkform.EditorTools
{
    /// <summary>
    /// Room builder: one-click level generation per Docs/LevelGenerationPlan.md. Builds every room
    /// scene of the 3-level graph from a shared floor skeleton with one of three challenge variants
    /// (V0 single spike + 3-step tower, V1 double spike + 3-step tower, V2 spike-on-step + 4-step
    /// tower), room label, and directional door slots (right-ground / right-high / left-ground / top),
    /// generates the LevelScene assets, fills the LevelFlow
    /// graph, wires GameManager's SceneDirector.flow, and appends the scenes to Build Settings.
    /// Re-running is safe (idempotent): scenes/assets are overwritten in place, connections are
    /// rebuilt from the data table each run, the flow slot is filled only when empty, and Build
    /// Settings entries are de-duplicated.
    ///
    /// Menu: Tools &gt; Inkform &gt; Room Builder. Also callable headlessly:
    ///   Unity -batchmode -quit -executeMethod Inkform.EditorTools.RoomBuilder.BuildAll
    /// </summary>
    public static class RoomBuilder
    {
        // ---- Paths ----
        private const string ScenesDir = "Assets/Scenes/Generated";
        private const string LevelsDir = ScenesDir + "/Levels";
        private const string FlowAssetPath = "Assets/Scenes/All_level_Con.asset";
        private const string GameManagerPrefabPath = "Assets/Prefabs/Control/GameManager.prefab";
        private const string PlayerPrefabPath = "Assets/Prefabs/Control/Player.prefab";
        private const string CameraPrefabPath = "Assets/Prefabs/Control/MainCamera.prefab";
        private const string VolumePrefabPath = "Assets/Prefabs/env/GlobalVolume.prefab";
        private const string CheckpointPrefabPath = "Assets/Prefabs/env/Checkpoint.prefab";
        private const string MenuScenePath = "Assets/Scenes/Menu/MainMenu.unity";
        private const string PanelFontPath = "Assets/Art/UI/BombSlimeFonts.ttf";
        private const string GeneratedArtDir = "Assets/Art/Generated";
        private const string WhitePixelPath = GeneratedArtDir + "/WhitePixel.png";

        // ---- Room data ----
        // Sourced from LevelGraph.txt, which is the single source of truth for the topology. This used
        // to be a hand-maintained C# table that also generated the LevelScene assets, which meant the
        // graph existed in two places that nothing compared. The graph tool owns it now; this builder
        // only reads it, and only to know which greybox scenes to bootstrap.

        private sealed class RoomData
        {
            public readonly string sceneName;
            public readonly string levelName;

            /// <summary>Target room of each door, in graph order. Used for the door label and for the
            /// Spawn_&lt;target&gt; checkpoint name.</summary>
            public readonly string[] neighbors;

            /// <summary>Parallel to neighbors: the id each door announces. Normally identical to the
            /// target name — the graph only differs when a link declares an explicit id.</summary>
            public readonly string[] exitIds;

            public RoomData(string sceneName, string levelName, string[] neighbors, string[] exitIds)
            {
                this.sceneName = sceneName;
                this.levelName = levelName;
                this.neighbors = neighbors;
                this.exitIds = exitIds;
            }
        }

        // Rebuilt on demand from the graph file. Each menu entry clears it first, so editing the graph
        // and running a builder in the same session cannot act on a stale table.
        private static RoomData[] cachedRooms;

        private static RoomData[] Rooms
        {
            get
            {
                if (cachedRooms == null) cachedRooms = LoadRoomsFromGraph();
                return cachedRooms;
            }
        }

        private static void InvalidateRooms() => cachedRooms = null;

        private static RoomData[] LoadRoomsFromGraph()
        {
            LevelGraphDocument doc = LevelGraphFile.Load();

            if (doc.Rooms.Count == 0)
            {
                Debug.LogWarning($"RoomBuilder: {LevelGraphFile.Path} has no rooms. "
                               + "Open Tools > Inkform > Level Graph and press \"Import From Assets\" to seed it.");
                return new RoomData[0];
            }

            var exits = new Dictionary<string, List<DirectedLink>>();
            foreach (RoomEntry room in doc.Rooms) exits[room.Name] = new List<DirectedLink>();
            foreach (DirectedLink link in doc.EnumerateDirected())
            {
                if (exits.TryGetValue(link.From, out List<DirectedLink> list)) list.Add(link);
            }

            var rooms = new RoomData[doc.Rooms.Count];
            for (int i = 0; i < doc.Rooms.Count; i++)
            {
                RoomEntry room = doc.Rooms[i];
                List<DirectedLink> outgoing = exits[room.Name];

                var neighbors = new string[outgoing.Count];
                var exitIds = new string[outgoing.Count];
                for (int k = 0; k < outgoing.Count; k++)
                {
                    neighbors[k] = outgoing[k].To;
                    exitIds[k] = outgoing[k].ExitId;
                }

                rooms[i] = new RoomData(room.Name, room.DisplayName, neighbors, exitIds);
            }

            return rooms;
        }

        // ---- Menu entries ----

        [MenuItem("Tools/Inkform/Room Builder/Build Missing Rooms")]
        public static void BuildMissingRooms()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            InvalidateRooms();
            EnsureFolder(ScenesDir, "Assets/Scenes");

            int built = 0;
            int skipped = 0;

            foreach (RoomData room in Rooms)
            {
                // The whole point of this method. The old Build All Rooms opened an empty scene and
                // saved over every room, so any hand authoring in a generated level was destroyed on
                // the next run — which made the generator something a designer could not safely live
                // beside. A scene that exists is now the designer's, and the builder leaves it alone.
                if (File.Exists($"{ScenesDir}/{room.sceneName}.unity")) { skipped++; continue; }

                BuildRoom(room);
                built++;
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"RoomBuilder: {built} greybox room(s) built, {skipped} existing scene(s) left untouched.");
        }

        /// <summary>
        /// Throws away one room's scene and regenerates the greybox. The only destructive entry point
        /// left, and it is deliberately single-room, explicit and confirmed: re-rolling a blockout you
        /// have not authored yet is a reasonable thing to want, doing it to 22 rooms by accident is not.
        /// </summary>
        [MenuItem("Tools/Inkform/Room Builder/Rebuild Selected Room (Destructive)")]
        public static void RebuildSelectedRoom()
        {
            InvalidateRooms();

            string roomName = EditorInputDialog.Show(
                "Rebuild room", "Scene name of the room to regenerate as a greybox:", "");
            if (string.IsNullOrEmpty(roomName)) return;

            RoomData target = null;
            foreach (RoomData room in Rooms)
            {
                if (room.sceneName == roomName) { target = room; break; }
            }

            if (target == null)
            {
                Debug.LogWarning($"RoomBuilder: '{roomName}' is not a room in {LevelGraphFile.Path}.");
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "Rebuild room",
                    $"Discard everything in {roomName}.unity and regenerate it as a greybox?\n\nThis cannot be undone.",
                    "Discard and rebuild", "Cancel"))
                return;

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            BuildRoom(target);
            AssetDatabase.SaveAssets();
            Debug.Log($"RoomBuilder: rebuilt {roomName} as a greybox.");
        }


        [MenuItem("Tools/Inkform/Room Builder/Wire GameManager Flow")]
        public static void WireGameManagerFlow()
        {
            LevelFlow flow = AssetDatabase.LoadAssetAtPath<LevelFlow>(FlowAssetPath);
            if (flow == null || flow.levels == null || flow.levels.Length == 0)
            {
                Debug.LogWarning("RoomBuilder: the flow asset has no levels yet — open Tools > Inkform > Level Graph and press Apply first");
                return;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(GameManagerPrefabPath);
            SceneDirector director = root.GetComponent<SceneDirector>();
            if (director == null)
            {
                Debug.LogError("RoomBuilder: GameManager prefab has no SceneDirector");
                PrefabUtility.UnloadPrefabContents(root);
                return;
            }

            SerializedObject so = new SerializedObject(director);
            AssignIfEmpty(so, "flow", flow);
            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, GameManagerPrefabPath);
            PrefabUtility.UnloadPrefabContents(root);
            Debug.Log($"RoomBuilder: GameManager SceneDirector.flow -> {FlowAssetPath}");
        }

        [MenuItem("Tools/Inkform/Room Builder/Fix Level Build Settings")]
        public static void FixBuildSettings()
        {
            // The room list is cached per menu invocation; the level graph window writes the file
            // directly (it is not a menu entry), so re-read it or freshly adopted rooms never reach
            // the build lists.
            InvalidateRooms();

            var scenes = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(MenuScenePath, true) };

            // Preserve every existing entry except the menu (re-added at index 0 above), de-duplicated
            foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
            {
                if (s.path == MenuScenePath) continue;
                if (scenes.Exists(x => x.path == s.path)) continue;
                scenes.Add(s);
            }

            foreach (RoomData room in Rooms)
            {
                // Hand-authored rooms may live outside Generated/ — resolve by name project-wide,
                // falling back to the generated path for rooms whose scene does not exist yet
                // (the greybox builder will create it there).
                string path = LevelGraphFile.ScenePathFor(room.sceneName) ?? $"{ScenesDir}/{room.sceneName}.unity";
                if (scenes.Exists(x => x.path == path)) continue;
                scenes.Add(new EditorBuildSettingsScene(path, true));
            }

            EditorBuildSettings.scenes = scenes.ToArray();

            // Unity 6000: a build profile with m_OverrideGlobalSceneList overrides the global list
            // for LoadScene — the global entries alone are not enough, so append to the profile too
            int profileAdded = AppendRoomsToBuildProfile();
            Debug.Log($"RoomBuilder: Build Settings — {scenes.Count} global scenes (menu at index 0, {Rooms.Length} rooms), {profileAdded} scene(s) added to the build profile");
        }

        /// <summary>Adds the generated scenes to every build profile that overrides the global scene
        /// list (Assets/Settings/Build Profiles). Without this, SceneManager.LoadScene fails with
        /// "has not been added to the build settings" even though the global list contains the scene.
        /// Called with a room name by the level graph fixer so an adopted hand-authored scene lands in
        /// the profiles too; called with null it covers every room in the graph.</summary>
        public static int AppendRoomsToBuildProfile(string roomName = null)
        {
            // Called from the level graph fixer too, where the graph file was just saved by the
            // window — never trust the per-menu cache here.
            InvalidateRooms();

            int added = 0;
            const string profilesDir = "Assets/Settings/Build Profiles";
            if (!AssetDatabase.IsValidFolder(profilesDir)) return 0;

            foreach (string file in System.IO.Directory.GetFiles(profilesDir, "*.asset"))
            {
                string path = file.Replace('\\', '/');
                SerializedObject so = new SerializedObject(AssetDatabase.LoadAssetAtPath<Object>(path));
                SerializedProperty overrideList = so.FindProperty("m_OverrideGlobalSceneList");
                if (overrideList == null || !overrideList.boolValue) continue;   // not an overriding build profile

                SerializedProperty scenesProp = so.FindProperty("m_Scenes");
                if (scenesProp == null || !scenesProp.isArray) continue;

                foreach (RoomData room in Rooms)
                {
                    if (roomName != null && room.sceneName != roomName) continue;

                    string scenePath = LevelGraphFile.ScenePathFor(room.sceneName) ?? $"{ScenesDir}/{room.sceneName}.unity";
                    bool exists = false;
                    for (int i = 0; i < scenesProp.arraySize; i++)
                    {
                        SerializedProperty item = scenesProp.GetArrayElementAtIndex(i);
                        if (item.FindPropertyRelative("m_path").stringValue == scenePath) { exists = true; break; }
                    }
                    if (exists) continue;

                    scenesProp.InsertArrayElementAtIndex(scenesProp.arraySize);
                    SerializedProperty entry = scenesProp.GetArrayElementAtIndex(scenesProp.arraySize - 1);
                    entry.FindPropertyRelative("m_enabled").boolValue = true;
                    entry.FindPropertyRelative("m_path").stringValue = scenePath;
                    entry.FindPropertyRelative("m_guid").stringValue = AssetDatabase.AssetPathToGUID(scenePath);
                    added++;
                }
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            return added;
        }

        /// <summary>
        /// The whole bootstrap in one go, and non-destructive throughout: greybox only what has no
        /// scene yet, then make the assets match the graph. The topology itself is not generated here
        /// any more — LevelGraph.txt owns it, and Tools > Inkform > Level Graph is where it is edited.
        /// </summary>
        [MenuItem("Tools/Inkform/Room Builder/Build All (non-destructive)")]
        public static void BuildAll()
        {
            BuildMissingRooms();
            LevelGraphSync.Apply(LevelGraphFile.Load());
            WireGameManagerFlow();
            FixBuildSettings();
        }

        // ---- Room template (Docs/LevelGenerationPlan.md §4) ----
        // Shared skeleton (floor + spawn + four directional door slots) with three challenge variants
        // rotating by room index: V0 single spike + 3-step tower, V1 double spike + 3-step tower,
        // V2 spike-on-step + 4-step tower. Every climb is +2 units per step, within the ~2.45 jump
        // reach; the two ground doors stay reachable by walking. All blocks sit on Terrain(6) so
        // ContactSensor's ground mask (6|11) registers them.

        private static readonly Color FloorColor = new Color(0.5f, 0.5f, 0.5f);
        private static readonly Color StepColor = new Color(0.65f, 0.65f, 0.65f);
        private static readonly Color SpikeColor = new Color(0.85f, 0.2f, 0.2f);

        private static void BuildRoom(RoomData room)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            Instantiate(scene, GameManagerPrefabPath, "GameManager", new Vector3(0f, 0f, 0f));
            Instantiate(scene, PlayerPrefabPath, "Player", new Vector3(4.5f, 2.5f, 0f));
            Instantiate(scene, CameraPrefabPath, "Main Camera", new Vector3(10f, 5.5f, -10f));
            Instantiate(scene, VolumePrefabPath, "GlobalVolume", new Vector3(10f, 5.5f, 0f));

            // No scene-instance wiring: the persistent managers (InputHandler / AniHandler on the
            // DontDestroyOnLoad GameManager) and the follow camera resolve the player at runtime via
            // PlayerBus.Player, so serialized references cannot go stale across scene switches.

            BuildChallenge(scene, room);

            GameObject checkpoint = Instantiate(scene, CheckpointPrefabPath, "Checkpoint", new Vector3(4.5f, 2f, 0f));
            SerializedObject soCp = new SerializedObject(checkpoint.GetComponent<Checkpoint>());
            soCp.FindProperty("isStartPoint").boolValue = true;
            soCp.ApplyModifiedPropertiesWithoutUndo();

            BuildRoomLabel(room);

            EditorSceneManager.SaveScene(scene, $"{ScenesDir}/{room.sceneName}.unity");
        }

        /// <summary>The challenge variant of a room: its index in the Rooms table modulo 3.</summary>
        private static int VariantOf(RoomData room)
        {
            for (int i = 0; i < Rooms.Length; i++)
            {
                if (ReferenceEquals(Rooms[i], room)) return i % 3;
            }
            return 0;
        }

        /// <summary>Shared floor plus the variant's blocks; the four directional door slots are then
        /// filled in order by the room's neighbors (S0 right-ground, S1 right-high, S2 left-ground, S3 top).</summary>
        private static void BuildChallenge(Scene scene, RoomData room)
        {
            MakeBlock("Floor", 11f, 1.5f, 22f, 1f, FloorColor);

            Vector2 highDoor;   // S1: on the right platform (RP)
            Vector2 topDoor;    // S3: on the top platform (TP)
            switch (VariantOf(room))
            {
                case 0: // V0: single ground spike + 3-step tower
                    MakeBlock("Spike", 7f, 3f, 3f, 2f, SpikeColor);
                    MakeBlock("M1", 15f, 3.5f, 4f, 1f, StepColor);
                    MakeBlock("RP", 19f, 5.5f, 6f, 1f, StepColor);
                    MakeBlock("TP", 10f, 7.5f, 4f, 1f, StepColor);
                    highDoor = new Vector2(20.5f, 6.5f);
                    topDoor = new Vector2(10f, 8.5f);
                    break;
                case 1: // V1: two ground spikes + 3-step tower
                    MakeBlock("Spike", 6.5f, 3f, 3f, 2f, SpikeColor);
                    MakeBlock("Spike (2)", 11.5f, 3f, 3f, 2f, SpikeColor);
                    MakeBlock("M1", 15f, 3.5f, 4f, 1f, StepColor);
                    MakeBlock("RP", 19f, 5.5f, 6f, 1f, StepColor);
                    MakeBlock("TP", 10f, 7.5f, 4f, 1f, StepColor);
                    highDoor = new Vector2(20.5f, 6.5f);
                    topDoor = new Vector2(10f, 8.5f);
                    break;
                default: // V2: spike on the mid step + 4-step tower
                    MakeBlock("M1", 14f, 3.5f, 4f, 1f, StepColor);
                    MakeBlock("M2", 17f, 5.5f, 4f, 1f, StepColor);
                    MakeBlock("Spike", 17.5f, 6f, 3f, 1f, SpikeColor);
                    MakeBlock("RP", 20f, 7.5f, 6f, 1f, StepColor);
                    MakeBlock("TP", 10f, 9.5f, 4f, 1f, StepColor);
                    highDoor = new Vector2(20.5f, 8.5f);
                    topDoor = new Vector2(10f, 10.5f);
                    break;
            }

            Vector2[] slots = { new Vector2(20.5f, 2.5f), highDoor, new Vector2(1.5f, 2.5f), topDoor };
            BuildDoors(scene, room, slots);
        }

        private static void BuildDoors(Scene scene, RoomData room, Vector2[] slots)
        {
            for (int k = 0; k < room.neighbors.Length && k < slots.Length; k++)
            {
                string target = room.neighbors[k];
                Vector2 pos = slots[k];
                GameObject door = new GameObject($"Door_{target}");
                door.transform.position = new Vector3(pos.x, pos.y, 0f);

                BoxCollider2D col = door.AddComponent<BoxCollider2D>();
                col.isTrigger = true;
                col.size = new Vector2(1.2f, 2f);

                LevelExit exit = door.AddComponent<LevelExit>();
                SerializedObject soExit = new SerializedObject(exit);
                // The id the graph declares, which is normally the target room name but may be an
                // explicit one. The label and the Spawn_ checkpoint below stay keyed to the room name:
                // that is what RespawnDirector.FindDoorSpawn looks for, regardless of the exit's id.
                soExit.FindProperty("exitId").stringValue = room.exitIds[k];
                soExit.ApplyModifiedPropertiesWithoutUndo();

                // Label above the door; the top door's label hangs below it to stay on-screen.
                // Plain scene name, no arrow: the custom font has no → glyph.
                GameObject labelGo = new GameObject("Label");
                labelGo.transform.SetParent(door.transform, false);
                float labelOffset = k == 3 ? -2f : 2f;
                labelGo.transform.localPosition = new Vector3(0f, labelOffset, -1f);
                MakeLabel(labelGo, target, 36, 0.025f);

                // Door-side spawn point (Checkpoint, not a start point): entering from `target`
                // spawns the player beside this door instead of the room's start point. Placed on the
                // inside floor, ≥0.9 from the door trigger (1.2 wide) so spawning cannot re-trigger it.
                float dx = k == 2 ? 1.5f : -1.5f;   // the left-wall door opens inward to the right; the rest open left
                Instantiate(scene, CheckpointPrefabPath, $"Spawn_{target}", new Vector3(pos.x + dx, pos.y - 0.5f, 0f));
            }
        }

        private static void BuildRoomLabel(RoomData room)
        {
            GameObject labelGo = new GameObject("RoomLabel");
            labelGo.transform.position = new Vector3(2.5f, 9.5f, -1f);
            MakeLabel(labelGo, $"{room.sceneName}\n{room.levelName}", 48, 0.05f);
        }

        /// <summary>World-space canvas + uGUI Text label — the same font-material mechanism the
        /// project's menus use. Legacy TextMesh serializes font.material as a dead builtin reference
        /// (text invisible until the font is manually re-picked); uGUI Text resolves the font material
        /// at runtime, so generated labels survive scene reloads. Text world height ≈ fontSize × scale.</summary>
        private static void MakeLabel(GameObject go, string text, int fontSize, float canvasScale)
        {
            Canvas canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            go.transform.localScale = Vector3.one * canvasScale;

            GameObject textGo = new GameObject("Text");
            textGo.transform.SetParent(go.transform, false);
            Text t = textGo.AddComponent<Text>();
            t.font = LegacyFont;
            t.text = text;
            t.fontSize = fontSize;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.color = Color.white;
            textGo.GetComponent<RectTransform>().sizeDelta = new Vector2(600f, 100f);
        }

        /// <summary>A plain colored rectangle: a 1×1 white sprite scaled to (w,h) with a BoxCollider2D
        /// of the same size. Default-shape stand-in for the tilemap wall/spike prefabs.</summary>
        private static void MakeBlock(string name, float cx, float cy, float w, float h, Color color)
        {
            GameObject block = new GameObject(name);
            block.transform.position = new Vector3(cx, cy, 0f);
            block.transform.localScale = new Vector3(w, h, 1f);
            block.layer = 6;    // Terrain: ContactSensor's ground mask is (1<<6)|(1<<11)

            SpriteRenderer sr = block.AddComponent<SpriteRenderer>();
            sr.sprite = WhiteSprite;
            sr.color = color;

            BoxCollider2D col = block.AddComponent<BoxCollider2D>();
            col.size = Vector2.one;     // local; the transform scale makes it (w,h) in world space
        }

        /// <summary>A 1×1 world-unit white sprite asset, generated on first use and persisted so the
        /// scene's SpriteRenderer references survive a reload (a transient Sprite.Create sprite would
        /// serialize a dead fileID and come back as Missing). The built-in skin sprites
        /// ("UI/Skin/UISprite.psd") no longer exist in Unity 6000 — don't reach for GetBuiltinResource.</summary>
        private static Sprite cachedWhite;
        private static Sprite WhiteSprite
        {
            get
            {
                if (cachedWhite == null)
                {
                    cachedWhite = AssetDatabase.LoadAssetAtPath<Sprite>(WhitePixelPath);
                    if (cachedWhite == null)
                    {
                        EnsureFolder(GeneratedArtDir, "Assets/Art");
                        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                        tex.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
                        tex.Apply();
                        File.WriteAllBytes(WhitePixelPath, tex.EncodeToPNG());
                        Object.DestroyImmediate(tex);
                        AssetDatabase.ImportAsset(WhitePixelPath);
                        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(WhitePixelPath);
                        importer.textureType = TextureImporterType.Sprite;
                        importer.spritePixelsPerUnit = 2f;      // 2px texture => 1 world unit, matches MakeBlock's scale math
                        importer.textureCompression = TextureImporterCompression.Uncompressed;
                        importer.SaveAndReimport();
                        cachedWhite = AssetDatabase.LoadAssetAtPath<Sprite>(WhitePixelPath);
                    }
                }
                return cachedWhite;
            }
        }

        private static GameObject Instantiate(Scene scene, string prefabPath, string name, Vector3 position)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogError($"RoomBuilder: missing prefab at {prefabPath}");
                return null;
            }
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.name = name;
            instance.transform.position = position;
            return instance;
        }

        // ---- Helpers ----

        private static void EnsureFolder(string folder, string parent)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            AssetDatabase.CreateFolder(parent, folder.Substring(folder.LastIndexOf('/') + 1));
        }

        /// <summary>Fills a serialized object-reference slot only when it is currently empty, so a
        /// hand-made assignment survives the next build (same stance as UIBuilder.AssignIfEmpty).</summary>
        private static void AssignIfEmpty(SerializedObject so, string propertyName, Object value)
        {
            SerializedProperty property = so.FindProperty(propertyName);
            if (property == null) return;
            if (property.objectReferenceValue != null) return;
            property.objectReferenceValue = value;
        }

        /// <summary>Project font first, built-in fallbacks after — TextMesh takes the same Font asset
        /// type as uGUI Text. Local copy of UIBuilder.FontUtils (which is private to that class).</summary>
        private static Font cachedFont;
        private static Font LegacyFont
        {
            get
            {
                if (cachedFont == null)
                {
                    cachedFont = AssetDatabase.LoadAssetAtPath<Font>(PanelFontPath);
                    if (cachedFont == null) cachedFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                    if (cachedFont == null) cachedFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
                }
                return cachedFont;
            }
        }
    }
}
