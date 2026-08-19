using System;
using System.Globalization;
using System.IO;
using Inkform.Bus;
using UnityEngine;

namespace Inkform.Save
{
    /// <summary>
    /// Save slots: the single source of truth for "what has been played and where it left off", three
    /// slots persisted as JSON under Application.persistentDataPath/Saves/slot{n}.json.
    ///
    /// Fully static, for the same reason SettingsStore is (see its class docs): a save is cross-domain
    /// infrastructure that Level / UI / Life all touch, and a static API needs zero scene or prefab
    /// edits — no new component on GameManager, no new serialized slot to wire. Same lifecycle pattern
    /// too: ResetStatics for Domain Reload off, a RuntimeInitializeOnLoadMethod that reads the files
    /// before the first scene.
    ///
    /// Two kinds of writer, both routed here:
    ///   - autosave — RespawnDirector calls RecordProgress every time the respawn point changes (a
    ///     checkpoint touched, or a room entered). It owns "where the player comes back", so it is the
    ///     only class that ever knows the coordinate worth saving.
    ///   - manual — SceneDirector.ReturnToMainMenu calls EndRun, which is what finally makes the pause
    ///     menu's "Save and Quit" live up to its name.
    ///
    /// ActiveSlot is what separates "a run in progress" from "just poking at a level": it is -1 until a
    /// slot is picked in the save menu, and RecordProgress is a silent no-op while it is. Opening a
    /// Generated scene straight from the editor therefore never touches a player's files.
    /// </summary>
    public static class SaveStore
    {
        public const int SlotCount = 3;

        private const string SavesFolder = "Saves";

        /// <summary>The slot the current run writes into; -1 = no run (the menu, or a level opened
        /// straight from the editor). Set by BeginNewRun / ContinueRun, cleared by EndRun.</summary>
        public static int ActiveSlot { get; private set; } = -1;

        /// <summary>Raised after any slot's contents changed. SaveMenuPanel re-reads its three rows;
        /// nothing else subscribes.</summary>
        public static event Action Changed;

        // The three slots, read from disk once at startup and kept in memory. A slot is never null —
        // a missing or unreadable file becomes an empty SaveData, so no reader needs a null check.
        private static SaveData[] slots;

        // ---- Run baselines ----
        // Play time and death count both accumulate across runs, but the sources they are measured from
        // are per-session: Time.realtimeSinceStartup counts from process start, and LifeBus.DeathCount
        // counts every death since play mode began (LifeBus only clears it on SubsystemRegistration).
        // Recording where this run began in each of those is what lets Stamp turn "since the process
        // started" into "since this save started" — without it, playing slot 0 and then starting slot 1
        // would carry slot 0's deaths straight into the new file.
        private static float runPlayBase;
        private static float runStartRealtime;
        private static int runDeathBase;
        private static int runDeathSessionBase;

        // ---- Lifecycle ----

        // Static fields do not clear on scene reload; with Domain Reload off, dead subscribers from the
        // previous run linger (same reason as the buses' and SettingsStore's ResetStatics)
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
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void LoadAll()
        {
            slots = new SaveData[SlotCount];
            for (int i = 0; i < SlotCount; i++) slots[i] = ReadSlot(i);
        }

        // The menu could ask for a slot before BeforeSceneLoad has run — ordering between the
        // RuntimeInitialize phases of different classes is not something to lean on — so every public
        // entry point goes through this rather than trusting LoadAll to have happened first.
        private static void EnsureLoaded()
        {
            if (slots == null) LoadAll();
        }

        // ---- Reads ----

        /// <summary>The slot's contents; an empty SaveData for an unused slot or an out-of-range
        /// index. Never null.</summary>
        public static SaveData Get(int slot)
        {
            EnsureLoaded();
            if (!IsValidSlot(slot)) return new SaveData();
            return slots[slot];
        }

        /// <summary>Whether this slot holds a run that can be continued.</summary>
        public static bool HasSave(int slot) => !Get(slot).IsEmpty;

        // ---- Run lifecycle ----

        /// <summary>
        /// Starts a fresh run in this slot, discarding whatever was there. The file is deleted right
        /// away rather than left for the first autosave to overwrite: quitting in the seconds before
        /// the entry room's first RecordProgress would otherwise leave the old save sitting on disk,
        /// and the slot would look un-overwritten on the next launch.
        /// </summary>
        public static void BeginNewRun(int slot)
        {
            EnsureLoaded();
            if (!IsValidSlot(slot)) return;

            slots[slot] = new SaveData();
            DeleteFile(slot);
            StartRun(slot, 0f, 0);
            Changed?.Invoke();
        }

        /// <summary>Resumes this slot's run: its play time and death count become the baseline this
        /// session adds onto. A slot with nothing in it falls through to a fresh run.</summary>
        public static void ContinueRun(int slot)
        {
            EnsureLoaded();
            if (!IsValidSlot(slot)) return;

            SaveData data = slots[slot];
            if (data.IsEmpty) { BeginNewRun(slot); return; }

            StartRun(slot, data.playSeconds, data.deaths);
        }

