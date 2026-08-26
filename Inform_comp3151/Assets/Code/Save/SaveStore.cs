using System;
using System.Globalization;
using System.IO;
using System.Text;
using Inkform.Bus;
using Inkform.Item;
using Inkform.Progression;
using UnityEngine;

namespace Inkform.Save
{
    /// <summary>Three transactional, atomically-written save slots.</summary>
    public static class SaveStore
    {
        public const int SlotCount = 3;
        private const string SavesFolder = "Saves";

        public static int ActiveSlot { get; private set; } = -1;
        public static event Action Changed;

        private static SaveData[] slots;
        private static float runPlayBase;
        private static float runStartRealtime;
        private static int runDeathBase;
        private static int runDeathSessionBase;

        // An overwrite remains reversible until the entry scene records its first valid position.
        private static int pendingNewSlot = -1;
        private static SaveData pendingPrevious;
#if UNITY_INCLUDE_TESTS
        private static string testSavesDirectory;
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Changed = null;
            ActiveSlot = -1;
            slots = null;
            runPlayBase = 0f;
            runStartRealtime = 0f;
            runDeathBase = 0;
            runDeathSessionBase = 0;
            pendingNewSlot = -1;
            pendingPrevious = null;
#if UNITY_INCLUDE_TESTS
            testSavesDirectory = null;
#endif
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void LoadAll()
        {
            slots = new SaveData[SlotCount];
            for (int i = 0; i < SlotCount; i++) slots[i] = ReadSlot(i);
        }

        private static void EnsureLoaded()
        {
            if (slots == null) LoadAll();
        }

        public static SaveData Get(int slot)
        {
            EnsureLoaded();
            return IsValidSlot(slot) ? slots[slot] : new SaveData();
        }

        public static bool HasSave(int slot) => !Get(slot).IsEmpty;

        public static void BeginNewRun(int slot)
        {
            EnsureLoaded();
            if (!IsValidSlot(slot)) return;
            if (pendingNewSlot >= 0) AbortNewRun();

            pendingNewSlot = slot;
            pendingPrevious = Clone(slots[slot]);
            slots[slot] = new SaveData();
            InventoryStore.ClearWithoutSaving();
            EquipmentProgressStore.ClearWithoutSaving();
            StartRun(slot, 0f, 0);
            Changed?.Invoke();
        }

        public static bool AbortNewRun()
        {
            EnsureLoaded();
            if (pendingNewSlot < 0) return false;

            int slot = pendingNewSlot;
            slots[slot] = pendingPrevious ?? new SaveData();
            ActiveSlot = -1;
            ClearPending();
            InventoryStore.Restore(slots[slot].inventoryItemIds, slots[slot].selectedInventoryIndex);
            RestoreEquipment(slots[slot]);
            Changed?.Invoke();
            return true;
        }

        public static void ContinueRun(int slot)
        {
            EnsureLoaded();
            if (!IsValidSlot(slot)) return;

            SaveData data = slots[slot];
            if (data.IsEmpty) { BeginNewRun(slot); return; }

            ClearPending();
            StartRun(slot, data.playSeconds, data.deaths);
            InventoryStore.Restore(data.inventoryItemIds, data.selectedInventoryIndex);
            RestoreEquipment(data);
        }

        private static void StartRun(int slot, float playBase, int deathBase)
        {
            ActiveSlot = slot;
            runPlayBase = playBase;
            runStartRealtime = Time.realtimeSinceStartup;
            runDeathBase = deathBase;
            runDeathSessionBase = LifeBus.DeathCount;
        }

        public static void RecordProgress(string sceneName, Vector2 spawn)
        {
            if (ActiveSlot < 0 || string.IsNullOrEmpty(sceneName)) return;

            SaveData data = slots[ActiveSlot];
            data.sceneName = sceneName;
            data.spawnX = spawn.x;
            data.spawnY = spawn.y;
            CaptureInventory(data);
            CaptureEquipment(data);
            Stamp(data);

            if (WriteSlot(ActiveSlot, data)) CommitPendingIfNeeded(ActiveSlot);
            Changed?.Invoke();
        }

        public static void RecordInventory(string[] ids, int selectedIndex)
        {
            if (ActiveSlot < 0) return;
            SaveData data = slots[ActiveSlot];
            data.inventoryItemIds = ids ?? Array.Empty<string>();
            data.selectedInventoryIndex = selectedIndex;
            CaptureEquipment(data);

            // A brand-new run is not committed until RespawnDirector supplies a loadable scene/position.
            if (data.IsEmpty) return;

            Stamp(data);
            if (WriteSlot(ActiveSlot, data)) CommitPendingIfNeeded(ActiveSlot);
            Changed?.Invoke();
        }

        public static void SaveNow()
        {
            if (ActiveSlot < 0) return;
            SaveData data = slots[ActiveSlot];
            if (data.IsEmpty) return;

            CaptureInventory(data);
            CaptureEquipment(data);
            Stamp(data);
            if (WriteSlot(ActiveSlot, data)) CommitPendingIfNeeded(ActiveSlot);
            Changed?.Invoke();
        }

        public static void EndRun()
        {
            if (ActiveSlot < 0) return;
            int slot = ActiveSlot;

            if (slot == pendingNewSlot && slots[slot].IsEmpty)
            {
                slots[slot] = pendingPrevious ?? new SaveData();
                ClearPending();
                Changed?.Invoke();
            }
            else
            {
                SaveNow();
            }
            ActiveSlot = -1;
        }

