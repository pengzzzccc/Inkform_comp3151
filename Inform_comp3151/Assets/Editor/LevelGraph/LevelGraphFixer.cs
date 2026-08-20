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
            GameObject created = ApplySceneFix(finding);

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

            // Asset-level repairs all collapse into one Sync, so run it at most once no matter how many
            // findings asked for it.
            if (assetFixes.Count > 0)
            {
                LevelGraphSync.Apply(doc);
                fixedCount += assetFixes.Count;

                foreach (Finding f in assetFixes)
                {
                    if (f.Fix == FixAction.AddSceneToBuild) AddSceneToBuild(f.Room);
                    else if (f.Fix == FixAction.MoveMenuSceneFirst) MoveMenuSceneFirst(doc.MenuScene);
                }
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
                        ApplySceneFix(f);
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

        private static bool IsAssetFix(FixAction fix) =>
            fix == FixAction.CreateLevelSceneAsset
            || fix == FixAction.FixSceneNameField
            || fix == FixAction.FixDisplayNameField
            || fix == FixAction.SyncLevelFlow
            || fix == FixAction.AddSceneToBuild
            || fix == FixAction.MoveMenuSceneFirst;

        /// <summary>
        /// Makes every room scene carry the wiring its edges require: for each directed link A→B, scene
        /// A gets a LevelExit with exitId B, scene B gets a checkpoint named Spawn_A, and every room
        /// with any edge gets a start point. Only ever creates what is missing — existing doors,
        /// checkpoints and start points are left alone, so the call is idempotent.
        ///
        /// This is the batch equivalent of working the findings list after Deep Validate, called by the
        /// window's "Apply Level Topology" button so adopting a hand-authored level is one click.
        /// Returns how many objects were created.
        /// </summary>
        public static int EnsureSceneWiring(LevelGraphDocument doc)
        {
            if (doc == null) return 0;

            // Collect the per-scene wish list from the directed expansion, so each scene is opened
            // exactly once no matter how many edges touch it.
            var doors = new Dictionary<string, HashSet<string>>();      // room -> exit ids its scene must expose
            var spawns = new Dictionary<string, HashSet<string>>();     // room -> source rooms it needs Spawn_ checkpoints for
            var roomsWithEdges = new HashSet<string>();

            foreach (DirectedLink link in doc.EnumerateDirected())
            {
                roomsWithEdges.Add(link.From);
                roomsWithEdges.Add(link.To);

                if (!doors.TryGetValue(link.From, out HashSet<string> ids))
                {
                    ids = new HashSet<string>();
                    doors[link.From] = ids;
                }
                ids.Add(link.ExitId);

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

                    if (doors.TryGetValue(room, out HashSet<string> exitIds))
                    {
                        foreach (string exitId in exitIds)
                        {
                            if (FindLevelExit(exitId) != null) continue;

                            var finding = new Finding { Room = room, ExitId = exitId, Code = "C15" };
                            CreateLevelExit(finding);
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

                            CreateCheckpoint(want, false);
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

                default:
                    // Every asset-shaped repair is "make the assets match the graph", which is exactly
                    // what Sync already does idempotently.
                    LevelGraphSync.Apply(doc);
                    break;
            }
        }

        private static GameObject ApplySceneFix(Finding finding)
        {
            switch (finding.Fix)
            {
                case FixAction.CreateLevelExit: return CreateLevelExit(finding);
                case FixAction.CreateDoorSpawn: return CreateCheckpoint($"Spawn_{finding.Other}", false);
                case FixAction.CreateStartPoint: return CreateCheckpoint("Checkpoint_Start", true);
                case FixAction.MakeColliderTrigger: return MakeColliderTrigger(finding);
                default: return null;
            }
        }

        // Mirrors the door RoomBuilder generates: same trigger size, same exitId convention, same
        // naming — so a hand-repaired door is indistinguishable from a generated one.
        private static GameObject CreateLevelExit(Finding finding)
        {
            var door = new GameObject($"Door_{finding.ExitId}");
            Undo.RegisterCreatedObjectUndo(door, "Create Level Exit");

            var collider = door.AddComponent<BoxCollider2D>();
            collider.isTrigger = true;
            collider.size = new Vector2(1.2f, 2f);

            LevelExit exit = door.AddComponent<LevelExit>();
            var so = new SerializedObject(exit);
            so.FindProperty("exitId").stringValue = finding.ExitId;
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
