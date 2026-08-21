using System.Collections.Generic;
using Inkform.Level;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Inkform.LevelGraph.EditorTools
{
    /// <summary>
    /// The validation rules that need the project, not just the graph: build settings (B) and scenes
    /// (C). Split from LevelGraphValidator because these need LevelExit / Checkpoint, which live in
    /// the predefined Assembly-CSharp — an assembly definition cannot reference that, so the pure
    /// rules and these cannot share an assembly. The upside is that the pure half stays testable.
    ///
    /// Split by cost, which is what the window cares about:
    ///   ValidateProject — build settings and scene-file presence. Milliseconds; runs on every refresh.
    ///   ValidateScenes  — opens every room scene. Seconds; runs only when asked.
    ///
    /// A connection A→B is really a five-part contract, and until this class existed none of it was
    /// checked: the link in the graph, a LevelExit in A carrying the right id, a Spawn_A checkpoint
    /// in B, B present in Build Settings, and B's scene file existing. Break any one and the only
    /// symptom is a warning at the moment a player walks into that door.
    /// </summary>
    public static class LevelGraphProjectValidator
    {
        // ---- B: build settings ----

        public static List<Finding> ValidateProject(LevelGraphDocument doc)
        {
            var findings = new List<Finding>();
            if (doc == null) return findings;

            foreach (RoomEntry room in doc.Rooms)
            {
                // C12 lives here rather than in ValidateScenes: knowing the file is missing costs one
                // lookup, and there is no point paying to open the other 21 scenes to find that out.
                if (LevelGraphFile.ScenePathFor(room.Name) == null)
                {
                    findings.Add(new Finding
                    {
                        Severity = Severity.Error,
                        Code = "C12",
                        Room = room.Name,
                        Message = $"No scene file named '{room.Name}.unity' anywhere in the project. "
                                + "Create or adopt that scene before applying the graph.",
                    });
                }
            }

            CheckBuildSettings(doc, findings);

            return findings;
        }

        // C13 / C14 — SceneManager.LoadScene matches by name and only sees scenes in Build Settings.
        private static void CheckBuildSettings(LevelGraphDocument doc, List<Finding> findings)
        {
            var inBuild = new HashSet<string>();
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;

            foreach (EditorBuildSettingsScene s in scenes)
            {
                if (s.enabled) inBuild.Add(System.IO.Path.GetFileNameWithoutExtension(s.path));
            }

            foreach (RoomEntry room in doc.Rooms)
            {
                if (inBuild.Contains(room.Name)) continue;
                if (LevelGraphFile.ScenePathFor(room.Name) == null) continue;   // already reported as C12

                findings.Add(new Finding
                {
                    Severity = Severity.Error,
                    Code = "C13",
                    Room = room.Name,
                    Message = $"'{room.Name}' is not in Build Settings — loading it would silently do nothing.",
                    Fix = FixAction.AddSceneToBuild,
                });
            }

            if (string.IsNullOrEmpty(doc.MenuScene)) return;

            bool menuFirst = scenes.Length > 0
                && System.IO.Path.GetFileNameWithoutExtension(scenes[0].path) == doc.MenuScene
                && scenes[0].enabled;

            if (!menuFirst)
            {
                findings.Add(new Finding
                {
                    Severity = Severity.Warning,
                    Code = "C14",
                    Room = doc.MenuScene,
                    Message = $"'{doc.MenuScene}' is not the first enabled scene in Build Settings — the build would boot elsewhere.",
                    Fix = FixAction.MoveMenuSceneFirst,
                });
            }
        }

        // ---- C: scenes ----

        /// <summary>
        /// Opens every room scene and checks the door wiring. Slow and destructive to the current scene
        /// setup, so the caller must have saved first; the setup is restored at the end.
        ///
        /// Opens scenes rather than scanning their YAML on purpose. Checkpoints are prefab instances —
        /// a scene file contains the Checkpoint *prefab's* GUID, never the script's — so a text scan
        /// finds zero of them and would cheerfully report every door spawn as missing.
        /// </summary>
        public static List<Finding> ValidateScenes(LevelGraphDocument doc)
        {
            var findings = new List<Finding>();
            if (doc == null || doc.Rooms.Count == 0) return findings;

            // Exits the graph expects out of each room, and door spawns each room should offer.
            var expectedExits = new Dictionary<string, HashSet<string>>();
            var expectedSpawns = new Dictionary<string, HashSet<string>>();

            foreach (RoomEntry room in doc.Rooms)
            {
                expectedExits[room.Name] = new HashSet<string>();
                expectedSpawns[room.Name] = new HashSet<string>();
            }

            foreach (DirectedLink link in doc.EnumerateDirected())
            {
                if (expectedExits.TryGetValue(link.From, out HashSet<string> exits)) exits.Add(link.ExitId);
                if (expectedSpawns.TryGetValue(link.To, out HashSet<string> spawns)) spawns.Add(link.From);
            }

            SceneSetup[] setup = EditorSceneManager.GetSceneManagerSetup();

            try
            {
                for (int i = 0; i < doc.Rooms.Count; i++)
                {
                    RoomEntry room = doc.Rooms[i];
                    string path = LevelGraphFile.ScenePathFor(room.Name);
                    if (path == null) continue;     // C12, already reported by ValidateProject

                    if (EditorUtility.DisplayCancelableProgressBar(
                            "Validating scenes", $"{room.Name}  ({i + 1}/{doc.Rooms.Count})",
                            (i + 1) / (float)doc.Rooms.Count))
                        break;

                    Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                    InspectScene(scene, room.Name, expectedExits[room.Name], expectedSpawns[room.Name], findings);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                if (setup != null && setup.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(setup);
            }

            return findings;
        }

        private static void InspectScene(Scene scene, string roomName,
            HashSet<string> expectedExits, HashSet<string> expectedSpawns, List<Finding> findings)
        {
            var foundExits = new HashSet<string>();

            foreach (LevelExit exit in Object.FindObjectsByType<LevelExit>(FindObjectsInactive.Include))
            {
                string id = ReadString(exit, "exitId");
                foundExits.Add(id);

                // C17 — both LevelExit and Checkpoint only declare RequireComponent(Collider2D); nothing
                // anywhere forces isTrigger, and both work exclusively through OnTriggerEnter2D. A
                // non-trigger collider is a door that can never open, with no error to go on.
                var collider = exit.GetComponent<Collider2D>();
                if (collider != null && !collider.isTrigger)
                {
                    findings.Add(new Finding
                    {
                        Severity = Severity.Error,
                        Code = "C17",
                        Room = roomName,
                        ExitId = id,
                        Message = $"LevelExit '{exit.name}' in '{roomName}' has a non-trigger collider — it can never fire.",
                        Fix = FixAction.MakeColliderTrigger,
                    });
                }

                // C16 — an exit the graph does not know about. Reported, never removed: it is far more
                // likely to be a secret door someone placed than a mistake, and the tool cannot tell.
                if (!expectedExits.Contains(id))
                {
                    findings.Add(new Finding
                    {
                        Severity = Severity.Warning,
                        Code = "C16",
                        Room = roomName,
                        ExitId = id,
                        Message = $"'{roomName}' has a LevelExit with id '{id}' that the graph has no link for "
                                + "(left alone — add the link, or delete the door by hand).",
                    });
                }
            }

            // C15 — the graph promises a door that the scene does not have.
            foreach (string id in expectedExits)
            {
                if (foundExits.Contains(id)) continue;

                findings.Add(new Finding
                {
                    Severity = Severity.Error,
                    Code = "C15",
                    Room = roomName,
                    ExitId = id,
                    Message = $"'{roomName}' is missing a LevelExit with id '{id}'.",
                    Fix = FixAction.CreateLevelExit,
                });
            }

            var checkpointNames = new HashSet<string>();
            bool hasStartPoint = false;

            foreach (Checkpoint checkpoint in Object.FindObjectsByType<Checkpoint>(FindObjectsInactive.Include))
            {
                checkpointNames.Add(checkpoint.gameObject.name);
                if (checkpoint.IsStartPoint) hasStartPoint = true;

                // C20 — same trigger trap as C17.
                var collider = checkpoint.GetComponent<Collider2D>();
                if (collider != null && !collider.isTrigger)
                {
                    findings.Add(new Finding
                    {
                        Severity = Severity.Error,
                        Code = "C20",
                        Room = roomName,
                        Other = checkpoint.gameObject.name,
                        Message = $"Checkpoint '{checkpoint.gameObject.name}' in '{roomName}' has a non-trigger collider — it can never fire.",
                        Fix = FixAction.MakeColliderTrigger,
                    });
                }
            }

            // C18 — RespawnDirector looks for a checkpoint literally named Spawn_<sourceScene>; without
            // it the player entering from that door lands on the level start point instead.
            foreach (string from in expectedSpawns)
            {
                string wanted = $"Spawn_{from}";
                if (checkpointNames.Contains(wanted)) continue;

                findings.Add(new Finding
                {
                    Severity = Severity.Warning,
                    Code = "C18",
                    Room = roomName,
                    Other = from,
                    Message = $"'{roomName}' has no '{wanted}' checkpoint — arriving from '{from}' would spawn at the level start instead of the door.",
                    Fix = FixAction.CreateDoorSpawn,
                });
            }

            // C19 — with no start point, RespawnDirector falls back to wherever the player prefab sits.
            if (!hasStartPoint)
            {
                findings.Add(new Finding
                {
                    Severity = Severity.Warning,
                    Code = "C19",
                    Room = roomName,
                    Message = $"'{roomName}' has no checkpoint marked isStartPoint — respawns fall back to the player's placed position.",
                    Fix = FixAction.CreateStartPoint,
                });
            }
        }

        /// <summary>Reads a private [SerializeField] without adding a public accessor to the runtime
        /// class — this is a pure editor tool and the plan was to leave runtime code alone.</summary>
        internal static string ReadString(Object target, string property)
        {
            SerializedProperty p = new SerializedObject(target).FindProperty(property);
            return p != null ? p.stringValue : "";
        }
    }
}
