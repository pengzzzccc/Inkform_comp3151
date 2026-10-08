using System;
using System.Collections.Generic;
using Inkform.Save;
using UnityEngine;

namespace Inkform.Ability
{
    /// <summary>
    /// Save-level ability unlocks: which permanent capabilities (e.g. using checkpoints) this run's
    /// save slot has earned. Same static-store pattern as InventoryStore — abilities are cross-domain
    /// data (world pickups grant them, Checkpoint gates on them, the save file persists them), so a
    /// static API with no scene wiring fits, and every unlock writes the slot immediately: an ability
    /// is never lost to a crash right after pickup.
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
        public static event Action Changed;

        private static readonly string[] DefaultAbilities = { AbilityIds.Checkpoint, AbilityIds.RopeGun };

        private static readonly HashSet<string> unlocked =
            new HashSet<string>(DefaultAbilities, StringComparer.Ordinal);
        private static bool suppressPersistence;

        public static IReadOnlyCollection<string> Unlocked => unlocked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Changed = null;
            unlocked.Clear();
            foreach (string ability in DefaultAbilities) unlocked.Add(ability);
            suppressPersistence = false;
        }

        public static bool Owns(string abilityId) =>
            !string.IsNullOrWhiteSpace(abilityId) && unlocked.Contains(abilityId);

        /// <summary>Grants an ability. false on a blank or already-owned id — callers use that to
        /// skip duplicate feedback the same way TryCollectCapacityPickup does.</summary>
        public static bool Unlock(string abilityId)
        {
            if (string.IsNullOrWhiteSpace(abilityId) || !unlocked.Add(abilityId)) return false;

            Notify();
            return true;
        }

        public static void ClearWithoutSaving()
        {
            bool changed = unlocked.Count != DefaultAbilities.Length;
            unlocked.Clear();
            foreach (string ability in DefaultAbilities) unlocked.Add(ability);
            if (changed) Changed?.Invoke();
        }

#if UNITY_EDITOR
        /// <summary>Editor cheat (F4): flips both card abilities in memory only. A cheat granted
        /// for one test run must never be written into the player's save slot, so this goes through
        /// the same suppressPersistence gate Restore uses — Changed still fires, subscribers (the
        /// rope-gun reticle, tutorial panels) refresh immediately, but Notify skips the save write.
        /// Caveat: turning the cheat OFF removes the ids from memory; if a later real unlock then
        /// triggers a save write, the cheat-held abilities are absent from that snapshot — they come
        /// back on the next load, and a normal playthrough never hits this.</summary>
        public static void SetCardsForCheat(bool owned)
        {
            suppressPersistence = true;
            try
            {
                bool changed = false;
                if (owned ? unlocked.Add(AbilityIds.Checkpoint) : unlocked.Remove(AbilityIds.Checkpoint)) changed = true;
                if (owned ? unlocked.Add(AbilityIds.RopeGun) : unlocked.Remove(AbilityIds.RopeGun)) changed = true;
                if (changed) Changed?.Invoke();
            }
            finally
            {
                suppressPersistence = false;
            }
        }
#endif

        /// <summary>Loads the set from a save slot, unioned with the built-in defaults — a save
        /// from before the abilities shipped as defaults simply gets topped up. Raises Changed
        /// once and never writes back — restoring is a read, and the restored values are already
        /// on disk (the defaults ride along on the next real save write).</summary>
        public static void Restore(string[] abilityIds)
        {
            suppressPersistence = true;
            try
            {
                unlocked.Clear();
                foreach (string ability in DefaultAbilities) unlocked.Add(ability);
                if (abilityIds != null)
                {
                    foreach (string abilityId in abilityIds)
                    {
                        if (!string.IsNullOrWhiteSpace(abilityId)) unlocked.Add(abilityId);
                    }
                }
            }
            finally
            {
                suppressPersistence = false;
            }

            Changed?.Invoke();
        }

        public static string[] SnapshotIds()
        {
            var result = new string[unlocked.Count];
            unlocked.CopyTo(result);
            Array.Sort(result, StringComparer.Ordinal);
            return result;
        }

        private static void Notify()
        {
            Changed?.Invoke();
            if (!suppressPersistence) SaveStore.RecordAbilities(SnapshotIds());
        }
    }
}
