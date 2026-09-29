using System;
using System.Collections;
using System.Collections.Generic;
using Inkform.Bus;
using Inkform.Item;
using Inkform.Life;
using Inkform.Player;
using Inkform.Settings;
using Inkform.Tool;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.DualShock;
using UnityEngine.InputSystem.Haptics;

namespace Inkform.Fx
{
    /// <summary>
    /// Gamepad haptics: translates "what happened in the game" into left/right motor rumble. Pure
    /// event-driven — subscribes to buses, never polls. Same director pattern as FxDirector /
    /// AudioDirector: tuning for "how strong each event rumbles" is centralized in this one Inspector;
    /// RopeGun / PlayerHandler never need to know haptics exist.
    ///
    /// Motor direction semantics (Xbox / most gamepads: big motor left, small motor right):
    ///  - land (directionless): both motors split the strength evenly
    ///  - rope fired / hit (directional): strength split by the horizontal component of the direction
    ///  - blast: shockwave model — the left/right motor triggers in the order the blast front sweeps
    ///    across the player's body (near side first, far side delayed by the travel-time difference),
    ///    with the overall strength attenuated by distance from the blast center
    ///
    /// Device layer: every motor write goes through WriteMotors — a change-guarded entry that routes
    /// PlayStation pads through their merged "motors + light bar" command (calling the two separately
    /// is documented to silently drop one of them), and skips writes entirely when nothing changed
    /// (each write is a HID output report). DualShock pads also get a light bar state machine:
    /// green in play, red on death, blue in the pause menu. Focus loss / suspend / quit pause the
    /// pad's haptics, so the motors can never hold a stale rumble while the app is in the background.
    ///
    /// Freeze semantics: only a true world freeze (map view, tutorial) silences the pad — timers park
    /// and the motors zero. Hitstop keeps running on unscaled time, so the death rumble plays through
    /// its freeze, and the user pause does too, so menu clicks buzz at once instead of piling up.
    ///
    /// Implementation: a scheduled queue + active list driven by a single coroutine. All timing uses
    /// unscaled time — a blast triggers hitstop (timeScale = 0), and WaitForSeconds would freeze.
    /// Overlapping rumbles take the per-motor max, so a short rumble never cuts a longer one.
    /// Attach to GameManager (the DontDestroyOnLoad host), survives scene switches.
    /// </summary>
    public class RumbleManager : MonoBehaviour
    {
        // All strengths are tuned so the *weakest* common pad (Xbox / XInput — its motors barely
        // respond below ~15% duty) still feels every event; PlayStation pads read the same table
        // louder. The tier references are Celeste's rumble tables — its Player.cs gives death a
        // Light/Medium rumble, the audio-visual hit already carries the weight, touch stays
        // restrained. High-frequency player moves (jump, wall jump) rumble nothing there, and the
        // same restraint applies here. The player-facing RumbleLevel setting scales all of these
        // further. Serialized overrides live on GameManager.prefab — keep the two in sync.
        [Header("Land")]
        [SerializeField] private float landStrength = 0.4f;
        [SerializeField] private float landDuration = 0.12f;
        [Tooltip("Minimum seconds between land rumbles — ground-contact jitter at a ledge edge re-fires Land every few frames")]
        [SerializeField] private float landCooldown = 0.25f;

        [Header("Blast (shockwave)")]
        [SerializeField] private float blastStrength = 0.9f;
        [SerializeField] private float blastDuration = 0.22f;
        [SerializeField] private float blastFalloff = 20f;                 // blasts farther than this are completely unfelt
        [Tooltip("Shockwave speed: drives the left/right motor delay. Lower = stronger sweep feel")]
        [SerializeField] private float shockSpeed = 30f;
        [Tooltip("Player body half-width, used to compute the shockwave's arrival-time difference across the body")]
        [SerializeField] private float playerHalfWidth = 0.5f;

        [Header("Rope fired")]
        [SerializeField] private float fireStrength = 0.25f;
        [SerializeField] private float fireDuration = 0.08f;

