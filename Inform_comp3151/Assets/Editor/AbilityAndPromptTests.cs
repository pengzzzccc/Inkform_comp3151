#if UNITY_INCLUDE_TESTS
using System;
using System.IO;
using System.Reflection;
using Inkform.Ability;
using Inkform.Bus;
using Inkform.Interactable;
using Inkform.Interactable.Parts;
using Inkform.Save;
using Inkform.Settings;
using Inkform.Tool;
using Inkform.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Inkform.Tests
{
    /// <summary>
    /// Ability-granting timecard: AbilityStore lifecycle, save round-trip and v3 migration, the
    /// device-scheme mapping behind the interaction prompt, and the reworked TimeCard prefab wiring.
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
        public void AbilityStore_UnlockOwnsAndRestoreReplaces()
        {
            Assert.IsFalse(AbilityStore.Owns(AbilityIds.Checkpoint));
            Assert.IsTrue(AbilityStore.Unlock(AbilityIds.Checkpoint));
            Assert.IsTrue(AbilityStore.Owns(AbilityIds.Checkpoint));
            Assert.IsFalse(AbilityStore.Unlock(AbilityIds.Checkpoint), "the same ability cannot unlock twice");
            Assert.IsFalse(AbilityStore.Unlock("  "), "blank ids are rejected");
            CollectionAssert.AreEqual(new[] { AbilityIds.Checkpoint }, AbilityStore.SnapshotIds());

            AbilityStore.Restore(new[] { "other" });
            Assert.IsFalse(AbilityStore.Owns(AbilityIds.Checkpoint), "Restore replaces the whole set");
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

                Assert.IsTrue(AbilityStore.Unlock(AbilityIds.Checkpoint));

                SaveData read = JsonUtility.FromJson<SaveData>(File.ReadAllText(Path.Combine(directory, "slot0.json")));
                CollectionAssert.AreEqual(new[] { AbilityIds.Checkpoint }, read.unlockedAbilityIds,
                    "an unlock must reach disk immediately");
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
        public void AbilityPickup_ConfirmGrantsOnlyWhilePlayerIsInRange()
        {
            AbilityStore.ClearWithoutSaving();
            GameObject root = new GameObject("Ability pickup test");
            root.SetActive(false);
            BoxCollider2D collider = root.AddComponent<BoxCollider2D>();
            collider.isTrigger = true;
            root.AddComponent<Inkform.Interactable.Interactable>();
            AbilityPickupPart pickup = root.AddComponent<AbilityPickupPart>();
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
                    Assert.IsFalse(AbilityStore.Owns(AbilityIds.Checkpoint),
                        "confirm with nobody in range must do nothing");

                    Assert.IsFalse(pickup.HandleContact(ContactPhase.Enter, playerCollider),
                        "presence tracking never claims the contact");
                    Assert.IsTrue(prompt.PlayerInRange);
                    PlayerBus.RaiseInteractPressed();
                    Assert.IsTrue(AbilityStore.Owns(AbilityIds.Checkpoint));
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
            Assert.IsTrue(AbilityStore.Unlock(AbilityIds.Checkpoint));

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
