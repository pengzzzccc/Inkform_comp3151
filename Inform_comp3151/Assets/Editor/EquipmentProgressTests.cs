#if UNITY_INCLUDE_TESTS
using System;
using System.IO;
using System.Reflection;
using Inkform.Bus;
using Inkform.Progression;
using Inkform.Save;
using NUnit.Framework;
using UnityEngine;

namespace Inkform.Tests
{
    public sealed class EquipmentProgressTests
    {
        private string savesDirectory;

        [SetUp]
        public void SetUp()
        {
            savesDirectory = Path.Combine(Application.temporaryCachePath, $"inkform-equipment-{Guid.NewGuid():N}");
            SaveStore.UseTestSaveDirectory(savesDirectory);
            EquipmentProgressStore.ClearWithoutSaving();
        }

        [TearDown]
        public void TearDown()
        {
            EquipmentProgressStore.ClearWithoutSaving();
            if (Directory.Exists(savesDirectory)) Directory.Delete(savesDirectory, true);
        }

        [Test]
        public void RopeGunUnlock_IsIdempotentAndRaisesOneFact()
        {
            int acquired = 0;
            void OnAcquired() => acquired++;
            EquipmentBus.RopeGunAcquired += OnAcquired;
            try
            {
                Assert.IsTrue(EquipmentProgressStore.TryUnlockRopeGun());
                Assert.IsFalse(EquipmentProgressStore.TryUnlockRopeGun());
                Assert.IsTrue(EquipmentProgressStore.RopeGunUnlocked);
                Assert.AreEqual(1, acquired);
            }
            finally
            {
                EquipmentBus.RopeGunAcquired -= OnAcquired;
            }
        }

        [Test]
        public void RopeGunUnlock_IsSavedAndRestoredBySlot()
        {
            SaveStore.BeginNewRun(0);
            SaveStore.RecordProgress("B2", Vector2.one);
            Assert.IsTrue(EquipmentProgressStore.TryUnlockRopeGun());

            EquipmentProgressStore.ClearWithoutSaving();
            Assert.IsFalse(EquipmentProgressStore.RopeGunUnlocked);

            SaveStore.ContinueRun(0);
            Assert.IsTrue(EquipmentProgressStore.RopeGunUnlocked);

            string json = File.ReadAllText(Path.Combine(savesDirectory, "slot0.json"));
            string removedField = "crys" + "talCount";
            StringAssert.Contains("\"ropeGunUnlocked\": true", json);
            StringAssert.DoesNotContain(removedField, json);
        }

        [TestCase(1, false)]
        [TestCase(2, false)]
        [TestCase(3, true)]
        public void ExistingSaveVersions_LoadWithExpectedRopeGunState(int version, bool unlocked)
        {
            string path = Path.Combine(savesDirectory, $"legacy-v{version}.json");
            Directory.CreateDirectory(savesDirectory);
            string removedField = "crys" + "talCount";
            File.WriteAllText(path,
                $"{{\"version\":{version},\"sceneName\":\"B2\",\"ropeGunUnlocked\":{unlocked.ToString().ToLowerInvariant()},\"{removedField}\":9}}");

            MethodInfo read = typeof(SaveStore).GetMethod("TryRead", BindingFlags.Static | BindingFlags.NonPublic);
            object[] args = { path, null };
            Assert.IsTrue((bool)read.Invoke(null, args));
            SaveData data = (SaveData)args[1];
            Assert.AreEqual(SaveData.CurrentVersion, data.version);
            Assert.AreEqual(unlocked, data.ropeGunUnlocked);
        }
    }
}
#endif
