using System.Collections.Generic;
using Inkform.EditorTools;
using Inkform.Level;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Inkform.LevelGraph.EditorTools
{
    /// <summary>
    /// Repairs findings. Everything here is **additive**: it creates the door the graph says is
    /// missing, adds the missing Build Settings entry, ticks isTrigger back on. It never deletes a
    /// LevelExit, never removes a checkpoint, and never regenerates a scene.
    ///
    /// That restraint is the whole design. The existing RoomBuilder rebuilds room scenes from scratch
    /// and saves over them, so any hand authoring in a generated room is lost on the next run — a tool
    /// that can destroy a designer's work is a tool a designer cannot use. This one is only ever
    /// allowed to add.
    ///
    /// Created objects land at the scene origin and get selected, because the tool knows a door is
    /// missing but has no idea where the doorway is. Placing it is the designer's call; the tool's job
    /// is to remove the tedium of creating and wiring it.
    /// </summary>
    public static class LevelGraphFixer
    {
        private const string CheckpointPrefabPath = "Assets/Prefabs/env/Checkpoint.prefab";

        /// <summary>
        /// Fixes one finding and leaves its scene open with the new object selected, ready to be
        /// dragged into place.
        /// </summary>
        public static bool FixOne(Finding finding, LevelGraphDocument doc)
        {
            if (finding == null || !finding.CanFix) return false;

            if (IsAssetFix(finding.Fix))
            {
                ApplyAssetFix(finding, doc);
                return true;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;

            string path = LevelGraphFile.ScenePathFor(finding.Room);
            if (path == null)
            {
                Debug.LogWarning($"Level graph: cannot fix '{finding.Code}' — no scene file for '{finding.Room}'.");
                return false;
            }

            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            GameObject created = ApplySceneFix(finding, doc);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            if (created != null)
            {
                Selection.activeGameObject = created;
                EditorGUIUtility.PingObject(created);
                SceneView.FrameLastActiveSceneView();
            }

            return true;
        }

        /// <summary>
        /// Fixes many findings, opening each affected scene once. Restores whatever scene setup was
        /// open beforehand, so a bulk repair does not leave the editor somewhere unexpected.
        /// </summary>
        public static int FixAll(IEnumerable<Finding> findings, LevelGraphDocument doc)
        {
            var byScene = new Dictionary<string, List<Finding>>();
            var assetFixes = new List<Finding>();

            foreach (Finding f in findings)
            {
                if (f == null || !f.CanFix) continue;

                if (IsAssetFix(f.Fix)) { assetFixes.Add(f); continue; }

                if (!byScene.TryGetValue(f.Room, out List<Finding> list))
                {
                    list = new List<Finding>();
                    byScene[f.Room] = list;
                }
                list.Add(f);
            }

            int fixedCount = 0;

            // Build-settings repairs are file-level, no scene needs opening for them.
            if (assetFixes.Count > 0)
            {
                foreach (Finding f in assetFixes)
                {
                    if (f.Fix == FixAction.AddSceneToBuild) AddSceneToBuild(f.Room);
                    else if (f.Fix == FixAction.MoveMenuSceneFirst) MoveMenuSceneFirst(doc.MenuScene);
                }
                fixedCount += assetFixes.Count;
            }

            if (byScene.Count == 0) return fixedCount;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return fixedCount;

            SceneSetup[] setup = EditorSceneManager.GetSceneManagerSetup();

            try
            {
                int i = 0;
                foreach (KeyValuePair<string, List<Finding>> pair in byScene)
                {
                    string path = LevelGraphFile.ScenePathFor(pair.Key);
                    if (path == null) continue;

                    EditorUtility.DisplayProgressBar("Fixing scenes", pair.Key, ++i / (float)byScene.Count);

                    Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                    foreach (Finding f in pair.Value)
                    {
                        ApplySceneFix(f, doc);
                        fixedCount++;
                    }

                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                if (setup != null && setup.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(setup);
            }

            return fixedCount;
        }

        // The only file-level repairs left are build settings; everything else opens a scene.
        private static bool IsAssetFix(FixAction fix) =>
            fix == FixAction.AddSceneToBuild
            || fix == FixAction.MoveMenuSceneFirst;

        /// <summary>
        /// Makes every room scene carry the wiring its edges require: for each directed link A→B, scene
        /// A gets a LevelExit with exitId B (its gizmo says "→ B"), scene B gets a checkpoint named
        /// Spawn_A parented beside the door that leads back to A, and every room with any edge gets a
        /// start point. Only ever creates what is missing — existing doors, checkpoints and start
        /// points are left alone, so the call is idempotent.
        ///
        /// New doors line up in a row of slots (DoorSlot) instead of piling on the origin, and each
        /// new Spawn_ becomes a child of its paired door at a fixed local offset to the door's right —
        /// moving the door later carries the spawn with it, which was the old footgun: two objects to
        /// drag in sync, and a forgotten one meant arrivals landing nowhere near the door.
        ///
        /// Called by the window's "Apply Level Topology" button so adopting a hand-authored level is
        /// one click. Returns how many objects were created.
        /// </summary>
        public static int EnsureSceneWiring(LevelGraphDocument doc)
        {
            if (doc == null) return 0;

            // Collect the per-scene wish list from the directed expansion, so each scene is opened
            // exactly once no matter how many edges touch it. Doors remember their target scene too —
            // that is the destination the door's gizmo advertises.
            var doors = new Dictionary<string, Dictionary<string, string>>();   // room -> exitId -> target
            var spawns = new Dictionary<string, HashSet<string>>();            // room -> source rooms it needs Spawn_ checkpoints for
            var roomsWithEdges = new HashSet<string>();

            foreach (DirectedLink link in doc.EnumerateDirected())
            {
                roomsWithEdges.Add(link.From);
                roomsWithEdges.Add(link.To);

                if (!doors.TryGetValue(link.From, out Dictionary<string, string> map))
                {
                    map = new Dictionary<string, string>();
                    doors[link.From] = map;
                }
                map[link.ExitId] = link.To;

                if (!spawns.TryGetValue(link.To, out HashSet<string> sources))
                {
                    sources = new HashSet<string>();
                    spawns[link.To] = sources;
                }
                sources.Add(link.From);
            }

            int created = 0;
            if (roomsWithEdges.Count == 0) return created;

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return created;

            SceneSetup[] setup = EditorSceneManager.GetSceneManagerSetup();

            try
            {
                int i = 0;
                foreach (string room in roomsWithEdges)
                {
                    string path = LevelGraphFile.ScenePathFor(room);
                    if (path == null) continue;     // no scene yet (e.g. NewRoom) — nothing to wire

                    EditorUtility.DisplayProgressBar("Wiring scenes", room, ++i / (float)roomsWithEdges.Count);

                    Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                    bool dirty = false;
                    int slot = 0;

                    if (doors.TryGetValue(room, out Dictionary<string, string> exits))
                    {
                        foreach (KeyValuePair<string, string> edge in exits)
                        {
                            if (FindLevelExit(edge.Key) != null) continue;

                            var finding = new Finding { Room = room, ExitId = edge.Key, Other = edge.Value };
                            CreateLevelExit(finding, DoorSlot(slot++));
                            created++;
                            dirty = true;
                        }
                    }

                    if (spawns.TryGetValue(room, out HashSet<string> sources))
                    {
                        foreach (string source in sources)
                        {
                            string want = $"Spawn_{source}";
                            if (FindCheckpoint(want) != null) continue;

                            GameObject spawn = CreateCheckpoint(want, false);

                            // Pair the spawn with the door that leads back to where the player came
                            // from. With the default id convention that door's exitId is exactly the
                            // source room's name; a custom-id link with no matching door leaves the
                            // spawn at a root-level slot of its own.
                            LevelExit paired = FindLevelExit(source);
                            if (paired != null)
                            {
                                // To the door's right, clear of the trigger (0.6 half-width) by more
                                // than the 0.9 the plan demands, so arriving cannot re-touch the door
                                spawn.transform.SetParent(paired.transform, false);
                                spawn.transform.localPosition = new Vector3(1.6f, -0.5f, 0f);
                            }
                            else
                            {
                                spawn.transform.position = DoorSlot(slot++);
                            }

                            created++;
                            dirty = true;
                        }
                    }

                    if (FindStartPoint() == null)
                    {
                        CreateCheckpoint("Checkpoint_Start", true);
                        created++;
                        dirty = true;
                    }

                    if (dirty)
                    {
                        EditorSceneManager.MarkSceneDirty(scene);
                        EditorSceneManager.SaveScene(scene);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                if (setup != null && setup.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(setup);
            }

            return created;
        }

        private static LevelExit FindLevelExit(string exitId)
        {
            foreach (LevelExit exit in Object.FindObjectsByType<LevelExit>(FindObjectsInactive.Include))
            {
                if (LevelGraphProjectValidator.ReadString(exit, "exitId") == exitId) return exit;
            }
            return null;
        }

        private static Checkpoint FindCheckpoint(string name)
        {
            foreach (Checkpoint checkpoint in Object.FindObjectsByType<Checkpoint>(FindObjectsInactive.Include))
            {
                if (checkpoint.gameObject.name == name) return checkpoint;
            }
            return null;
        }

        private static Checkpoint FindStartPoint()
        {
            foreach (Checkpoint checkpoint in Object.FindObjectsByType<Checkpoint>(FindObjectsInactive.Include))
            {
                SerializedObject so = new SerializedObject(checkpoint);
                SerializedProperty p = so.FindProperty("isStartPoint");
                if (p != null && p.boolValue) return checkpoint;
            }
            return null;
        }

        private static void ApplyAssetFix(Finding finding, LevelGraphDocument doc)
        {
            switch (finding.Fix)
            {
                case FixAction.AddSceneToBuild:
                    AddSceneToBuild(finding.Room);
                    break;

                case FixAction.MoveMenuSceneFirst:
                    MoveMenuSceneFirst(doc.MenuScene);
                    break;
            }
        }

        private static GameObject ApplySceneFix(Finding finding, LevelGraphDocument doc)
        {
            switch (finding.Fix)
            {
                case FixAction.CreateLevelExit:
                    ResolveDestination(finding, doc);
                    return CreateLevelExit(finding, DoorSlot(0));

                case FixAction.CreateDoorSpawn:
                    return CreateCheckpoint($"Spawn_{finding.Other}", false);

                case FixAction.CreateStartPoint: return CreateCheckpoint("Checkpoint_Start", true);
                case FixAction.MakeColliderTrigger: return MakeColliderTrigger(finding);
                default: return null;
            }
        }

        // Fills finding.Other with the room the finding's edge leads to (empty for room-level
        // findings), so a created door can carry its destination for the Scene-view gizmo.
        private static void ResolveDestination(Finding finding, LevelGraphDocument doc)
        {
            if (finding == null || doc == null || string.IsNullOrEmpty(finding.Room)
                || string.IsNullOrEmpty(finding.ExitId) || !string.IsNullOrEmpty(finding.Other)) return;

            foreach (DirectedLink link in doc.EnumerateDirected())
            {
                if (link.From == finding.Room && link.ExitId == finding.ExitId)
                {
                    finding.Other = link.To;
                    return;
                }
            }
        }

        // Doors created in one scene line up in a row instead of piling on the origin: slot 0 at
        // (2, 0), each further door 3 units right. Dragging them apart is the designer's job; the
        // tool's job is that they never start stacked on top of each other.
        private static Vector3 DoorSlot(int index) => new Vector3(2f + index * 3f, 0f, 0f);

        // Mirrors the door RoomBuilder generates: same trigger size, same exitId convention, same
        // naming — so a hand-repaired door is indistinguishable from a generated one. `destination`
        // is display info for the door's Scene-view gizmo ("→ where this leads"); empty falls back
        // to the exitId, which by convention is the target scene's name anyway.
        private static GameObject CreateLevelExit(Finding finding, Vector3 position)
        {
            var door = new GameObject($"Door_{finding.ExitId}");
            door.transform.position = position;
            Undo.RegisterCreatedObjectUndo(door, "Create Level Exit");

            var collider = door.AddComponent<BoxCollider2D>();
            collider.isTrigger = true;
            collider.size = new Vector2(1.2f, 2f);

            LevelExit exit = door.AddComponent<LevelExit>();
            var so = new SerializedObject(exit);
            so.FindProperty("exitId").stringValue = finding.ExitId;
            if (!string.IsNullOrEmpty(finding.Other)) so.FindProperty("destination").stringValue = finding.Other;
            so.ApplyModifiedPropertiesWithoutUndo();

            return door;
        }

        private static GameObject CreateCheckpoint(string name, bool isStartPoint)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CheckpointPrefabPath);

            GameObject checkpoint = prefab != null
                ? (GameObject)PrefabUtility.InstantiatePrefab(prefab)
                : new GameObject(name, typeof(BoxCollider2D), typeof(Checkpoint));

            checkpoint.name = name;
            checkpoint.transform.position = Vector3.zero;
            Undo.RegisterCreatedObjectUndo(checkpoint, "Create Checkpoint");

            var component = checkpoint.GetComponent<Checkpoint>();
            if (component != null)
            {
                var so = new SerializedObject(component);
                SerializedProperty p = so.FindProperty("isStartPoint");
                if (p != null && p.boolValue != isStartPoint)
                {
                    p.boolValue = isStartPoint;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            var collider = checkpoint.GetComponent<Collider2D>();
            if (collider != null && !collider.isTrigger)
            {
                Undo.RecordObject(collider, "Make Trigger");
                collider.isTrigger = true;
            }

            return checkpoint;
        }

        private static GameObject MakeColliderTrigger(Finding finding)
        {
            // C17 identifies the door by its exit id; C20 identifies the checkpoint by object name.
            if (finding.Code == "C20")
            {
                foreach (Checkpoint checkpoint in Object.FindObjectsByType<Checkpoint>(FindObjectsInactive.Include))
                {
                    if (checkpoint.gameObject.name != finding.Other) continue;
                    return SetTrigger(checkpoint.GetComponent<Collider2D>(), checkpoint.gameObject);
                }
                return null;
            }

            foreach (LevelExit exit in Object.FindObjectsByType<LevelExit>(FindObjectsInactive.Include))
            {
                if (LevelGraphProjectValidator.ReadString(exit, "exitId") != finding.ExitId) continue;
                return SetTrigger(exit.GetComponent<Collider2D>(), exit.gameObject);
            }

            return null;
        }

        private static GameObject SetTrigger(Collider2D collider, GameObject owner)
        {
            if (collider == null) return null;

            Undo.RecordObject(collider, "Make Trigger");
            collider.isTrigger = true;
            EditorUtility.SetDirty(collider);
            return owner;
        }

        // ---- Build settings ----

        private static void AddSceneToBuild(string roomName)
        {
            string path = LevelGraphFile.ScenePathFor(roomName);
            if (path == null) return;

            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);

            foreach (EditorBuildSettingsScene s in scenes)
            {
                if (s.path != path) continue;

                // Present but disabled: enabling is still additive, and a disabled entry is the same
                // silent failure as a missing one.
                if (!s.enabled)
                {
                    s.enabled = true;
                    EditorBuildSettings.scenes = scenes.ToArray();
                }
                return;
            }

            scenes.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();

            // The global list is not the whole story on Unity 6000: a build profile with
            // m_OverrideGlobalSceneList replaces it, so the scene must land in the profiles too or
            // LoadScene still fails. RoomBuilder owns that write — reuse it rather than duplicate it.
            RoomBuilder.AppendRoomsToBuildProfile(roomName);
        }

        private static void MoveMenuSceneFirst(string menuSceneName)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);

            int index = scenes.FindIndex(s =>
                System.IO.Path.GetFileNameWithoutExtension(s.path) == menuSceneName);

            if (index < 0)
            {
                string path = LevelGraphFile.ScenePathFor(menuSceneName);
                if (path == null) return;

                scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            }
            else
            {
                EditorBuildSettingsScene entry = scenes[index];
                entry.enabled = true;
                scenes.RemoveAt(index);
                scenes.Insert(0, entry);
            }

            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
