#if UNITY_INCLUDE_TESTS
using System;
using System.IO;
using System.Reflection;
using Inkform.Audio;
using Inkform.Bus;
using Inkform.Fx;
using Inkform.Item;
using Inkform.Player;
using Inkform.Save;
using NUnit.Framework;
using UnityEngine;

namespace Inkform.Tests
{
    public sealed class InkformRuntimeEdgeTests
    {
        [TearDown]
        public void TearDown()
        {
            InventoryStore.Clear();
            InvokeStatic(typeof(SaveStore), "ResetStatics");
            Time.timeScale = 1f;
            AudioListener.pause = false;
        }

        [Test]
        public void Inventory_HasFourSlotsAndKeepsSelectionValid()
        {
            InventoryStore.Clear();
            InventoryItemDefinition[] definitions = new InventoryItemDefinition[5];
            try
            {
                for (int i = 0; i < definitions.Length; i++)
                {
                    definitions[i] = ScriptableObject.CreateInstance<InventoryItemDefinition>();
                    SetField(definitions[i], "id", $"test-{i}");
                }

                for (int i = 0; i < InventoryStore.Capacity; i++)
                    Assert.IsTrue(InventoryStore.TryAdd(definitions[i]));
                Assert.IsFalse(InventoryStore.TryAdd(definitions[4]), "a fifth item must remain in the world");

                Assert.IsTrue(InventoryStore.Select(3));
                Assert.AreEqual(3, InventoryStore.SelectedIndex);
                Assert.IsTrue(InventoryStore.RemoveSelected());
                Assert.AreEqual(2, InventoryStore.SelectedIndex, "selection clamps after removing the last occupied slot");
            }
            finally
            {
                foreach (InventoryItemDefinition definition in definitions)
                    if (definition != null) UnityEngine.Object.DestroyImmediate(definition);
            }
        }

