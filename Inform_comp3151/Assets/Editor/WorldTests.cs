#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Inkform.Level;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Inkform.Tests
{
    public sealed class WorldTests
    {
        [Test]
        public void WorldDefinition_FindsRoomsBySceneNameAndToleratesUnknownScenes()
        {
            RoomDefinition cave = ScriptableObject.CreateInstance<RoomDefinition>();
            RoomDefinition lab = ScriptableObject.CreateInstance<RoomDefinition>();
            WorldDefinition world = ScriptableObject.CreateInstance<WorldDefinition>();
            try
            {
                SetScenePath(cave, "Assets/Scenes/Level1/Mine Cave 1.unity");
                SetScenePath(lab, "Assets/Scenes/Level1/Mine Cave 2.unity");
                SetRooms(world, cave, lab);

                Assert.AreSame(cave, world.FindBySceneName("Mine Cave 1"), "lookup by scene file name");
                Assert.AreSame(lab, world.FindBySceneName("Mine Cave 2"));
                Assert.IsNull(world.FindBySceneName("Mine Cave 3"), "an unregistered scene is nobody's room");
                Assert.IsNull(world.FindBySceneName(null), "null names never match");
                Assert.IsNull(world.FindBySceneName(""), "empty names never match");
            }
            finally
            {
                Object.DestroyImmediate(cave);
                Object.DestroyImmediate(lab);
                Object.DestroyImmediate(world);
            }
        }

        [Test]
        public void RoomDefinition_DisplayNameFallsBackToSceneName()
        {
            RoomDefinition room = ScriptableObject.CreateInstance<RoomDefinition>();
            try
            {
                SetScenePath(room, "Assets/Scenes/Level1/Mine Cave 1.unity");
                Assert.AreEqual("Mine Cave 1", room.SceneName, "scene name is the file name without extension");
                Assert.AreEqual("Mine Cave 1", room.DisplayName, "empty display falls back to the scene name");

                SetField(room, "displayName", "The First Cave");
                Assert.AreEqual("The First Cave", room.DisplayName);
            }
            finally
            {
                Object.DestroyImmediate(room);
            }
        }

        [Test]
        public void SceneReference_SceneNameComesFromPathAndSyncRecoversTheAsset()
        {
            // Point at a scene that ships with the project, then run the editor-side sync: the path
            // must resolve back into a live SceneAsset reference (the rename-safety a bare path
            // string never had)
            string menuPath = "Assets/Scenes/Menu/MainMenu.unity";
            Assert.IsTrue(File.Exists(menuPath), "the shipped menu scene must exist for this test");

            var reference = new SceneReference();
            Assert.IsFalse(reference.IsSet, "a fresh reference is unset");
            Assert.IsTrue(string.IsNullOrEmpty(reference.SceneName));

            SetField(reference, "scenePath", menuPath);
            reference.OnBeforeSerialize();   // the recover-lost-asset half of the sync

            Assert.AreEqual("MainMenu", reference.SceneName);
            Assert.IsNotNull(reference.SceneAsset, "the sync must re-link the SceneAsset from the path");
            Assert.AreEqual(menuPath, AssetDatabase.GetAssetPath(reference.SceneAsset));
        }

        // ---- Helpers: same reflection style as InkformRuntimeEdgeTests (private serialized fields) ----

        private static void SetScenePath(RoomDefinition room, string path)
        {
            var reference = new SceneReference();
            SetField(reference, "scenePath", path);
            SceneAsset asset = AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
            if (asset != null) SetField(reference, "sceneAsset", asset);
            SetField(room, "scene", reference);
        }

        private static void SetRooms(WorldDefinition world, params RoomDefinition[] rooms)
        {
            SetField(world, "rooms", new List<RoomDefinition>(rooms));
        }

        private static void SetField(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(target, value);
    }
}
#endif
