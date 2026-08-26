using System;
using Inkform.Bus;
using Inkform.Save;
using UnityEngine;

namespace Inkform.Progression
{
    /// <summary>Persistent, save-slot-scoped equipment unlocks that are independent of level themes.</summary>
    public static class EquipmentProgressStore
    {
        public static event Action Changed;

        public static bool RopeGunUnlocked { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Changed = null;
            RopeGunUnlocked = false;
        }

        /// <summary>Unlocks the Rope Gun once and immediately mirrors the fact into the active save.</summary>
        public static bool TryUnlockRopeGun()
        {
            if (RopeGunUnlocked) return false;

            RopeGunUnlocked = true;
            Changed?.Invoke();
            EquipmentBus.RaiseRopeGunAcquired();
            SaveStore.RecordRopeGunUnlocked(true);
            return true;
        }

        /// <summary>Restores a save without treating load as a new pickup or writing the slot again.</summary>
        public static void Restore(bool ropeGunUnlocked)
        {
            if (RopeGunUnlocked == ropeGunUnlocked) return;
            RopeGunUnlocked = ropeGunUnlocked;
            Changed?.Invoke();
        }

        /// <summary>Clears a new run before its transactional entry-scene save is committed.</summary>
        public static void ClearWithoutSaving() => Restore(false);
    }
}
