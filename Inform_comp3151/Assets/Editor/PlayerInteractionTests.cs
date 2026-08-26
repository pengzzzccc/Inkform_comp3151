#if UNITY_INCLUDE_TESTS
using System.Reflection;
using Inkform.Bus;
using Inkform.Interactable;
using Inkform.Interactable.Parts;
using Inkform.Player;
using Inkform.Progression;
using NUnit.Framework;
using UnityEngine;

namespace Inkform.Tests
{
    public sealed class PlayerInteractionTests
    {
        [SetUp]
        public void SetUp() => EquipmentProgressStore.ClearWithoutSaving();

        [TearDown]
        public void TearDown() => EquipmentProgressStore.ClearWithoutSaving();

        [Test]
        public void Interactor_SelectsAndActivatesClosestVisiblePart()
        {
            GameObject playerObject = new GameObject("Interaction Test Player");
            GameObject nearObject = CreateTarget("Near Rope Gun", new Vector2(0.5f, 0f));
            GameObject farObject = CreateTarget("Far Rope Gun", new Vector2(1f, 0f));
            try
            {
                PlayerInteractor interactor = playerObject.AddComponent<PlayerInteractor>();
                RopeGunPickupPart near = nearObject.GetComponent<RopeGunPickupPart>();

                typeof(PlayerInteractor).GetMethod("SelectClosest", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.Invoke(interactor, null);

                PlayerInteractablePart selected = (PlayerInteractablePart)typeof(PlayerInteractor)
                    .GetField("current", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(interactor);
                Assert.AreSame(near, selected);
                Assert.AreEqual("Pick Up Rope Gun", InteractionBus.Current.Action);
                interactor.InteractStarted();
                Assert.IsTrue(EquipmentProgressStore.RopeGunUnlocked);
            }
            finally
            {
                Object.DestroyImmediate(farObject);
                Object.DestroyImmediate(nearObject);
                Object.DestroyImmediate(playerObject);
                InteractionBus.Clear();
            }
        }

        private static GameObject CreateTarget(string name, Vector2 position)
        {
            GameObject target = new GameObject(name);
            target.SetActive(false);
            target.transform.position = position;
            target.AddComponent<BoxCollider2D>();
            target.AddComponent<Inkform.Interactable.Interactable>();
            target.AddComponent<RopeGunPickupPart>();
            target.SetActive(true);
            return target;
        }
    }
}
#endif