        [Test]
        public void SaveV1_MigratesToCurrentVersionWithEmptyInventory()
        {
            string path = Path.Combine(Application.temporaryCachePath, $"inkform-v1-{Guid.NewGuid():N}.json");
            try
            {
                File.WriteAllText(path, "{\"version\":1,\"sceneName\":\"B2\",\"spawnX\":2,\"spawnY\":3}");
                MethodInfo read = typeof(SaveStore).GetMethod("TryRead", BindingFlags.Static | BindingFlags.NonPublic);
                object[] args = { path, null };
                Assert.IsTrue((bool)read.Invoke(null, args));
                SaveData data = (SaveData)args[1];
                Assert.AreEqual(SaveData.CurrentVersion, data.version);
                Assert.IsNotNull(data.inventoryItemIds);
                Assert.IsEmpty(data.inventoryItemIds);
                Assert.AreEqual("B2", data.sceneName);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void PendingOverwrite_AbortRestoresPreviousSlot()
        {
            SaveData previous = new SaveData { sceneName = "B3", inventoryItemIds = Array.Empty<string>() };
            SetStaticField(typeof(SaveStore), "slots", new[] { previous, new SaveData(), new SaveData() });

            SaveStore.BeginNewRun(0);
            Assert.IsTrue(SaveStore.Get(0).IsEmpty);
            Assert.IsTrue(SaveStore.AbortNewRun());
            Assert.AreEqual("B3", SaveStore.Get(0).sceneName);
            Assert.AreEqual(-1, SaveStore.ActiveSlot);
        }

        [Test]
        public void SaveRecovery_UsesValidTempWhenPrimaryIsCorrupt()
        {
            string directory = Path.Combine(Application.temporaryCachePath, $"inkform-save-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            try
            {
                File.WriteAllText(Path.Combine(directory, "slot0.json"), "not-json");
                File.WriteAllText(Path.Combine(directory, "slot0.json.tmp"),
                    JsonUtility.ToJson(new SaveData { sceneName = "B2", inventoryItemIds = Array.Empty<string>() }));

                SaveStore.UseTestSaveDirectory(directory);
                Assert.AreEqual("B2", SaveStore.Get(0).sceneName);

                SaveData recovered = JsonUtility.FromJson<SaveData>(File.ReadAllText(Path.Combine(directory, "slot0.json")));
                Assert.AreEqual("B2", recovered.sceneName, "the valid temp file must repair the primary");
                Assert.IsTrue(File.Exists(Path.Combine(directory, "slot0.json.bak")));
            }
            finally
            {
                InvokeStatic(typeof(SaveStore), "ResetStatics");
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [Test]
        public void PauseAndHitStop_AreIndependentFreezeReasons()
        {
            GameObject go = new GameObject("GameTimeController test");
            go.SetActive(false);
            GameTimeController controller = go.AddComponent<GameTimeController>();
            go.SetActive(true);
            try
            {
                controller.RequestHitStop(0.2f);
                Assert.IsTrue(controller.IsHitStopped);
                Assert.AreEqual(0f, Time.timeScale);

                controller.SetUserPaused(true);
                controller.ClearHitStop();
                Assert.IsTrue(controller.IsUserPaused);
                Assert.AreEqual(0f, Time.timeScale, "ending hitstop must not release a user pause");

                controller.RequestHitStop(0.2f);
                controller.SetUserPaused(false);
                Assert.IsFalse(controller.IsUserPaused);
                Assert.IsTrue(controller.IsHitStopped);
                Assert.AreEqual(0f, Time.timeScale, "unpausing must preserve a remaining hitstop");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void RopeRangeOverrides_UseMinimumAndRestoreBySource()
        {
            GameObject go = new GameObject("RopeGun range test");
            go.SetActive(false);
            go.AddComponent<Rigidbody2D>();
            RopeGun gun = go.AddComponent<RopeGun>();
            go.SetActive(true);
            object wide = new object();
            object narrow = new object();
            try
            {
                RopeGunBus.RaiseRangeOverride(wide, 3f);
                RopeGunBus.RaiseRangeOverride(narrow, 2f);
                Assert.AreEqual(2f, GetField<float>(gun, "currentMaxRange"));

                RopeGunBus.RaiseRangeRestored(narrow);
                Assert.AreEqual(3f, GetField<float>(gun, "currentMaxRange"));
                RopeGunBus.RaiseRangeRestored(wide);
                Assert.AreEqual(4.2f, GetField<float>(gun, "currentMaxRange"), 0.001f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void AudioManager_DestroyedActiveSourceRepairsCountAndPool()
        {
            GameObject go = new GameObject("AudioManager recycle test");
            go.SetActive(false);
            AudioManager manager = go.AddComponent<AudioManager>();
            SetField(manager, "poolSize", 2);
            go.SetActive(true);
            SoundCue cue = ScriptableObject.CreateInstance<SoundCue>();
            AudioClip clip = AudioClip.Create("test", 32, 1, 8000, false);
            cue.clips = new[] { clip };
            cue.cooldown = 0f;
            cue.maxConcurrent = 1;
            try
            {
                AudioSource first = manager.Play(cue);
                Assert.IsNotNull(first);
                Assert.AreEqual(1, cue.activeCount);
                UnityEngine.Object.DestroyImmediate(first.gameObject);

                Invoke(manager, "Update");
                Assert.AreEqual(0, cue.activeCount);
                Assert.IsNotNull(manager.Play(cue), "a replacement source must restore the fixed pool capacity");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cue);
                UnityEngine.Object.DestroyImmediate(clip);
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        private static void Invoke(object target, string method) =>
            target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(target, null);

        private static void InvokeStatic(Type type, string method) =>
            type.GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, null);

        private static void SetField(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(target, value);

        private static T GetField<T>(object target, string name) =>
            (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);

        private static void SetStaticField(Type type, string name, object value) =>
            type.GetField(name, BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, value);
    }
}
#endif