        private static void StartRun(int slot, float playBase, int deathBase)
        {
            ActiveSlot = slot;
            runPlayBase = playBase;
            runStartRealtime = Time.realtimeSinceStartup;
            runDeathBase = deathBase;
            runDeathSessionBase = LifeBus.DeathCount;
        }

        /// <summary>
        /// Autosave. Called by RespawnDirector whenever the respawn point is settled — a checkpoint
        /// touched, or a room entered — so a save always names a place the player can actually be put
        /// back onto. A no-op outside a run (see ActiveSlot).
        /// </summary>
        public static void RecordProgress(string sceneName, Vector2 spawn)
        {
            if (ActiveSlot < 0) return;
            if (string.IsNullOrEmpty(sceneName)) return;

            SaveData data = slots[ActiveSlot];
            data.sceneName = sceneName;
            data.spawnX = spawn.x;
            data.spawnY = spawn.y;

            Stamp(data);
            WriteSlot(ActiveSlot, data);
            Changed?.Invoke();
        }

        /// <summary>
        /// Manual save: refreshes play time / deaths / timestamp over whatever position the autosave
        /// last recorded, and writes. Public so a future standalone "Save" button has something to
        /// call; today the one caller is EndRun.
        ///
        /// Refuses a slot with no sceneName. Writing one would produce a file that shows up as a save
        /// in the menu and then cannot be loaded.
        /// </summary>
        public static void SaveNow()
        {
            if (ActiveSlot < 0) return;

            SaveData data = slots[ActiveSlot];
            if (data.IsEmpty) return;

            Stamp(data);
            WriteSlot(ActiveSlot, data);
            Changed?.Invoke();
        }

        /// <summary>Flushes and closes the run (leaving the game for the menu). After this,
        /// RecordProgress goes quiet again until a slot is picked.</summary>
        public static void EndRun()
        {
            SaveNow();
            ActiveSlot = -1;
        }

        /// <summary>Wipes a slot, on disk and in memory. Clears ActiveSlot when the run being played is
        /// the one deleted, so nothing writes the file back a moment later.</summary>
        public static void Delete(int slot)
        {
            EnsureLoaded();
            if (!IsValidSlot(slot)) return;

            slots[slot] = new SaveData();
            DeleteFile(slot);
            if (ActiveSlot == slot) ActiveSlot = -1;
            Changed?.Invoke();
        }

        private static void Stamp(SaveData data)
        {
            data.version = SaveData.CurrentVersion;

            // Unscaled and pause-blind by design: realtimeSinceStartup keeps running while the pause
            // menu holds Time.timeScale at 0. Of the two behaviours, a clock that stops whenever a menu
            // is open would be the stranger one to explain — and the alternative costs a MonoBehaviour
            // on GameManager, which this whole class exists to avoid.
            data.playSeconds = runPlayBase + Mathf.Max(0f, Time.realtimeSinceStartup - runStartRealtime);
            data.deaths = runDeathBase + Mathf.Max(0, LifeBus.DeathCount - runDeathSessionBase);
            data.savedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
        }

        private static bool IsValidSlot(int slot) => slot >= 0 && slot < SlotCount;

        // ---- Files ----

        private static string SavesDir => Path.Combine(Application.persistentDataPath, SavesFolder);

        private static string PathFor(int slot) => Path.Combine(SavesDir, "slot" + slot + ".json");

        /// <summary>
        /// Reads one slot. Every failure — no file, no permission, a truncated write, hand-edited JSON,
        /// a version this build does not know — lands on the same answer: an empty slot. Same "an
        /// unconfigured thing is a legal state" stance the rest of the project takes (AudioManager on a
        /// Cue with no clip, UIManager on an empty panel slot). A corrupt save must never be the reason
        /// the main menu fails to open.
        /// </summary>
        private static SaveData ReadSlot(int slot)
        {
            string path = PathFor(slot);
            try
            {
                if (!File.Exists(path)) return new SaveData();

                SaveData data = JsonUtility.FromJson<SaveData>(File.ReadAllText(path));
                if (data == null || data.version != SaveData.CurrentVersion)
                {
                    Debug.LogWarning($"SaveStore: slot {slot} is not a version {SaveData.CurrentVersion} save — treating it as empty ({path})");
                    return new SaveData();
                }
                return data;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"SaveStore: could not read slot {slot} — treating it as empty ({e.Message})");
                return new SaveData();
            }
        }

        private static void WriteSlot(int slot, SaveData data)
        {
            try
            {
                Directory.CreateDirectory(SavesDir);
                File.WriteAllText(PathFor(slot), JsonUtility.ToJson(data, true));
            }
            catch (Exception e)
            {
                // Warn, never throw: this runs from a checkpoint trigger in the middle of play. A full
                // disk or a locked file should cost the player their save, not their run.
                Debug.LogWarning($"SaveStore: could not write slot {slot} ({e.Message})");
            }
        }

        private static void DeleteFile(int slot)
        {
            try
            {
                string path = PathFor(slot);
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"SaveStore: could not delete slot {slot} ({e.Message})");
            }
        }
    }
}
