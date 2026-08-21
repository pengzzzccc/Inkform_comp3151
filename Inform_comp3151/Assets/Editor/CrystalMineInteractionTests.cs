#if UNITY_INCLUDE_TESTS
using System.Reflection;
using Inkform.Bus;
using Inkform.Fx;
using Inkform.Input;
using Inkform.Interactable;
using Inkform.Interactable.Parts;
using Inkform.Level.CrystalMine;
using NUnit.Framework;
using UnityEngine;

namespace Inkform.Tests
{
    public sealed class CrystalMineInteractionTests
    {
        [Test]
        public void GameplayGate_RequiresEverySourceToRelease()
        {
            object map = new object();
            object camera = new object();
            GameplayControlGate.Acquire(map);
            GameplayControlGate.Acquire(camera);
            GameplayControlGate.Release(map);
            Assert.IsTrue(GameplayControlGate.IsBlocked);
            GameplayControlGate.Release(camera);
            Assert.IsFalse(GameplayControlGate.IsBlocked);
        }

        [Test]
        public void CameraFocus_ReleasesOnlyItsOwningSource()
        {
            GameObject go = new GameObject("Camera focus test", typeof(Camera));
            CamHandler camera = go.AddComponent<CamHandler>();
            object first = new object();
            object second = new object();
            try
            {
                FxBus.RequestFocus(first, Vector2.one, 3f, 0.2f);
                FxBus.RequestFocus(second, Vector2.zero, 4f, 0.2f);
                FxBus.ReleaseFocus(first);
                Assert.AreSame(second, GetField<object>(camera, "focusSource"));
                FxBus.ReleaseFocus(second);
                Assert.IsNull(GetField<object>(camera, "focusSource"));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void CrystalFormation_DropsOnlyOnce()
        {
            GameObject shardPrefab = new GameObject("CrystalShardTestPrefab");
            GameObject formation = new GameObject("CrystalFormationTest");
            formation.SetActive(false);
            formation.AddComponent<BoxCollider2D>();
            formation.AddComponent<Inkform.Interactable.Interactable>();
            CrystalDropPart drop = formation.AddComponent<CrystalDropPart>();
            SetField(drop, "crystalShardPrefab", shardPrefab);
            SetField(drop, "dropCount", 2);
            formation.SetActive(true);
            try
            {
                HazardBus.RaiseExploded(formation, Vector2.zero, 1f);
                HazardBus.RaiseExploded(formation, Vector2.zero, 1f);
                GameObject[] all = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include);
                int clones = 0;
                foreach (GameObject item in all)
                    if (item.name == "CrystalShardTestPrefab(Clone)") clones++;
                Assert.AreEqual(2, clones);
                foreach (GameObject item in all)
                    if (item.name == "CrystalShardTestPrefab(Clone)") Object.DestroyImmediate(item);
            }
            finally
            {
                Object.DestroyImmediate(formation);
                Object.DestroyImmediate(shardPrefab);
            }
        }

        [Test]
        public void ElevatorMemento_RestoresIdleAndClearsEncounter()
        {
            GameObject go = new GameObject("Elevator encounter test");
            ElevatorEncounterController encounter = go.AddComponent<ElevatorEncounterController>();
            try
            {
                Inkform.Life.IMemento snapshot = encounter.Capture();
                Assert.IsTrue(encounter.BeginEncounter());
                Assert.AreEqual(ElevatorEncounterController.EncounterState.Rising, encounter.State);
                snapshot.Restore();
                Assert.AreEqual(ElevatorEncounterController.EncounterState.Idle, encounter.State);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        private static void SetField(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(target, value);

        private static T GetField<T>(object target, string name) =>
            (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    }
}
#endif
