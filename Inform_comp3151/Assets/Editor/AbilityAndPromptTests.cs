#if UNITY_INCLUDE_TESTS
using System;
using System.IO;
using System.Reflection;
using Inkform.Ability;
using Inkform.Bus;
using Inkform.Fx;
using Inkform.Input;
using Inkform.Interactable;
using Inkform.Interactable.Parts;
using Inkform.Level;
using Inkform.Player;
using Inkform.Save;
using Inkform.Settings;
using Inkform.Tool;
using Inkform.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;

namespace Inkform.Tests
{
    /// <summary>
    /// Ability-granting timecard: AbilityStore lifecycle, save round-trip and v3 migration, the
    /// device-scheme mapping behind the interaction prompt, and the TimeCard / RopeGunCard prefab
    /// wiring.
    /// </summary>
    public sealed class AbilityAndPromptTests
    {
        [TearDown]
        public void TearDown()
        {
            AbilityStore.ClearWithoutSaving();
            InvokeStatic(typeof(SaveStore), "ResetStatics");
        }

        [Test]
        public void AbilityStore_DefaultAbilitiesAreBuiltInAndRestoreUnionsThem()
        {
            Assert.IsTrue(AbilityStore.Owns(AbilityIds.Checkpoint), "both card abilities ship as built-in defaults");
            Assert.IsTrue(AbilityStore.Owns(AbilityIds.RopeGun));
            Assert.IsFalse(AbilityStore.Unlock(AbilityIds.Checkpoint), "a default ability is already owned");
            CollectionAssert.AreEquivalent(new[] { AbilityIds.Checkpoint, AbilityIds.RopeGun }, AbilityStore.SnapshotIds());

            AbilityStore.Restore(new[] { "other" });
            Assert.IsTrue(AbilityStore.Owns(AbilityIds.Checkpoint),
                "Restore unions the defaults in — an old save from before the defaults ships is topped up");
            Assert.IsTrue(AbilityStore.Owns(AbilityIds.RopeGun));
            Assert.IsTrue(AbilityStore.Owns("other"));
        }

