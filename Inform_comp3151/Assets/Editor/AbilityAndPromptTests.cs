#if UNITY_INCLUDE_TESTS
using System;
using System.IO;
using System.Reflection;
using Inkform.Ability;
using Inkform.Bus;
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

        [Test]
        public void RopeGun_FireIsDeniedWithoutTheAbility()
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
                    "without the ability the shot must be refused before anything spawns");

                Assert.IsTrue(AbilityStore.Unlock(AbilityIds.RopeGun));
                InvokeInstance(ropeGun, "TryFire");
                Assert.AreEqual(RopeGun.RopePhase.Flying, PhaseOf(ropeGun),
                    "with the ability earned the same press fires normally");
            }
            finally
            {
                InvokeInstance(ropeGun, "Finish");      // despawns the hook the earned shot created
                UnityEngine.Object.DestroyImmediate(player);
            }
        }

        [Test]
        public void TutorialPanel_PagesSwitchByAbilityAndRespectThePickupSwitch()
        {
            Texture2D texture = new Texture2D(4, 4);
            Sprite first = Sprite.Create(texture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f), 100f);
            Sprite second = Sprite.Create(texture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f), 100f);
            Sprite ropeGunOnly = Sprite.Create(texture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f), 100f);

            GameObject root = new GameObject("Tutorial test");
            root.SetActive(false);
            TutorialPanel panel = root.AddComponent<TutorialPanel>();   // CanvasGroup comes via RequireComponent
            Button prev = AddTutorialButton(root, "Btn_Prev");
            Button next = AddTutorialButton(root, "Btn_Next");
            AddTutorialButton(root, "Btn_Close");
            GameObject page = new GameObject("Page", typeof(RectTransform), typeof(Image));
            page.transform.SetParent(root.transform, false);
            GameObject labelGo = new GameObject("Lbl_Page", typeof(RectTransform), typeof(Text));
            labelGo.transform.SetParent(root.transform, false);
            Text label = labelGo.GetComponent<Text>();

            SetPages(panel, "checkpointPages", first, second);
            SetPages(panel, "ropeGunPages", ropeGunOnly);
            InvokeInstance(panel, "Awake");     // edit mode never runs Unity's magic methods
            try
            {
                Assert.IsTrue(panel.ShowOnPickup, "the toggle defaults to on");
                Assert.IsTrue(panel.HasPages(AbilityIds.Checkpoint));
                Assert.IsTrue(panel.HasPages(AbilityIds.RopeGun));
                Assert.IsFalse(panel.HasPages("other"), "an unknown ability has no page set");

                panel.OpenWith(AbilityIds.Checkpoint);
                Image pageImage = page.GetComponent<Image>();
                Assert.AreEqual(first, pageImage.sprite);
                Assert.AreEqual("1 / 2", label.text);
                Assert.IsFalse(prev.interactable, "no page before the first");
                Assert.IsTrue(next.interactable);

                next.onClick.Invoke();
                Assert.AreEqual(second, pageImage.sprite);
                Assert.AreEqual("2 / 2", label.text);
                Assert.IsFalse(next.interactable, "no page after the last");
                Assert.IsTrue(prev.interactable);

                prev.onClick.Invoke();
                Assert.AreEqual(first, pageImage.sprite, "Prev steps back to the first page");

                panel.OpenWith(AbilityIds.RopeGun);
                Assert.AreEqual(ropeGunOnly, pageImage.sprite, "each ability opens its own page set");
                Assert.AreEqual("1 / 1", label.text);
                Assert.IsFalse(prev.interactable);
                Assert.IsFalse(next.interactable, "a single page has nothing to step to");

                SetPickupSwitch(panel, false);
                Assert.IsFalse(panel.ShowOnPickup, "the Inspector switch is the off gate OpenTutorial reads");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(first);
                UnityEngine.Object.DestroyImmediate(second);
                UnityEngine.Object.DestroyImmediate(ropeGunOnly);
                UnityEngine.Object.DestroyImmediate(texture);
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

        private static Button AddTutorialButton(GameObject parent, string name)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent.transform, false);
            return go.GetComponent<Button>();
        }

        private static void SetPages(TutorialPanel panel, string field, params Sprite[] pages) =>
            panel.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(panel, pages);

        private static void SetPickupSwitch(TutorialPanel panel, bool value) =>
            panel.GetType().GetField("showOnPickup", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(panel, value);

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
