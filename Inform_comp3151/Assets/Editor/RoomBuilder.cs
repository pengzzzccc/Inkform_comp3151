using System.Collections.Generic;
using System.IO;
using Inkform.Fx;
using Inkform.Input;
using Inkform.Level;
using Inkform.Player;
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

        // ---- Room data (Docs/LevelGenerationPlan.md §3.2) ----
        // sceneName doubles as the LevelScene asset name, the scene file name, the door's exitId and
        // the door label's destination — one string, four places, no spelling drift.

        private sealed class RoomData
        {
            public readonly string sceneName;
            public readonly string levelName;
            public readonly string[] neighbors;

            public RoomData(string sceneName, string levelName, params string[] neighbors)
            {
                this.sceneName = sceneName;
                this.levelName = levelName;
                this.neighbors = neighbors;
            }
        }

        private const string Level1Name = "a laboratory in a cave";
        private const string Level2Name = "Mountain tunnel";
        private const string Level3Name = "Battle Corridor";

        private static readonly RoomData[] Rooms =
        {
            new RoomData("L1_Player", Level1Name, "L1_S1", "L1_B1", "L1_B5"),
            new RoomData("L1_Boss", Level1Name, "L1_B5", "L1_B6", "L2_B1"),
            new RoomData("L1_B1", Level1Name, "L1_Player", "L1_B2", "L1_B3", "L1_S2"),
            new RoomData("L1_B2", Level1Name, "L1_B1", "L1_S3"),
            new RoomData("L1_B3", Level1Name, "L1_B1", "L1_B4", "L1_B5", "L1_B6"),
            new RoomData("L1_B4", Level1Name, "L1_B3", "L1_S4"),
            new RoomData("L1_B5", Level1Name, "L1_Player", "L1_B3", "L1_Boss"),
            new RoomData("L1_B6", Level1Name, "L1_S3", "L1_B3", "L1_Boss"),
            new RoomData("L1_S1", Level1Name, "L1_Player"),
            new RoomData("L1_S2", Level1Name, "L1_B1"),
            new RoomData("L1_S3", Level1Name, "L1_B2", "L1_B6"),
            new RoomData("L1_S4", Level1Name, "L1_B4", "L2_S1"),
            new RoomData("L2_Boss", Level2Name, "L2_B6", "L2_S2", "L3_LongFight"),
            new RoomData("L2_B1", Level2Name, "L2_B3", "L2_S1", "L1_Boss"),
            new RoomData("L2_B2", Level2Name, "L2_S1", "L2_S2"),
            new RoomData("L2_B3", Level2Name, "L2_B1", "L2_B4"),
            new RoomData("L2_B4", Level2Name, "L2_B3", "L2_B5", "L2_S1", "L2_S2"),
            new RoomData("L2_B5", Level2Name, "L2_B4", "L2_B6", "L2_S2"),
            new RoomData("L2_B6", Level2Name, "L2_B5", "L2_Boss"),
            new RoomData("L2_S1", Level2Name, "L2_B1", "L2_B4", "L2_B2", "L1_S4"),
            new RoomData("L2_S2", Level2Name, "L2_B4", "L2_B5", "L2_B2", "L2_Boss"),
            new RoomData("L3_LongFight", Level3Name, "L2_Boss"),
        };

        // ---- Menu entries ----

        [MenuItem("Tools/Inkform/Room Builder/Build All Rooms")]
        public static void BuildAllRooms()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EnsureFolder(ScenesDir, "Assets/Scenes");
            foreach (RoomData room in Rooms) BuildRoom(room);
            AssetDatabase.SaveAssets();
            Debug.Log($"RoomBuilder: built {Rooms.Length} room scenes under {ScenesDir}/");
        }

        [MenuItem("Tools/Inkform/Room Builder/Build Level Graph")]
        public static void BuildLevelGraph()
        {
            EnsureFolder(LevelsDir, ScenesDir);

            // Pass 1: create-or-load every LevelScene asset so connection targets exist before wiring.
            // sceneName goes through Unity's SerializedObject pipeline — a plain field assignment +
            // SetDirty once failed to persist the string (all assets were saved with an empty
            // sceneName, silently killing FindBySceneName at runtime); the verify pass below catches
            // any future loss instead of letting the graph die quietly.
            var assets = new Dictionary<string, LevelScene>();
            foreach (RoomData room in Rooms)
            {
                string path = $"{LevelsDir}/{room.sceneName}.asset";
                LevelScene level = AssetDatabase.LoadAssetAtPath<LevelScene>(path);
                if (level == null)
                {
                    level = ScriptableObject.CreateInstance<LevelScene>();
                    level.name = room.sceneName;
                    level.sceneName = room.sceneName;   // set before CreateAsset so the first write is already correct
                    AssetDatabase.CreateAsset(level, path);
                }
                SerializedObject so = new SerializedObject(level);
                so.FindProperty("sceneName").stringValue = room.sceneName;
                so.ApplyModifiedPropertiesWithoutUndo();
                assets[room.sceneName] = level;
            }

            // Pass 2: rebuild every connection list from the data table
            foreach (RoomData room in Rooms)
            {
                LevelScene level = assets[room.sceneName];
                var connections = new List<LevelConnection>(room.neighbors.Length);
                foreach (string neighbor in room.neighbors) connections.Add(new LevelConnection { id = neighbor, target = assets[neighbor] });
                level.connections = connections.ToArray();
                EditorUtility.SetDirty(level);
            }

            // LevelFlow: create-or-load; entryLevel and levels are ours to set, mainMenuSceneName is not
            LevelFlow flow = AssetDatabase.LoadAssetAtPath<LevelFlow>(FlowAssetPath);
            if (flow == null)
            {
                flow = ScriptableObject.CreateInstance<LevelFlow>();
                AssetDatabase.CreateAsset(flow, FlowAssetPath);
            }
            flow.entryLevel = assets["L1_Player"];
            var all = new List<LevelScene>(Rooms.Length);
            foreach (RoomData room in Rooms) all.Add(assets[room.sceneName]);
            flow.levels = all.ToArray();
            EditorUtility.SetDirty(flow);
            AssetDatabase.SaveAssets();

            // Verify sceneName persisted to disk: read the asset file directly — LoadAssetAtPath
            // returns the in-memory copy (always holding the value just written), which cannot catch
            // a lost write. The graph is dead without sceneName, so fail loudly.
            var missing = new List<string>();
            foreach (RoomData room in Rooms)
            {
                string assetPath = $"{LevelsDir}/{room.sceneName}.asset";
                string fullPath = System.IO.Path.Combine(Application.dataPath, assetPath.Substring("Assets/".Length));
                string yaml = System.IO.File.Exists(fullPath) ? System.IO.File.ReadAllText(fullPath) : string.Empty;
                if (!yaml.Contains($"sceneName: {room.sceneName}")) missing.Add(room.sceneName);
            }
            if (missing.Count > 0)
                Debug.LogError($"RoomBuilder: sceneName did not persist for {missing.Count} asset(s): {string.Join(", ", missing)}");
            else
                Debug.Log($"RoomBuilder: level graph wired — {Rooms.Length} LevelScene assets, flow at {FlowAssetPath}");
        }

        [MenuItem("Tools/Inkform/Room Builder/Wire GameManager Flow")]
        public static void WireGameManagerFlow()
        {
            LevelFlow flow = AssetDatabase.LoadAssetAtPath<LevelFlow>(FlowAssetPath);
            if (flow == null || flow.levels == null || flow.levels.Length == 0)
            {
                Debug.LogWarning("RoomBuilder: run 'Build Level Graph' first — flow asset has no levels yet");
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
                string path = $"{ScenesDir}/{room.sceneName}.unity";
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
        /// "has not been added to the build settings" even though the global list contains the scene.</summary>
        private static int AppendRoomsToBuildProfile()
        {
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
                    string scenePath = $"{ScenesDir}/{room.sceneName}.unity";
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

        [MenuItem("Tools/Inkform/Room Builder/Build All")]
        public static void BuildAll()
        {
            BuildAllRooms();
            BuildLevelGraph();
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

            GameObject gameManager = Instantiate(scene, GameManagerPrefabPath, "GameManager", new Vector3(0f, 0f, 0f));
            GameObject player = Instantiate(scene, PlayerPrefabPath, "Player", new Vector3(4.5f, 2.5f, 0f));
            GameObject camera = Instantiate(scene, CameraPrefabPath, "Main Camera", new Vector3(10f, 5.5f, -10f));
            Instantiate(scene, VolumePrefabPath, "GlobalVolume", new Vector3(10f, 5.5f, 0f));

            WirePlayerReferences(gameManager, player, camera);

            BuildChallenge(room);

            GameObject checkpoint = Instantiate(scene, CheckpointPrefabPath, "Checkpoint", new Vector3(4.5f, 2f, 0f));
            SerializedObject soCp = new SerializedObject(checkpoint.GetComponent<Checkpoint>());
            soCp.FindProperty("isStartPoint").boolValue = true;
            soCp.ApplyModifiedPropertiesWithoutUndo();

            BuildRoomLabel(room);

            EditorSceneManager.SaveScene(scene, $"{ScenesDir}/{room.sceneName}.unity");
        }

        /// <summary>
        /// Scene-instance wiring the hand-built scenes carry: the follow camera must be the player's
        /// child with CamHandler.target set, and the GameManager's InputHandler.player /
        /// AniHandler.animations must point at THIS scene's Player — the prefab's cross-prefab
        /// references (into Player.prefab) do not remap to the scene instance on load, leaving the
        /// animator undriven (player never animates) and the camera stationary otherwise.
        /// </summary>
        private static void WirePlayerReferences(GameObject gameManager, GameObject player, GameObject camera)
        {
            camera.transform.SetParent(player.transform, true);
            CamHandler cam = camera.GetComponent<CamHandler>();
            if (cam != null)
            {
                SerializedObject soCam = new SerializedObject(cam);
                soCam.FindProperty("target").objectReferenceValue = player.transform;
                soCam.ApplyModifiedPropertiesWithoutUndo();
            }

            InputHandler input = gameManager.GetComponent<InputHandler>();
            if (input != null)
            {
                SerializedObject soIn = new SerializedObject(input);
                soIn.FindProperty("player").objectReferenceValue = player.GetComponent<PlayerHandler>();
                soIn.ApplyModifiedPropertiesWithoutUndo();
            }

            AniHandler ani = gameManager.GetComponent<AniHandler>();
            if (ani != null)
            {
                SerializedObject soAni = new SerializedObject(ani);
                soAni.FindProperty("animations").objectReferenceValue = player.GetComponent<Animator>();
                soAni.ApplyModifiedPropertiesWithoutUndo();
            }
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
        private static void BuildChallenge(RoomData room)
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
            BuildDoors(room, slots);
        }

        private static void BuildDoors(RoomData room, Vector2[] slots)
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
                soExit.FindProperty("exitId").stringValue = target;
                soExit.ApplyModifiedPropertiesWithoutUndo();

                // Label above the door; the top door's label hangs below it to stay on-screen.
                // Plain scene name, no arrow: the custom font has no → glyph.
                GameObject labelGo = new GameObject("Label");
                labelGo.transform.SetParent(door.transform, false);
                float labelOffset = k == 3 ? -2f : 2f;
                labelGo.transform.localPosition = new Vector3(0f, labelOffset, -1f);
                MakeLabel(labelGo, target, 36, 0.025f);
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