        [Header("Rope hit")]
        [SerializeField] private float hitStrength = 0.6f;
        [SerializeField] private float hitDuration = 0.15f;

        [Header("Life")]
        [SerializeField] private float deathStrength = 0.5f;       // Medium
        [SerializeField] private float deathDuration = 0.25f;      // Medium
        [SerializeField] private float checkpointStrength = 0.22f; // Light
        [SerializeField] private float checkpointDuration = 0.25f;
        [SerializeField] private float respawnStrength = 0.2f;     // Light — a teleport home, not an impact
        [SerializeField] private float respawnDuration = 0.1f;

        [Header("Dash")]
        [SerializeField] private float dashStrength = 0.45f;
        [SerializeField] private float dashDuration = 0.1f;
        [SerializeField] private float dashFailStrength = 0.2f;    // no fuel: a faint tick, not a reward
        [SerializeField] private float dashFailDuration = 0.06f;

        [Header("Hazard")]
        [SerializeField] private float tickStrength = 0.2f;        // bomb fuse warning pulse
        [SerializeField] private float tickDuration = 0.05f;
        [SerializeField] private float brokenStrength = 0.2f;      // wall shattered nearby
        [SerializeField] private float brokenDuration = 0.1f;

        [Header("Items")]
        [SerializeField] private float storeStrength = 0.45f;      // swallow
        [SerializeField] private float storeDuration = 0.1f;
        [SerializeField] private float releaseStrength = 0.2f;     // spit (Celeste's Drop tier)
        [SerializeField] private float releaseDuration = 0.1f;
        [SerializeField] private float upgradeStrength = 0.5f;     // capacity / ability — permanent progress
        [SerializeField] private float upgradeDuration = 0.25f;

        [Header("UI")]
        [SerializeField] private float uiStrength = 0.22f;
        [SerializeField] private float uiClickDuration = 0.1f;
        [SerializeField] private float uiToggleDuration = 0.1f;

        [Header("Motors")]
        [Tooltip("Floor for any motor above zero — Xbox/XInput pads barely respond below ~15% duty, so small values would be imperceptible")]
        [SerializeField] private float minMotorLevel = 0.12f;

        [Header("PlayStation")]
        [Tooltip("DualSense HID rumble. The Input System package cannot send valid Bluetooth output reports for the DualSense — a USB-format report goes over the BT link and the pad latches a full-power rumble. Bluetooth is auto-detected and skipped; turn this off if that ever fails.")]
        [SerializeField] private bool dualSenseRumble = true;

        private struct ScheduledRumble
        {
            public float remainingDelay;
            public float low;
            public float high;
            public float duration;
        }

        private struct ActiveRumble
        {
            public float remainingDuration;
            public float low;
            public float high;
        }

        private readonly List<ScheduledRumble> scheduled = new List<ScheduledRumble>();
        private readonly List<ActiveRumble> active = new List<ActiveRumble>();
        private Coroutine driveLoop;

        // ---- Device write state ----

        // Last levels the Drive loop commanded (0 while idle) — the light bar pushes ride on them.
        private float driveLow, driveHigh;
        // NaN = "device state unknown": the next write must go out even if the levels did not change.
        private float lastWrittenLow = float.NaN, lastWrittenHigh = float.NaN;

        // Light bar states (DualShock pads): the project's highlight green in play, red while dead,
        // the pad-conventional blue in menus. Pure feedback — no logic reads it back.
        private static readonly Color32 LightBarGameplay = new Color32(132, 255, 84, 255);
        private static readonly Color32 LightBarPaused = new Color32(80, 140, 255, 255);
        private static readonly Color32 LightBarDead = new Color32(255, 64, 64, 255);
        private Color32 currentLightBar = LightBarGameplay;
        private bool playerDead;

        private bool hapticsSuspended;

        private float lastLandRealtime = -10f;

        private static readonly HashSet<Gamepad> LoggedPads = new HashSet<Gamepad>();

        // Statics survive Domain Reload off; the sets would keep pads from previous sessions logged.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            LoggedPads.Clear();
            DualSenseSkipLogged.Clear();
        }

