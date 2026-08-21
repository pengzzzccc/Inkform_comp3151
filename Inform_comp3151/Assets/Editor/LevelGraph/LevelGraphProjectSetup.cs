using System.Collections.Generic;
using System.IO;
using Inkform.Level;
using UnityEditor;
using UnityEngine;

namespace Inkform.LevelGraph.EditorTools
{
    /// <summary>Project-level writes owned by the Level Graph workflow.</summary>
    public static class LevelGraphProjectSetup
    {
        private const string GameManagerPrefabPath = "Assets/Prefabs/Control/GameManager.prefab";
        private const string ProfilesDirectory = "Assets/Settings/Build Profiles";

        public static int Apply(LevelGraphDocument doc)
        {
            if (doc == null) return 0;
            WireGameManager();
            EnsureBuildSettings(doc);
            return EnsureBuildProfiles(doc);
        }

        public static void WireGameManager()
        {
            TextAsset graph = AssetDatabase.LoadAssetAtPath<TextAsset>(LevelGraphFile.Path);
            if (graph == null)
            {
                Debug.LogError($"Level Graph: cannot wire GameManager because {LevelGraphFile.Path} is missing.");
                return;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(GameManagerPrefabPath);
            try
            {
                SceneDirector director = root.GetComponent<SceneDirector>();
                if (director == null)
                {
                    Debug.LogError("Level Graph: GameManager prefab has no SceneDirector.");
                    return;
                }

                SerializedObject serialized = new SerializedObject(director);
                SerializedProperty property = serialized.FindProperty("levelGraphFile");
                if (property != null && property.objectReferenceValue != graph)
                {
                    property.objectReferenceValue = graph;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    PrefabUtility.SaveAsPrefabAsset(root, GameManagerPrefabPath);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        public static void EnsureBuildSettings(LevelGraphDocument doc)
        {
            var result = new List<EditorBuildSettingsScene>();
            string menuPath = LevelGraphFile.ScenePathFor(doc.MenuScene);
            if (!string.IsNullOrEmpty(menuPath)) result.Add(new EditorBuildSettingsScene(menuPath, true));

            foreach (EditorBuildSettingsScene existing in EditorBuildSettings.scenes)
            {
                if (existing.path == menuPath || result.Exists(x => x.path == existing.path)) continue;
                result.Add(existing);
            }

            foreach (RoomEntry room in doc.Rooms)
            {
                string path = LevelGraphFile.ScenePathFor(room.Name);
                if (string.IsNullOrEmpty(path)) continue;
                int index = result.FindIndex(x => x.path == path);
                if (index < 0) result.Add(new EditorBuildSettingsScene(path, true));
                else result[index] = new EditorBuildSettingsScene(path, true);
            }

            EditorBuildSettings.scenes = result.ToArray();
        }

        public static void EnsureSceneInBuild(LevelGraphDocument doc, string roomName)
        {
            if (doc == null || string.IsNullOrEmpty(roomName)) return;
            EnsureBuildSettings(doc);
            EnsureBuildProfiles(doc, roomName);
        }

        public static int EnsureBuildProfiles(LevelGraphDocument doc, string onlyRoom = null)
        {
            if (doc == null || !AssetDatabase.IsValidFolder(ProfilesDirectory)) return 0;
            int changed = 0;

            foreach (string file in Directory.GetFiles(ProfilesDirectory, "*.asset"))
            {
                string assetPath = file.Replace('\\', '/');
                UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
                if (asset == null) continue;
                SerializedObject serialized = new SerializedObject(asset);
                SerializedProperty overrideList = serialized.FindProperty("m_OverrideGlobalSceneList");
                if (overrideList == null || !overrideList.boolValue) continue;
                SerializedProperty scenes = serialized.FindProperty("m_Scenes");
                if (scenes == null || !scenes.isArray) continue;

                if (onlyRoom == null)
                    changed += EnsureProfileScene(scenes, LevelGraphFile.ScenePathFor(doc.MenuScene));

                foreach (RoomEntry room in doc.Rooms)
                {
                    if (onlyRoom != null && room.Name != onlyRoom) continue;
                    changed += EnsureProfileScene(scenes, LevelGraphFile.ScenePathFor(room.Name));
                }

                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            AssetDatabase.SaveAssets();
            return changed;
        }

        private static int EnsureProfileScene(SerializedProperty scenes, string scenePath)
        {
            if (string.IsNullOrEmpty(scenePath)) return 0;
            for (int i = 0; i < scenes.arraySize; i++)
            {
                SerializedProperty entry = scenes.GetArrayElementAtIndex(i);
                SerializedProperty path = entry.FindPropertyRelative("m_path");
                if (path == null || path.stringValue != scenePath) continue;
                SerializedProperty enabled = entry.FindPropertyRelative("m_enabled");
                if (enabled != null && !enabled.boolValue) { enabled.boolValue = true; return 1; }
                return 0;
            }

            scenes.InsertArrayElementAtIndex(scenes.arraySize);
            SerializedProperty added = scenes.GetArrayElementAtIndex(scenes.arraySize - 1);
            added.FindPropertyRelative("m_enabled").boolValue = true;
            added.FindPropertyRelative("m_path").stringValue = scenePath;
            added.FindPropertyRelative("m_guid").stringValue = AssetDatabase.AssetPathToGUID(scenePath);
            return 1;
        }
    }
}