        public static void Delete(int slot)
        {
            EnsureLoaded();
            if (!IsValidSlot(slot)) return;

            slots[slot] = new SaveData();
            DeleteFiles(slot);
            if (ActiveSlot == slot) ActiveSlot = -1;
            if (pendingNewSlot == slot) ClearPending();
            Changed?.Invoke();
        }

        private static void CaptureInventory(SaveData data)
        {
            data.inventoryItemIds = InventoryStore.SnapshotIds();
            data.selectedInventoryIndex = InventoryStore.SelectedIndex;
        }

        /// <summary>Persists the one-time Rope Gun pickup without coupling the world pickup to files.</summary>
        public static void RecordRopeGunUnlocked(bool unlocked)
        {
            if (ActiveSlot < 0) return;

            SaveData data = slots[ActiveSlot];
            data.ropeGunUnlocked = unlocked;

            // A brand-new run stays transactional until its entry scene records a valid position.
            if (data.IsEmpty) return;

            Stamp(data);
            if (WriteSlot(ActiveSlot, data)) CommitPendingIfNeeded(ActiveSlot);
            Changed?.Invoke();
        }

        private static void CaptureEquipment(SaveData data)
        {
            data.ropeGunUnlocked = EquipmentProgressStore.RopeGunUnlocked;
        }

        private static void RestoreEquipment(SaveData data) =>
            EquipmentProgressStore.Restore(data != null && data.ropeGunUnlocked);

        private static void Stamp(SaveData data)
        {
            data.version = SaveData.CurrentVersion;
            data.playSeconds = runPlayBase + Mathf.Max(0f, Time.realtimeSinceStartup - runStartRealtime);
            data.deaths = runDeathBase + Mathf.Max(0, LifeBus.DeathCount - runDeathSessionBase);
            data.savedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
        }

        private static void CommitPendingIfNeeded(int slot)
        {
            if (pendingNewSlot == slot) ClearPending();
        }

        private static void ClearPending()
        {
            pendingNewSlot = -1;
            pendingPrevious = null;
        }

        private static bool IsValidSlot(int slot) => slot >= 0 && slot < SlotCount;

        private static string SavesDir
        {
            get
            {
#if UNITY_INCLUDE_TESTS
                if (!string.IsNullOrEmpty(testSavesDirectory)) return testSavesDirectory;
#endif
                return Path.Combine(Application.persistentDataPath, SavesFolder);
            }
        }

#if UNITY_INCLUDE_TESTS
        public static void UseTestSaveDirectory(string path)
        {
            ResetStatics();
            testSavesDirectory = path;
        }
#endif
        private static string PathFor(int slot) => Path.Combine(SavesDir, $"slot{slot}.json");
        private static string TempPathFor(int slot) => PathFor(slot) + ".tmp";
        private static string BackupPathFor(int slot) => PathFor(slot) + ".bak";

        private static SaveData ReadSlot(int slot)
        {
            string primary = PathFor(slot);
            string temp = TempPathFor(slot);
            string backup = BackupPathFor(slot);

            foreach (string candidate in new[] { primary, temp, backup })
            {
                if (!TryRead(candidate, out SaveData data)) continue;

                if (candidate != primary)
                {
                    Debug.LogWarning($"SaveStore: recovered slot {slot} from '{Path.GetFileName(candidate)}'");
                    WriteSlot(slot, data);
                }
                return data;
            }
            return new SaveData();
        }

        private static bool TryRead(string path, out SaveData data)
        {
            data = null;
            try
            {
                if (!File.Exists(path)) return false;
                data = JsonUtility.FromJson<SaveData>(File.ReadAllText(path));
                if (data == null) return false;

                if (data.version == 1)
                {
                    data.inventoryItemIds = Array.Empty<string>();
                    data.selectedInventoryIndex = 0;
                }
                else if (data.version != 2 && data.version != SaveData.CurrentVersion)
                {
                    return false;
                }

                data.version = SaveData.CurrentVersion;
                data.inventoryItemIds ??= Array.Empty<string>();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"SaveStore: could not read '{path}' ({e.Message})");
                data = null;
                return false;
            }
        }

        private static bool WriteSlot(int slot, SaveData data)
        {
            string primary = PathFor(slot);
            string temp = TempPathFor(slot);
            string backup = BackupPathFor(slot);

            try
            {
                Directory.CreateDirectory(SavesDir);
                byte[] bytes = new UTF8Encoding(false).GetBytes(JsonUtility.ToJson(data, true));

                using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }

                if (File.Exists(primary))
                    File.Replace(temp, primary, backup, true);
                else
                    File.Move(temp, primary);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"SaveStore: could not atomically write slot {slot} ({e.Message})");
                return false;
            }
        }

        private static void DeleteFiles(int slot)
        {
            foreach (string path in new[] { PathFor(slot), TempPathFor(slot), BackupPathFor(slot) })
            {
                try
                {
                    if (File.Exists(path)) File.Delete(path);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"SaveStore: could not delete '{path}' ({e.Message})");
                }
            }
        }

        private static SaveData Clone(SaveData source)
        {
            if (source == null) return new SaveData();
            SaveData clone = JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(source));
            clone.inventoryItemIds ??= Array.Empty<string>();
            return clone;
        }
    }
}