        /// <summary>The setting tier is Off, or gameplay input is filtered to keyboard/mouse — a pad
        /// resting on the desk next to a keyboard player must not buzz.</summary>
        private static bool RumbleActive =>
            SettingsStore.Rumble != SettingsStore.RumbleLevel.Off
            && SettingsStore.Device == SettingsStore.InputDevice.Gamepad;

        void OnEnable()
        {
            PlayerBus.StateChanged += OnPlayerState;
            PlayerBus.DashAttempted += OnDashAttempted;
            HazardBus.Blast += OnBlast;
            HazardBus.Ticked += OnTicked;
            HazardBus.Broken += OnBroken;
            RopeGunBus.Fired += OnRopeFired;
            RopeGunBus.Hit += OnRopeHit;
            LifeBus.Died += OnDied;
            LifeBus.CheckpointSet += OnCheckpointSet;
            LifeBus.Respawned += OnRespawned;
            ItemBus.ItemStored += OnItemStored;
            ItemBus.ItemReleased += OnItemReleased;
            ItemBus.InventoryCapacityUpgraded += OnCapacityUpgraded;
            ItemBus.AbilityUnlocked += OnAbilityUnlocked;
            UiBus.Clicked += OnUiClicked;
            UiBus.Toggled += OnUiToggled;

            SettingsStore.Changed += OnSettingsChanged;

            // A gamepad swapped mid-session leaves the old device object behind; react so motors
            // never keep targeting a dead device and the new pad is usable immediately
            InputSystem.onDeviceChange += OnDeviceChange;

            // Pads already connected before this subscribe never raise Added — log them now
            var pads = Gamepad.all;
            for (int i = 0; i < pads.Count; i++)
                LogPadDiagnostics(pads[i]);
        }

        void OnDisable()
        {
            PlayerBus.StateChanged -= OnPlayerState;
            PlayerBus.DashAttempted -= OnDashAttempted;
            HazardBus.Blast -= OnBlast;
            HazardBus.Ticked -= OnTicked;
            HazardBus.Broken -= OnBroken;
            RopeGunBus.Fired -= OnRopeFired;
            RopeGunBus.Hit -= OnRopeHit;
            LifeBus.Died -= OnDied;
            LifeBus.CheckpointSet -= OnCheckpointSet;
            LifeBus.Respawned -= OnRespawned;
            ItemBus.ItemStored -= OnItemStored;
            ItemBus.ItemReleased -= OnItemReleased;
            ItemBus.InventoryCapacityUpgraded -= OnCapacityUpgraded;
            ItemBus.AbilityUnlocked -= OnAbilityUnlocked;
            UiBus.Clicked -= OnUiClicked;
            UiBus.Toggled -= OnUiToggled;

            SettingsStore.Changed -= OnSettingsChanged;
            InputSystem.onDeviceChange -= OnDeviceChange;
            StopAllCoroutines();
            driveLoop = null;
            StopRumble();
        }

        private void OnSettingsChanged()
        {
            if (SettingsStore.Rumble == SettingsStore.RumbleLevel.Off) StopRumble();
        }

        // Disconnected/removed pads: their device objects can linger as Gamepad.current, where
        // every motor write becomes a silent no-op — clear the queues so rumble neither hums on a
        // dead device nor "transfers" wholesale to whichever pad becomes current next. The next
        // write re-targets through ActivePad, so the new pad picks up from the following event.
        private void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (device is not Gamepad pad) return;