        [Test]
        public void AbilityUnlock_PersistsIntoTheSlotFile()
        {
            string directory = Path.Combine(Application.temporaryCachePath, $"inkform-ability-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            try
            {
                File.WriteAllText(Path.Combine(directory, "slot0.json"),
                    JsonUtility.ToJson(new SaveData { sceneName = "B2" }));
                SaveStore.UseTestSaveDirectory(directory);
                SaveStore.ContinueRun(0);

                Assert.IsTrue(AbilityStore.Unlock("test-extra"));

                SaveData read = JsonUtility.FromJson<SaveData>(File.ReadAllText(Path.Combine(directory, "slot0.json")));
                CollectionAssert.Contains(read.unlockedAbilityIds, "test-extra",
                    "an unlock must reach disk immediately");
                CollectionAssert.Contains(read.unlockedAbilityIds, AbilityIds.RopeGun,
                    "the built-in defaults ride along on the same write");
            }
            finally
            {
                InvokeStatic(typeof(SaveStore), "ResetStatics");
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [Test]
        public void SaveV3_TimeCardInBagMigratesIntoCheckpointAbility()
        {
            string path = Path.Combine(Application.temporaryCachePath, $"inkform-v3card-{Guid.NewGuid():N}.json");
            try
            {
                File.WriteAllText(path,
                    "{\"version\":3,\"sceneName\":\"B2\",\"inventoryItemIds\":[\"allinone-bomb\",\"time-card\",\"allinone-bomb\"],\"inventoryCapacity\":3}");
                SaveData data = ReadSave(path);
                Assert.AreEqual(SaveData.CurrentVersion, data.version);
                CollectionAssert.AreEqual(new[] { "allinone-bomb", "allinone-bomb" }, data.inventoryItemIds,
                    "the retired time-card must leave the bag");
                CollectionAssert.AreEqual(new[] { AbilityIds.Checkpoint }, data.unlockedAbilityIds,
                    "a card held in the bag becomes the checkpoint ability it used to gate");
                Assert.AreEqual(3, data.inventoryCapacity);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void SaveV3_WithoutTimeCardMigratesToNoAbilities()
        {
            string path = Path.Combine(Application.temporaryCachePath, $"inkform-v3bare-{Guid.NewGuid():N}.json");
            try
            {
                File.WriteAllText(path,
                    "{\"version\":3,\"sceneName\":\"B2\",\"inventoryItemIds\":[\"allinone-bomb\"],\"inventoryCapacity\":1}");
                SaveData data = ReadSave(path);
                Assert.AreEqual(SaveData.CurrentVersion, data.version);
                CollectionAssert.AreEqual(new[] { "allinone-bomb" }, data.inventoryItemIds);
                Assert.IsNotNull(data.unlockedAbilityIds);
                Assert.IsEmpty(data.unlockedAbilityIds);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void PromptIcons_ResolveDeviceSchemes()
        {
            Assert.AreEqual(PromptScheme.KeyboardMouse,
                InteractPromptIcons.Resolve(SettingsStore.InputDevice.KeyboardMouse, "DualSenseGamepadHID"),
                "the Controls-tab family wins over whatever pad happens to be plugged in");

            Assert.AreEqual(PromptScheme.PlayStation,
                InteractPromptIcons.Resolve(SettingsStore.InputDevice.Gamepad, "DualSenseGamepadHID"));
            Assert.AreEqual(PromptScheme.PlayStation,
                InteractPromptIcons.Resolve(SettingsStore.InputDevice.Gamepad, "DualShock4GamepadHID"));

            Assert.AreEqual(PromptScheme.Xbox,
                InteractPromptIcons.Resolve(SettingsStore.InputDevice.Gamepad, "XInputControllerWindows"));
            Assert.AreEqual(PromptScheme.Xbox,
                InteractPromptIcons.Resolve(SettingsStore.InputDevice.Gamepad, "SwitchProController"),
                "unrecognized pads fall back to the letter face");
            Assert.AreEqual(PromptScheme.Xbox,
                InteractPromptIcons.Resolve(SettingsStore.InputDevice.Gamepad, null));
        }

        [Test]
        public void TimeCard_IsAnAbilityPickupWithPromptAndNoLongerCarriable()
        {
            GameObject instance = UnityEngine.Object.Instantiate(
                AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Item/TimeCard.prefab"));
            try
            {
                Inkform.Interactable.Interactable node = instance.GetComponent<Inkform.Interactable.Interactable>();
                Assert.IsNotNull(node);
                Assert.IsFalse(node.TryGetPart(out ICarriable _), "the card must not ride the bomb bag anymore");
                Assert.IsTrue(node.TryGetPart(out AbilityPickupPart pickup));
                Assert.AreEqual(AbilityIds.Checkpoint, pickup.AbilityId);
                Assert.IsTrue(node.TryGetPart(out InteractionPromptPart _));

                Collider2D collider = instance.GetComponent<Collider2D>();
                Assert.IsNotNull(collider);
                Assert.IsTrue(collider.isTrigger, "the pickup range is a walk-in trigger");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void RopeGunCard_IsAnAbilityPickupWithPrompt()
        {
            GameObject instance = UnityEngine.Object.Instantiate(
                AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Item/RopeGunCard.prefab"));
            try
            {
                Inkform.Interactable.Interactable node = instance.GetComponent<Inkform.Interactable.Interactable>();
                Assert.IsNotNull(node);
                Assert.IsTrue(node.TryGetPart(out AbilityPickupPart pickup));
                Assert.AreEqual(AbilityIds.RopeGun, pickup.AbilityId);
                Assert.IsTrue(node.TryGetPart(out InteractionPromptPart _));

                Collider2D collider = instance.GetComponent<Collider2D>();
                Assert.IsNotNull(collider);
                Assert.IsTrue(collider.isTrigger, "the pickup range is a walk-in trigger");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [TestCase("Assets/Prefabs/Item/TimeCard.prefab", 2f)]
        [TestCase("Assets/Prefabs/Item/RopeGunCard.prefab", 4f)]
        public void PromptOutline_IsPrebuiltAlignedAndUsesWorldConsistentWidth(
            string prefabPath, float expectedTexelWidth)
        {
            GameObject instance = UnityEngine.Object.Instantiate(
                AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath));
            instance.transform.position = new Vector3(13f, -7f, 0f);
            try
            {
                Inkform.Interactable.Interactable node = instance.GetComponent<Inkform.Interactable.Interactable>();
                Assert.IsTrue(node.TryGetPart(out InteractionPromptPart prompt));

                // Edit-mode tests do not invoke Start automatically; this mirrors the runtime prebuild.
                InvokeInstance(prompt, "Start");

                SpriteRenderer source = instance.GetComponent<SpriteRenderer>();
                Transform outlineTransform = source.transform.Find("Outline");
                Assert.IsNotNull(outlineTransform, "the outline must exist before the first contact");

                SpriteRenderer outline = outlineTransform.GetComponent<SpriteRenderer>();
                Assert.IsNotNull(outline);
                Assert.IsFalse(outline.enabled, "the prebuilt outline waits disabled for first contact");
                Assert.AreSame(source.sprite, outline.sprite);
                Assert.AreEqual(source.gameObject.layer, outline.gameObject.layer);
                Assert.AreEqual(source.sortingLayerID, outline.sortingLayerID);
                Assert.AreEqual(source.sortingOrder + 1, outline.sortingOrder);

                Vector2 spriteCenter = source.sprite.bounds.center;
                if (source.flipX) spriteCenter.x = -spriteCenter.x;
                if (source.flipY) spriteCenter.y = -spriteCenter.y;
                Vector2 renderedOutlineCenter = (Vector2)outlineTransform.localPosition +
                    Vector2.Scale((Vector2)outlineTransform.localScale, spriteCenter);
                Assert.That(Vector2.Distance(spriteCenter, renderedOutlineCenter), Is.LessThan(0.0001f),
                    "outline scaling must preserve the source sprite centre");

                Material runtimeMaterial = outline.sharedMaterial;
                Assert.IsNotNull(runtimeMaterial);
                Assert.That(runtimeMaterial.GetFloat("_OutlineWidth"), Is.EqualTo(expectedTexelWidth).Within(0.0001f));
                Vector4 texelSize = runtimeMaterial.GetVector("_SpriteUvStep");
                Assert.That(texelSize.x, Is.EqualTo(1f / source.sprite.texture.width).Within(0.000001f));
                Assert.That(texelSize.y, Is.EqualTo(1f / source.sprite.texture.height).Within(0.000001f));
                Assert.That(runtimeMaterial.GetFloat("_Fade"), Is.Zero.Within(0.0001f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void RopeGun_FireIsDeniedWithoutTheAbilityOrAGreenReticle()
        {
            AbilityStore.ClearWithoutSaving();
            GameObject player = new GameObject("RopeGun gate test");
            player.SetActive(false);
            player.AddComponent<Rigidbody2D>();
            RopeGun ropeGun = player.AddComponent<RopeGun>();
            // Manual Awake mirrors the Checkpoint tests: edit mode never runs Unity's magic methods
            InvokeInstance(ropeGun, "Awake");
            try
            {
                InvokeInstance(ropeGun, "TryFire");
                Assert.AreEqual(RopeGun.RopePhase.Idle, PhaseOf(ropeGun),
                    "the ability ships built-in, so with no intercept the press is refused by the " +
                    "red-reticle gate before anything spawns");

                Assert.IsTrue(AbilityStore.Owns(AbilityIds.RopeGun));
                InvokeInstance(ropeGun, "TryFire");
                Assert.AreEqual(RopeGun.RopePhase.Idle, PhaseOf(ropeGun),
                    "instant fire anchors only on a green reticle — an empty scene has no " +
                    "intercept, so the press is refused exactly like the retired ability gate");
            }
            finally
            {
                InvokeInstance(ropeGun, "Finish");      // no-op when nothing fired; safety for future edits
                UnityEngine.Object.DestroyImmediate(player);
            }
        }

        [Test]
        public void AbilityPickup_ConfirmGrantsOnlyWhilePlayerIsInRange()
        {
            AbilityStore.ClearWithoutSaving();
            GameObject root = new GameObject("Ability pickup test");
            root.SetActive(false);
            BoxCollider2D collider = root.AddComponent<BoxCollider2D>();
            collider.isTrigger = true;
            root.AddComponent<Inkform.Interactable.Interactable>();
            AbilityPickupPart pickup = root.AddComponent<AbilityPickupPart>();
            // The card abilities ship built-in now, so the grant-from-nothing path is exercised
            // on a non-default id (attached before SetActive — Attach runs on activation).
            SetField(pickup, "abilityId", "test-ability");
            InteractionPromptPart prompt = root.AddComponent<InteractionPromptPart>();
            root.SetActive(true);
            try
            {
                GameObject player = new GameObject("Player");
                player.tag = Tags.Player;
                BoxCollider2D playerCollider = player.AddComponent<BoxCollider2D>();
                try
                {
                    PlayerBus.RaiseInteractPressed();
                    Assert.IsFalse(AbilityStore.Owns("test-ability"),
                        "confirm with nobody in range must do nothing");

                    Assert.IsFalse(pickup.HandleContact(ContactPhase.Enter, playerCollider),
                        "presence tracking never claims the contact");
                    Assert.IsTrue(prompt.PlayerInRange);
                    PlayerBus.RaiseInteractPressed();
                    Assert.IsTrue(AbilityStore.Owns("test-ability"));
                    Assert.IsTrue(root == null, "a collected pickup consumes its world object");
                }
                finally
                {
                    if (player != null) UnityEngine.Object.DestroyImmediate(player);
                }
            }
            finally
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void AbilityPickup_SelfRemovesWhenAbilityAlreadyEarned()
        {
            AbilityStore.ClearWithoutSaving();
            Assert.IsTrue(AbilityStore.Owns(AbilityIds.Checkpoint),
                "the default abilities count as already earned — the world cards self-remove on load");

            GameObject root = new GameObject("Earned pickup test");
            root.SetActive(false);
            CircleCollider2D collider = root.AddComponent<CircleCollider2D>();
            collider.isTrigger = true;
            Inkform.Interactable.Interactable node = root.AddComponent<Inkform.Interactable.Interactable>();
            root.AddComponent<AbilityPickupPart>();

            // Forcing part attach mirrors what Interactable.Awake does on scene load
            Assert.IsTrue(node.TryGetPart(out AbilityPickupPart _));
            Assert.IsTrue(root == null, "an already-earned ability consumes its world copy on load");
        }

        [Test]
        public void Checkpoints_NewestStampResetsPreviousMachines()
        {
            AbilityStore.ClearWithoutSaving();
            Assert.IsTrue(AbilityStore.Unlock(AbilityIds.Checkpoint));

            GameObject player = new GameObject("Player");
            player.tag = Tags.Player;
            BoxCollider2D playerCollider = player.AddComponent<BoxCollider2D>();
            Checkpoint first = NewCheckpoint(new Vector3(0f, 0f, 0f));
            Checkpoint second = NewCheckpoint(new Vector3(10f, 0f, 0f));
            try
            {
                Touch(first, playerCollider);
                Assert.IsTrue(IsStamped(first));
                Assert.IsFalse(IsStamped(second));

                Touch(second, playerCollider);
                Assert.IsTrue(IsStamped(second));
                Assert.IsFalse(IsStamped(first), "stamping a machine must reset the previous one to un-stamped");

                Touch(first, playerCollider);
                Assert.IsTrue(IsStamped(first), "a reset machine opens its gate and can be stamped again");
                Assert.IsFalse(IsStamped(second));
            }
            finally
            {
                if (first != null) { InvokeInstance(first, "OnDisable"); UnityEngine.Object.DestroyImmediate(first.gameObject); }
                if (second != null) { InvokeInstance(second, "OnDisable"); UnityEngine.Object.DestroyImmediate(second.gameObject); }
                UnityEngine.Object.DestroyImmediate(player);
            }
        }

        private static Checkpoint NewCheckpoint(Vector3 position)
        {
            GameObject go = new GameObject("Checkpoint test");
            go.SetActive(false);
            go.transform.position = position;
            go.AddComponent<SpriteRenderer>();
            go.AddComponent<BoxCollider2D>();
            Checkpoint checkpoint = go.AddComponent<Checkpoint>();
            InvokeInstance(checkpoint, "Awake");
            InvokeInstance(checkpoint, "OnEnable");
            return checkpoint;
        }

        private static void Touch(Checkpoint checkpoint, Collider2D other) =>
            checkpoint.GetType().GetMethod("OnTriggerEnter2D", BindingFlags.Instance | BindingFlags.NonPublic)?
                .Invoke(checkpoint, new object[] { other });

        private static bool IsStamped(Checkpoint checkpoint) =>
            (bool)checkpoint.GetType().GetField("active", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(checkpoint);

        private static RopeGun.RopePhase PhaseOf(RopeGun ropeGun) =>
            (RopeGun.RopePhase)ropeGun.GetType().GetField("phase", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(ropeGun);

        private static T ReadField<T>(object target, string name) =>
            (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);

        private static void SetField(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

        private static void InvokeInstance(object target, string method) =>
            target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(target, null);

        private static SaveData ReadSave(string path)
        {
            MethodInfo read = typeof(SaveStore).GetMethod("TryRead", BindingFlags.Static | BindingFlags.NonPublic);
            object[] args = { path, null };
            Assert.IsTrue((bool)read.Invoke(null, args));
            return (SaveData)args[1];
        }

        private static void InvokeStatic(Type type, string method) =>
            type.GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, null);
    }
}
#endif
