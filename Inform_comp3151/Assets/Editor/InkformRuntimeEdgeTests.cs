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
using UnityEditor;
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
        public void SaveV1_MigratesToV2WithEmptyInventory()
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
                Assert.AreEqual(4f, GetField<float>(gun, "currentMaxRange"), 0.001f);
                Assert.AreEqual(2.4f, GetField<Vector2>(gun, "aimOffset").magnitude, 0.001f);

                RopeGunBus.RaiseRangeOverride(wide, 3f);
                Assert.AreEqual(3f, GetField<float>(gun, "currentMaxRange"));
                Assert.AreEqual(2.4f, GetField<Vector2>(gun, "aimOffset").magnitude, 0.001f,
                    "a wider boundary must not push a free cursor outward");

                RopeGunBus.RaiseRangeOverride(narrow, 2f);
                Assert.AreEqual(2f, GetField<float>(gun, "currentMaxRange"));
                Assert.AreEqual(2f, GetField<Vector2>(gun, "aimOffset").magnitude, 0.001f,
                    "a shorter range must pull an out-of-bounds cursor inward");

                RopeGunBus.RaiseRangeRestored(narrow);
                Assert.AreEqual(3f, GetField<float>(gun, "currentMaxRange"));
                Assert.AreEqual(2f, GetField<Vector2>(gun, "aimOffset").magnitude, 0.001f,
                    "restoring range must preserve the free cursor distance");
                RopeGunBus.RaiseRangeRestored(wide);
                Assert.AreEqual(4f, GetField<float>(gun, "currentMaxRange"), 0.001f);
                Assert.AreEqual(2f, GetField<Vector2>(gun, "aimOffset").magnitude, 0.001f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void RopeAim_MovesFreelyAndClampsToSafeBounds()
        {
            GameObject go = new GameObject("RopeGun free aim test");
            go.SetActive(false);
            go.AddComponent<Rigidbody2D>();
            RopeGun gun = go.AddComponent<RopeGun>();
            go.SetActive(true);
            try
            {
                SetField(gun, "mouseAimSensitivity", 1f);
                SetField(gun, "aimOffset", new Vector2(1.5f, 0f));
                gun.Aim(new Vector2(1f, 0f), true);
                float movedDistance = GetField<Vector2>(gun, "aimOffset").magnitude;
                Assert.Greater(movedDistance, 1.5f, "aim input must be able to change cursor distance");
                Assert.Less(movedDistance, 4f, "ordinary input must not force the cursor to max range");

                SetField(gun, "aimOffset", new Vector2(0.1f, 0f));
                Invoke(gun, "ClampAimOffsetToCurrentRange");
                Assert.AreEqual(0.7f, GetField<Vector2>(gun, "aimOffset").magnitude, 0.001f);

                SetField(gun, "aimOffset", new Vector2(10f, 0f));
                Invoke(gun, "ClampAimOffsetToCurrentRange");
                Assert.AreEqual(4f, GetField<Vector2>(gun, "aimOffset").magnitude, 0.001f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void RopeFire_KeepsShotRangeAnchoredAtFirePositionAndUsesContinuousCollision()
        {
            GameObject go = new GameObject("RopeGun shot origin test");
            Vector2 testOrigin = new Vector2(10000f, 10000f);
            go.transform.position = testOrigin;
            go.SetActive(false);
            Rigidbody2D playerBody = go.AddComponent<Rigidbody2D>();
            RopeGun gun = go.AddComponent<RopeGun>();
            go.SetActive(true);
            try
            {
                Vector2 freeCursor = new Vector2(1.2f, 0.5f);
                SetField(gun, "aimOffset", freeCursor);
                gun.TryFire();
                Rigidbody2D hookBody = GetField<Rigidbody2D>(gun, "hookBody");
                Assert.AreEqual(CollisionDetectionMode2D.Continuous, hookBody.collisionDetectionMode);
                Assert.AreEqual(testOrigin, GetField<Vector2>(gun, "shotPlayerPosition"));
                Assert.AreEqual(4f, GetField<float>(gun, "shotMaxRange"), 0.001f);
                Assert.AreEqual(freeCursor, GetField<Vector2>(gun, "shotAimOffset"),
                    "the shot must snapshot the free cursor target at fire time");

                hookBody.position = testOrigin + new Vector2(3f, 0f);
                playerBody.position = testOrigin + new Vector2(-10f, 0f);
                Invoke(gun, "FixedUpdate");
                Assert.AreEqual("Flying", GetField<object>(gun, "phase").ToString(),
                    "moving the player after firing must not invalidate the shot range");
            }
            finally
            {
                GameObject hook = GetField<GameObject>(gun, "hookGo");
                if (hook != null) UnityEngine.Object.DestroyImmediate(hook);
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void RopePreview_IgnoresTriggersAndTerrainPastTheRangeBoundary()
        {
            GameObject player = new GameObject("RopeGun preview test");
            GameObject wall = new GameObject("preview wall");
            Vector2 testOrigin = new Vector2(10000f, 10000f);
            player.transform.position = testOrigin;
            player.SetActive(false);
            player.AddComponent<Rigidbody2D>();
            RopeGun gun = player.AddComponent<RopeGun>();
            player.SetActive(true);
            wall.layer = 6;
            BoxCollider2D box = wall.AddComponent<BoxCollider2D>();
            box.size = new Vector2(0.02f, 4f);
            try
            {
                wall.transform.position = testOrigin + new Vector2(2f, 0f);
                box.isTrigger = true;
                Physics2D.SyncTransforms();
                Invoke(gun, "UpdatePreview");
                Assert.AreEqual(GetField<Color>(gun, "missColor"),
                    GetField<SpriteRenderer>(gun, "reticleSprite").color,
                    "a trigger must not make the reticle green");

                box.isTrigger = false;
                Physics2D.SyncTransforms();
                Invoke(gun, "UpdatePreview");
                Assert.AreEqual(GetField<Color>(gun, "hitColor"),
                    GetField<SpriteRenderer>(gun, "reticleSprite").color,
                    "thin solid terrain inside the range must make the reticle green");

                wall.transform.position = testOrigin + new Vector2(4.1f, 0f);
                Physics2D.SyncTransforms();
                Invoke(gun, "UpdatePreview");
                Assert.AreEqual(GetField<Color>(gun, "hitColor"),
                    GetField<SpriteRenderer>(gun, "reticleSprite").color,
                    "the hook radius must allow a legal contact at the range boundary");

                wall.transform.position = testOrigin + new Vector2(4.35f, 0f);
                Physics2D.SyncTransforms();
                Invoke(gun, "UpdatePreview");
                Assert.AreEqual(GetField<Color>(gun, "missColor"),
                    GetField<SpriteRenderer>(gun, "reticleSprite").color,
                    "terrain beyond the range boundary must not make the reticle green");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(wall);
                UnityEngine.Object.DestroyImmediate(player);
            }
        }

        [Test]
        public void BreakableWallPrefab_IsOnTheHookableBreakableLayer()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/Item/BreakableWall.prefab");
            Assert.IsNotNull(prefab);
            Assert.AreEqual(11, prefab.layer,
                "direct BreakableWall instances must not inherit the unhookable Default layer");
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