            switch (change)
            {
                case InputDeviceChange.Added:
                case InputDeviceChange.Enabled:
                    LogPadDiagnostics(pad);
                    break;
                case InputDeviceChange.Disconnected:
                case InputDeviceChange.Removed:
                    StopRumble();
                    break;
            }
        }

        // Clears every queued/active rumble and stops the drive loop. ResetHaptics (where the pad
        // supports it) also clears the device's cached motor speeds, so a later ResumeHaptics —
        // ours after focus regain, or any third party's — cannot resurrect a rumble we consider off.
        private void StopRumble()
        {
            scheduled.Clear();
            active.Clear();
            if (driveLoop != null)
            {
                StopCoroutine(driveLoop);
                driveLoop = null;
            }
            driveLow = driveHigh = 0f;
            lastWrittenLow = float.NaN;      // the device state is about to change behind the guard's back
            Gamepad pad = ActivePad;
            if (pad is IHaptics haptics) haptics.ResetHaptics();
            else pad?.SetMotorSpeeds(0f, 0f);
        }

        // The pad the player is on right now. Deliberately no fallback to Gamepad.all: with two pads
        // connected, the one nobody is holding must not buzz — the pad that produced the last input
        // is Gamepad.current, and a rumble aimed anywhere else is a bug, not a convenience.
        private static Gamepad ActivePad =>
            Gamepad.current != null && Gamepad.current.added ? Gamepad.current : null;

        // ---- Device write path ----

        // The one motor entry: change-guarded (each write is a HID output report — writing the same
        // levels every frame costs USB I/O for nothing), floored (XInput pads have a low-duty dead
        // zone), then routed per device. PlayStation pads take the merged "motors + light bar"
        // command: the package documents that setting motors and the light bar separately can
        // silently drop one of the two.
        private void WriteMotors(float low, float high)
        {
            // XInput low-duty dead zone: any motor that is on at all must be at least perceptible.
            if (minMotorLevel > 0f)
            {
                if (low > 0f) low = Mathf.Max(low, minMotorLevel);
                if (high > 0f) high = Mathf.Max(high, minMotorLevel);
            }

            driveLow = low;
            driveHigh = high;
            if (lastWrittenLow == low && lastWrittenHigh == high) return;

            Gamepad pad = ActivePad;
            LogPadDiagnostics(pad);
            if (pad == null) return;

            lastWrittenLow = low;
            lastWrittenHigh = high;
            WriteNow(pad, low, high);
        }

        private void WriteNow(Gamepad pad, float low, float high)
        {
            switch (pad)
            {
                case DualSenseGamepadHID dualSense:
                    if (!dualSenseRumble || DualSenseBluetoothSuspected(dualSense))
                    {
                        LogDualSenseSkip(dualSense);
                        return;   // no output report at all — a USB-format report on the BT link latches full-power rumble
                    }
                    dualSense.SetMotorSpeedsAndLightBarColor(low, high, currentLightBar);
                    break;
                case DualShock4GamepadHID dualShock4:
                    dualShock4.SetMotorSpeedsAndLightBarColor(low, high, currentLightBar);
                    break;
                default:
                    pad.SetMotorSpeeds(low, high);
                    break;
            }
        }

        private static bool SupportsLightBar(Gamepad pad) =>
            pad is DualSenseGamepadHID or DualShock4GamepadHID;

        // The package sends the DualSense's USB-shaped output report on the Bluetooth link, where
        // the pad parses it at BT offsets and can latch a full-power rumble (its own source marks
        // the BT path "FIXME: not working"). Best-effort transport sniff from the capabilities JSON
        // — an explicit "bluetooth" hint, or the BT-sized output report (>64 bytes; USB is 32/64).
        // When in doubt the pad still rumbles: the dualSenseRumble toggle is the manual way out.
        private static bool DualSenseBluetoothSuspected(DualSenseGamepadHID pad)
        {
            string capabilities = pad.description.capabilities;
            if (string.IsNullOrEmpty(capabilities)) return false;

            if (capabilities.IndexOf("bluetooth", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            const string key = "outputreportsize";
            int keyIndex = capabilities.IndexOf(key, StringComparison.OrdinalIgnoreCase);
            if (keyIndex < 0) return false;

            int valueStart = keyIndex + key.Length;
            while (valueStart < capabilities.Length && (char.IsWhiteSpace(capabilities[valueStart]) || capabilities[valueStart] == ':' || capabilities[valueStart] == '"' || capabilities[valueStart] == ','))
                valueStart++;

            int valueEnd = valueStart;
            while (valueEnd < capabilities.Length && char.IsDigit(capabilities[valueEnd]))
                valueEnd++;

            return valueEnd > valueStart
                && int.TryParse(capabilities[valueStart..valueEnd], out int size)
                && size > 64;
        }

        private static readonly HashSet<Gamepad> DualSenseSkipLogged = new HashSet<Gamepad>();

        private static void LogDualSenseSkip(DualSenseGamepadHID pad)
        {
            if (!DualSenseSkipLogged.Add(pad)) return;
            Debug.Log($"[RumbleManager] {pad.displayName}: rumble output suppressed — " +
                      (dualSenseBtReason(pad) ?? "the dualSenseRumble toggle is off") +
                      ". Connect the pad via USB for rumble, or clear the latched motors by power-cycling it.");
        }

        private static string dualSenseBtReason(DualSenseGamepadHID pad) =>
            DualSenseBluetoothSuspected(pad) ? "Bluetooth connection detected" : null;

        // One diagnostics line per pad per session: when a player reports "no rumble", this names
        // the pad, its transport and the raw capability hints the BT sniff had to work with. The
        // DualSense-over-Bluetooth case is a known Input System package limitation and shows up here.
        private static void LogPadDiagnostics(Gamepad pad)
        {
            if (pad == null || !LoggedPads.Add(pad)) return;

            string features = pad switch
            {
                // Switch expressions do not narrow the matched variable, so the BT sniff needs the
                // pattern-typed arm variable, not `pad` (still Gamepad here).
                DualSenseGamepadHID dualSense => DualSenseBluetoothSuspected(dualSense)
                    ? "DualSense (Bluetooth suspected — rumble suppressed, connect via USB)"
                    : "DualSense (dual motor + light bar; rumble needs USB — Bluetooth output is not implemented by the Input System package)",
                DualShock4GamepadHID => "DualShock 4 (dual motor + light bar)",
                _ => pad is IHaptics ? "generic pad (dual motor)" : "generic pad (no haptics support detected)",
            };
            string capabilities = pad.description.capabilities ?? string.Empty;
            if (capabilities.Length > 120) capabilities = capabilities[..120] + "…";
            Debug.Log($"[RumbleManager] pad: {pad.displayName} via {pad.description.interfaceName} — {features}; capabilities: {capabilities}");
        }

        // ---- Light bar state machine (DualShock pads; pure feedback) ----

        private void Update() => UpdateLightBar();

        private void UpdateLightBar()
        {
            Color32 target = playerDead ? LightBarDead
                : GameTimeController.Instance != null && GameTimeController.Instance.IsUserPaused
                    ? LightBarPaused
                    : LightBarGameplay;
            if (target.Equals(currentLightBar)) return;

            currentLightBar = target;
            Gamepad pad = ActivePad;
            if (pad == null || !SupportsLightBar(pad)) return;

            // Push immediately at the current motor level (merged command), and re-arm the guard:
            // the next Drive write at unchanged levels must still go out if levels differ from the
            // ones written here.
            lastWrittenLow = driveLow;
            lastWrittenHigh = driveHigh;
            WriteNow(pad, driveLow, driveHigh);
        }

        // ---- Focus / suspend: the motors must never hold a stale rumble in the background ----
        // runInBackground is off, so the Drive coroutine simply stops running while unfocused —
        // without these, whatever levels were last written keep buzzing until the player returns.

        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus) ResumeAfterSuspend();
            else SuspendHaptics();
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) SuspendHaptics();
            else ResumeAfterSuspend();
        }

        private void OnApplicationQuit() => SuspendHaptics();

        private void SuspendHaptics()
        {
            if (hapticsSuspended) return;
            hapticsSuspended = true;
            StopRumble();
            if (ActivePad is IHaptics haptics) haptics.PauseHaptics();
        }

        private void ResumeAfterSuspend()
        {
            if (!hapticsSuspended) return;
            hapticsSuspended = false;
            // StopRumble cleared the device's cached speeds (ResetHaptics), so a plain Resume has
            // nothing to resurrect; the guard is re-armed by the NaN in lastWrittenLow.
            if (ActivePad is IHaptics haptics) haptics.ResumeHaptics();
        }

        private void OnPlayerState(PlayerState state)
        {
            if (state != PlayerState.Land) return;

            // Ground-contact jitter (grazing a ledge edge) re-fires Land every few frames; the
            // cooldown keeps the land rumble readable without tracking fall velocity.
            if (Time.unscaledTime - lastLandRealtime < landCooldown) return;
            lastLandRealtime = Time.unscaledTime;

            AddNow(landStrength, landStrength, landDuration);
        }

        // Shockwave: the blast front reaches the near half of the body first, so that motor fires
        // first; the far half fires after the travel-time difference. Overall strength attenuates by
        // distance from the blast center.
        private void OnBlast(Vector2 center, float radius, float force)
        {
            Transform p = Player;
            if (p == null) return;

            float dist = Vector2.Distance(center, p.position);
            float k = blastFalloff <= 0f
                ? 1f
                : Mathf.Clamp01(1f - dist / blastFalloff);
            if (k <= 0f) return;

            float strength = blastStrength * k;
            float w = Mathf.Max(0f, playerHalfWidth);

            // Distances to the left/right half of the body; their difference is the sweep delay
            float dL = Vector2.Distance(center, p.position + new Vector3(-w, 0f, 0f));
            float dR = Vector2.Distance(center, p.position + new Vector3(w, 0f, 0f));

            float speed = Mathf.Max(0.01f, shockSpeed);
            if (dL <= dR)
            {
                AddAfter(dL / speed, strength, 0f, blastDuration);   // left half first
                AddAfter(dR / speed, 0f, strength, blastDuration);   // right half later
            }
            else
            {
                AddAfter(dR / speed, 0f, strength, blastDuration);   // right half first
                AddAfter(dL / speed, strength, 0f, blastDuration);   // left half later
            }
        }

        private void OnRopeFired(Vector2 dir) => AddNow(dir, fireStrength, fireDuration);

        private void OnRopeHit(Vector2 dir) => AddNow(dir, hitStrength, hitDuration);

        // ---- New tier-calibrated handlers ----

        // Plays through the death hitstop (the Drive loop runs on unscaled time there): the rumble
        // lands inside the death pause — the Celeste beat. The light bar turns red for the same beat.
        private void OnDied(DeathContext ctx)
        {
            playerDead = true;
            AddNow(deathStrength, deathStrength, deathDuration);
        }

        private void OnCheckpointSet(Vector2 pos) => AddNow(checkpointStrength, checkpointStrength, checkpointDuration);

        private void OnRespawned(GameObject victim, Vector2 pos)
        {
            playerDead = false;
            AddNow(respawnStrength, respawnStrength, respawnDuration);
        }

        private void OnDashAttempted(Vector2 pos, bool succeeded)
        {
            if (succeeded) AddNow(dashStrength, dashStrength, dashDuration);
            else AddNow(dashFailStrength, dashFailStrength, dashFailDuration);
        }

        private void OnTicked(Vector2 pos, int step, int total) => AddNow(tickStrength, tickStrength, tickDuration);

        private void OnBroken(Vector2 pos) => AddNow(brokenStrength, brokenStrength, brokenDuration);

        private void OnItemStored(InventoryItemDefinition item) => AddNow(storeStrength, storeStrength, storeDuration);

        private void OnItemReleased(InventoryItemDefinition item, Vector2 pos, Vector2 velocity) =>
            AddNow(releaseStrength, releaseStrength, releaseDuration);

        private void OnCapacityUpgraded(Vector2 pos, int increase) => AddNow(upgradeStrength, upgradeStrength, upgradeDuration);

        private void OnAbilityUnlocked(Vector2 pos, string abilityId) => AddNow(upgradeStrength, upgradeStrength, upgradeDuration);

        private void OnUiClicked() => AddNow(uiStrength, uiStrength, uiClickDuration);

        private void OnUiToggled(bool on) => AddNow(uiStrength, uiStrength, uiToggleDuration);

        // Immediate rumble with explicit per-motor strengths (directionless events split evenly).
        // The player's RumbleLevel tier scales every entry at the door.
        private void AddNow(float low, float high, float duration)
        {
            if (!RumbleActive || ActivePad == null || duration <= 0f) return;

            float scale = SettingsStore.RumbleScale;
            active.Add(new ActiveRumble { remainingDuration = duration, low = low * scale, high = high * scale });
            EnsureLoop();
        }

        // Immediate rumble from a direction: split the strength by the horizontal component
        private void AddNow(Vector2 dir, float strength, float duration)
        {
            if (!RumbleActive || ActivePad == null || strength <= 0f || duration <= 0f) return;
            float x = dir.sqrMagnitude > 0.0001f ? dir.normalized.x : 0f;
            AddNow(strength * (0.5f - 0.5f * x), strength * (0.5f + 0.5f * x), duration);
        }

        // Delayed rumble: the shockwave's left/right trigger ordering
        private void AddAfter(float delay, float low, float high, float duration)
        {
            if (!RumbleActive || ActivePad == null || duration <= 0f) return;

            float scale = SettingsStore.RumbleScale;
            scheduled.Add(new ScheduledRumble
            {
                remainingDelay = Mathf.Max(0f, delay), low = low * scale, high = high * scale, duration = duration
            });
            EnsureLoop();
        }

        private void EnsureLoop()
        {
            if (driveLoop != null) return;
            driveLoop = StartCoroutine(Drive());
        }

        private IEnumerator Drive()
        {
            while (scheduled.Count > 0 || active.Count > 0)
            {
                // Only a true world freeze (map view, tutorial) silences the pad: timers park at
                // dt = 0 and the motors zero. Hitstop and the user pause keep running on unscaled
                // time — the death rumble plays through its freeze (the Celeste beat) and menu
                // clicks buzz at once instead of piling up into one burst on resume.
                bool worldFrozen = GameTimeController.Instance != null && GameTimeController.Instance.IsWorldFrozen;
                float dt = worldFrozen ? 0f : Time.unscaledDeltaTime;

                // Countdown fields preserve the exact remaining delay/duration while frozen.
                for (int i = scheduled.Count - 1; i >= 0; i--)
                {
                    ScheduledRumble pending = scheduled[i];
                    pending.remainingDelay -= dt;
                    if (pending.remainingDelay <= 0f)
                    {
                        active.Add(new ActiveRumble
                        {
                            remainingDuration = pending.duration,
                            low = pending.low,
                            high = pending.high,
                        });
                        scheduled.RemoveAt(i);
                    }
                    else scheduled[i] = pending;
                }

                for (int i = active.Count - 1; i >= 0; i--)
                {
                    ActiveRumble rumble = active[i];
                    rumble.remainingDuration -= dt;
                    if (rumble.remainingDuration <= 0f) active.RemoveAt(i);
                    else active[i] = rumble;
                }

                if (scheduled.Count == 0 && active.Count == 0)
                {
                    WriteMotors(0f, 0f);
                    driveLoop = null;
                    yield break;
                }

                if (worldFrozen)
                {
                    WriteMotors(0f, 0f);
                }
                else
                {
                    float low = 0f, high = 0f;
                    foreach (ActiveRumble r in active)
                    {
                        low = Mathf.Max(low, r.low);
                        high = Mathf.Max(high, r.high);
                    }
                    WriteMotors(low, high);
                }

                yield return null;
            }
        }

        // Player transform: the GameManager hosting this survives scene switches, so after a change
        // the old reference is a Unity fake-null and is re-looked-up on next use (same pattern as
        // PlayerBus.Player / AudioManager.Listener)
        private static Transform playerCache;

        private static Transform Player
        {
            get
            {
                if (playerCache == null)
                {
                    GameObject go = GameObject.FindGameObjectWithTag(Tags.Player);
                    playerCache = go != null ? go.transform : null;
                }
                return playerCache;
            }
        }
    }
}
