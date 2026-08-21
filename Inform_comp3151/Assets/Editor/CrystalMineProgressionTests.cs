#if UNITY_INCLUDE_TESTS
using System;
using System.IO;
using System.Reflection;
using Inkform.Progression;
using Inkform.Save;
using NUnit.Framework;
using UnityEngine;

namespace Inkform.Tests
{
    public sealed class CrystalMineProgressionTests
    {
        [SetUp]
        public void SetUp() => RunProgressStore.Clear();

        [TearDown]
        public void TearDown() => RunProgressStore.Clear();

        [Test]
        public void DoorActivation_IsAtomicAndPermanent()
        {
            RunProgressStore.AddCrystals(4);
            Assert.IsFalse(RunProgressStore.TryActivateDoor("mine-door-a", 5));
            Assert.AreEqual(4, RunProgressStore.CrystalCount, "failed activation must spend nothing");

            RunProgressStore.AddCrystals(1);
            Assert.IsTrue(RunProgressStore.TryActivateDoor("mine-door-a", 5));
            Assert.AreEqual(0, RunProgressStore.CrystalCount);
            Assert.IsTrue(RunProgressStore.IsDoorActivated("mine-door-a"));

            RunProgressStore.AddCrystals(3);
            Assert.IsTrue(RunProgressStore.TryActivateDoor("mine-door-a", 5));
            Assert.AreEqual(3, RunProgressStore.CrystalCount, "an opened door must never charge twice");
        }

        [Test]
        public void Intake_ConsumesOneAndStopsAtTarget()
        {
            RunProgressStore.AddCrystals(5);
            Assert.IsTrue(RunProgressStore.TryDepositOne(2));
            Assert.IsTrue(RunProgressStore.TryDepositOne(2));
            Assert.IsFalse(RunProgressStore.TryDepositOne(2));
            Assert.AreEqual(2, RunProgressStore.ControlRoomCharge);
            Assert.AreEqual(3, RunProgressStore.CrystalCount);
        }

        [Test]
        public void Rewards_AreIdempotent()
        {
            Assert.IsTrue(RunProgressStore.GrantReward(ProgressionRewardType.RopeGun));
            Assert.IsFalse(RunProgressStore.GrantReward(ProgressionRewardType.RopeGun));
            Assert.IsTrue(RunProgressStore.GrantReward(ProgressionRewardType.ElevatorController));
            Assert.IsFalse(RunProgressStore.GrantReward(ProgressionRewardType.ElevatorController));
        }

        [Test]
        public void Snapshot_RestoresTutorialAndStableDoorIds()
        {
            RunProgressStore.AddCrystals(10);
            RunProgressStore.TryActivateDoor("door-b", 1);
            RunProgressStore.TryActivateDoor("door-a", 1);
            RunProgressStore.SetTutorialStep(4);
            RunProgressSnapshot snapshot = RunProgressStore.Snapshot();

            RunProgressStore.Clear();
            RunProgressStore.Restore(snapshot);
            Assert.AreEqual(4, RunProgressStore.TutorialStep);
            CollectionAssert.AreEqual(new[] { "door-a", "door-b" }, RunProgressStore.Snapshot().activatedDoorIds);
        }

        [Test]
        public void Restore_DeduplicatesDoorIds()
        {
            RunProgressStore.Restore(new RunProgressSnapshot
            {
                activatedDoorIds = new[] { "same-door", "same-door", "" }
            });
            CollectionAssert.AreEqual(new[] { "same-door" }, RunProgressStore.Snapshot().activatedDoorIds);
        }

        [Test]
        public void SaveV2_MigratesToV3WithEmptyProgression()
        {
            string path = Path.Combine(Application.temporaryCachePath, $"inkform-v2-{Guid.NewGuid():N}.json");
            try
            {
                File.WriteAllText(path, "{\"version\":2,\"sceneName\":\"B2\",\"inventoryItemIds\":[]}");
                MethodInfo read = typeof(SaveStore).GetMethod("TryRead", BindingFlags.Static | BindingFlags.NonPublic);
                object[] args = { path, null };
                Assert.IsTrue((bool)read.Invoke(null, args));
                SaveData data = (SaveData)args[1];
                Assert.AreEqual(3, data.version);
                Assert.AreEqual(0, data.crystalCount);
                Assert.IsFalse(data.ropeGunUnlocked);
                Assert.IsFalse(data.elevatorControllerAcquired);
                Assert.IsNotNull(data.activatedDoorIds);
                Assert.IsEmpty(data.activatedDoorIds);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
#endif
