using System;
using System.Collections.Generic;
using Inkform.Save;
using UnityEngine;

namespace Inkform.Ability
{
    /// <summary>
    /// Save-level ability unlocks: which permanent capabilities (e.g. using checkpoints) this run's
    /// save slot owns. Same static-store pattern as InventoryStore — the save file persists the set,
    /// so a static API with no scene wiring fits, and every unlock writes the slot immediately.
    ///
    /// Unlike inventory items abilities are not a bag: no order, no capacity, no consuming. An id is
    /// either owned or it is not, for the whole life of the save slot.
    ///
    /// The pickup cards are retired: both abilities ship as built-in defaults. Every entry point
    /// (fresh boot, new game, save restore) seeds the default set, so old saves without the ids are
    /// topped up on load, and the next save write records them.
    /// </summary>
    public static class AbilityStore
    {
        private static readonly string[] DefaultAbilities = { AbilityIds.Checkpoint, AbilityIds.RopeGun };

        private static readonly HashSet<string> unlocked =
            new HashSet<string>(DefaultAbilities, StringComparer.Ordinal);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            unlocked.Clear();
            foreach (string ability in DefaultAbilities) unlocked.Add(ability);
        }

        public static bool Owns(string abilityId) =>
            !string.IsNullOrWhiteSpace(abilityId) && unlocked.Contains(abilityId);

        /// <summary>Grants an ability and writes the slot. false on a blank or already-owned id.</summary>
        public static bool Unlock(string abilityId)
        {
            if (string.IsNullOrWhiteSpace(abilityId) || !unlocked.Add(abilityId)) return false;

            SaveStore.RecordAbilities(SnapshotIds());
            return true;
        }

        public static void ClearWithoutSaving()
        {
            unlocked.Clear();
            foreach (string ability in DefaultAbilities) unlocked.Add(ability);
        }

        /// <summary>Loads the set from a save slot, unioned with the built-in defaults — a save
        /// from before the abilities shipped as defaults simply gets topped up. Never writes back —
        /// restoring is a read, and the restored values are already on disk (the defaults ride
        /// along on the next real save write).</summary>
        public static void Restore(string[] abilityIds)
        {
            unlocked.Clear();
            foreach (string ability in DefaultAbilities) unlocked.Add(ability);
            if (abilityIds == null) return;
            foreach (string abilityId in abilityIds)
            {
                if (!string.IsNullOrWhiteSpace(abilityId)) unlocked.Add(abilityId);
            }
        }

        public static string[] SnapshotIds()
        {
            var result = new string[unlocked.Count];
            unlocked.CopyTo(result);
            Array.Sort(result, StringComparer.Ordinal);
            return result;
        }
    }
}
