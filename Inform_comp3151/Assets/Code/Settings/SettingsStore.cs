using System;
using System.Collections.Generic;
using UnityEngine;

namespace Inkform.Settings
{
    /// <summary>
    /// Game settings: the single source of truth for player preferences (volumes, sensitivities,
    /// graphics options), persisted in PlayerPrefs.
    ///
    /// Fully static — no scene object, no prefab wiring: values load automatically before the first
    /// scene loads (RuntimeInitializeOnLoadMethod) and every Set saves back. Readers use the static
    /// properties directly: AudioManager reads volumes when a sound starts and subscribes to Changed
    /// to retune its live voices, RopeGun applies sensitivity and subscribes to Changed to re-read
    /// after edits. The Settings panel is the only writer.
    ///
    /// Static (over a GameManager component) because settings are cross-domain infrastructure like the
    /// buses — Audio / Player / UI all touch them — and a static API needs zero scene or prefab edits.
    /// Same lifecycle pattern as the buses: ResetStatics handles Domain Reload off, the
    /// RuntimeInitializeOnLoadMethod loads persisted values (a plain static default would be wiped by
    /// the ResetStatics pass that must run for the event).
    /// </summary>
    public static class SettingsStore
    {
        /// <summary>Which input device family gameplay input is filtered to (InputHandler).</summary>
        public enum InputDevice { KeyboardMouse, Gamepad }

        /// <summary>Gamepad rumble amount. Half scales every rumble's strength by 0.5, Off silences
        /// them — Celeste's three-step setting, and an accessibility staple.</summary>
        public enum RumbleAmount { Full, Half, Off }

        // Sensitivity is a plain multiplier shown as 0%~500% in the UI; 1.0 (=100%) means no change.
        public const float MinSensitivity = 0f;
        public const float MaxSensitivity = 5f;

        // ---- Values (PlayerPrefs-backed) ----

        public static float MasterVolume { get; private set; } = 1f;
        public static float MusicVolume { get; private set; } = 1f;
        public static float SfxVolume { get; private set; } = 1f;
        public static float MouseSensitivity { get; private set; } = 1f;
        public static float StickSensitivity { get; private set; } = 1f;

        /// <summary>Silences everything without touching the three volume values, so unmuting restores
        /// the mix the player set. Applied through AudioListener.volume — see ApplyAudio.</summary>
        public static bool Muted { get; private set; }

        /// <summary>Input family gameplay input is filtered to (InputHandler).</summary>
        public static InputDevice Device { get; private set; } = InputDevice.KeyboardMouse;

        public static int ResolutionWidth { get; private set; }
        public static int ResolutionHeight { get; private set; }
        public static bool Fullscreen { get; private set; } = true;
        public static int FpsCap { get; private set; } = 120;     // 0 = uncapped
        public static bool VSync { get; private set; }

        /// <summary>Frame rate counter overlay visible in gameplay.</summary>
        public static bool ShowFps { get; private set; }

        /// <summary>Session performance recorder on/off — writes the Docs/PerfLogs (or Log/) CSV
        /// traces. On by default: it is a development tool and silence would be a behavior change.</summary>
        public static bool PerfRecording { get; private set; } = true;

        /// <summary>Performance recorder sampling interval in seconds (0.1 = 10 Hz). Values outside
        /// PerfIntervals never get in through the setter.</summary>
        public static float PerfInterval { get; private set; } = 1f;

        /// <summary>Gamepad rumble amount; RumbleManager reads this live.</summary>
        public static RumbleAmount Rumble { get; private set; } = RumbleAmount.Full;

        /// <summary>Global visual FX intensity 0~1: scales screen shake, camera zoom, post punch and
        /// shatter launch speed at their consumers. Hitstop is a duration, not a strength, so it is
        /// deliberately untouched — same for gamepad rumble, which is haptic, not visual.</summary>
        public static float FxIntensity { get; private set; } = 1f;

        /// <summary>Frame cap options offered by the Graphics tab; 0 = uncapped. The current pick
        /// lives in FpsCap; this list only feeds the UI's left/right stepping.</summary>
        public static readonly int[] FpsOptions = { 30, 60, 120, 0 };

        /// <summary>Performance recorder sampling intervals (seconds) the Graphics tab steps
        /// through; shown to the player as their reciprocal in Hz (10 Hz .. 0.2 Hz).</summary>
        public static readonly float[] PerfIntervals = { 0.1f, 0.5f, 1f, 2f, 5f };

        /// <summary>
        /// Raised after any setting changes and was applied. Subscribers re-read the properties they
        /// care about (RopeGun's sensitivities; AudioManager retunes every voice already playing —
        /// its Plays read the volumes once, at start).
        /// </summary>
        public static event Action Changed;

        /// <summary>
        /// Runtime binding overrides as InputSystem JSON, applied to the shared InputActions asset at
        /// startup. Deliberately reads/writes PlayerPrefs directly with no cached field: the applier
        /// (InputActions.LoadBindingOverrides) runs in BeforeSceneLoad whose order relative to this
        /// class's own Load is undefined — a cached field could read its default empty string when the
        /// applier queries it.
        /// </summary>
        public static string BindingOverridesJson
        {
            get => PlayerPrefs.GetString(KeyBindings, "");
            set
            {
                if (string.IsNullOrEmpty(value)) PlayerPrefs.DeleteKey(KeyBindings);
                else PlayerPrefs.SetString(KeyBindings, value);
            }
        }

        private const string KeyMaster = "Inkform.masterVolume";
        private const string KeyMusic = "Inkform.musicVolume";
        private const string KeySfx = "Inkform.sfxVolume";
        private const string KeyMuted = "Inkform.muted";
        private const string KeyMouseSens = "Inkform.mouseSensitivity";
        private const string KeyStickSens = "Inkform.stickSensitivity";
        private const string KeyDevice = "Inkform.inputDevice";
        private const string KeyBindings = "Inkform.bindingOverrides";
        private const string KeyResW = "Inkform.resolutionWidth";
        private const string KeyResH = "Inkform.resolutionHeight";
        private const string KeyFullscreen = "Inkform.fullscreen";
        private const string KeyFps = "Inkform.fpsCap";
        private const string KeyVSync = "Inkform.vsync";
        private const string KeyShowFps = "Inkform.showFps";
        private const string KeyFxIntensity = "Inkform.fxIntensity";
        private const string KeyPerfRecording = "Inkform.perfRecording";
        private const string KeyPerfInterval = "Inkform.perfInterval";
        private const string KeyRumble = "Inkform.rumble";

        // ---- Lifecycle ----

        // Static fields do not clear on scene reload; with Domain Reload off, dead subscribers from
        // the previous run linger (same reason as the buses' ResetStatics)
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Changed = null;
            MasterVolume = MusicVolume = SfxVolume = 1f;
            Muted = false;
            MouseSensitivity = StickSensitivity = 1f;
            Device = InputDevice.KeyboardMouse;
            ResolutionWidth = ResolutionHeight = 0;
            Fullscreen = true;
            FpsCap = 60;
            VSync = false;
            ShowFps = false;
            PerfRecording = true;
            PerfInterval = 1f;
            Rumble = RumbleAmount.Full;
            FxIntensity = 1f;
            cachedResolutions = null;
        }

        // Load + apply before the first scene: PlayerPrefs values overwrite the defaults above, and
        // graphics options must be in effect before any camera renders. Idempotent — re-runs each play
        // mode entry, which is exactly what "persisted settings win over field initializers" needs.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void LoadAndApply()
        {
            Load();
            ApplyGraphics();
            ApplyAudio();
        }

        private static void Load()
        {
            MasterVolume = PlayerPrefs.GetFloat(KeyMaster, 1f);
            MusicVolume = PlayerPrefs.GetFloat(KeyMusic, 1f);
            SfxVolume = PlayerPrefs.GetFloat(KeySfx, 1f);
            Muted = PlayerPrefs.GetInt(KeyMuted, 0) != 0;
            // Remove the preference shipped for a minimap that was never implemented.
            PlayerPrefs.DeleteKey("Inkform.mapEnabled");
            MouseSensitivity = PlayerPrefs.GetFloat(KeyMouseSens, 1f);
            StickSensitivity = PlayerPrefs.GetFloat(KeyStickSens, 1f);
            Device = (InputDevice)PlayerPrefs.GetInt(KeyDevice, (int)InputDevice.KeyboardMouse);
            Fullscreen = PlayerPrefs.GetInt(KeyFullscreen, 1) != 0;
            FpsCap = PlayerPrefs.GetInt(KeyFps, 120);
            VSync = PlayerPrefs.GetInt(KeyVSync, 0) != 0;
            ShowFps = PlayerPrefs.GetInt(KeyShowFps, 0) != 0;
            PerfRecording = PlayerPrefs.GetInt(KeyPerfRecording, 1) != 0;
            PerfInterval = PlayerPrefs.GetFloat(KeyPerfInterval, 1f);
            Rumble = (RumbleAmount)PlayerPrefs.GetInt(KeyRumble, (int)RumbleAmount.Full);
            FxIntensity = PlayerPrefs.GetFloat(KeyFxIntensity, 1f);

            ResolutionWidth = PlayerPrefs.GetInt(KeyResW, 0);
            ResolutionHeight = PlayerPrefs.GetInt(KeyResH, 0);
            if (!IsResolutionSupported(ResolutionWidth, ResolutionHeight))
            {
                // Stored size is not offered by this machine (monitor changed): fall back to current
                Resolution r = Screen.currentResolution;
                ResolutionWidth = r.width;
                ResolutionHeight = r.height;
            }
        }

        // ---- Setters (panel is the only writer) ----

        // Each Set: guard on change (skip the PlayerPrefs write and the Changed storm when the slider
        // value did not actually move), clamp to the legal range, persist, then notify.
        public static void SetMasterVolume(float value)
        {
            value = Mathf.Clamp01(value);
            if (Mathf.Approximately(MasterVolume, value)) return;
            MasterVolume = value;
            PlayerPrefs.SetFloat(KeyMaster, value);
            Changed?.Invoke();
        }

        /// <summary>Music track volume. Applied by MusicPlayer, which recomputes running music
        /// volume every frame — slider drags land on the next frame, no restart needed.</summary>
        public static void SetMusicVolume(float value)
        {
            value = Mathf.Clamp01(value);
            if (Mathf.Approximately(MusicVolume, value)) return;
            MusicVolume = value;
            PlayerPrefs.SetFloat(KeyMusic, value);
            Changed?.Invoke();
        }

        public static void SetSfxVolume(float value)
        {
            value = Mathf.Clamp01(value);
            if (Mathf.Approximately(SfxVolume, value)) return;
            SfxVolume = value;
            PlayerPrefs.SetFloat(KeySfx, value);
            Changed?.Invoke();
        }

        public static void SetMuted(bool value)
        {
            if (Muted == value) return;
            Muted = value;
            PlayerPrefs.SetInt(KeyMuted, value ? 1 : 0);
            ApplyAudio();
            Changed?.Invoke();
        }

        public static void SetMouseSensitivity(float value)
        {
            value = Mathf.Clamp(value, MinSensitivity, MaxSensitivity);
            if (Mathf.Approximately(MouseSensitivity, value)) return;
            MouseSensitivity = value;
            PlayerPrefs.SetFloat(KeyMouseSens, value);
            Changed?.Invoke();
        }

        public static void SetStickSensitivity(float value)
        {
            value = Mathf.Clamp(value, MinSensitivity, MaxSensitivity);
            if (Mathf.Approximately(StickSensitivity, value)) return;
            StickSensitivity = value;
            PlayerPrefs.SetFloat(KeyStickSens, value);
            Changed?.Invoke();
        }

        public static void SetDevice(InputDevice value)
        {
            if (Device == value) return;
            Device = value;
            PlayerPrefs.SetInt(KeyDevice, (int)value);
            Changed?.Invoke();
        }

        public static void SetResolution(int width, int height)
        {
            if (!IsResolutionSupported(width, height)) return;
            if (ResolutionWidth == width && ResolutionHeight == height) return;
            ResolutionWidth = width;
            ResolutionHeight = height;
            PlayerPrefs.SetInt(KeyResW, width);
            PlayerPrefs.SetInt(KeyResH, height);
            ApplyGraphics();
            Changed?.Invoke();
        }

        public static void SetFullscreen(bool value)
        {
            if (Fullscreen == value) return;
            Fullscreen = value;
            PlayerPrefs.SetInt(KeyFullscreen, value ? 1 : 0);
            ApplyGraphics();
            Changed?.Invoke();
        }

        /// <summary>Frame cap in fps; 0 = uncapped. Ignored while VSync is on (Unity's own rule).</summary>
        public static void SetFpsCap(int value)
        {
            if (value < 0) value = 0;
            if (FpsCap == value) return;
            FpsCap = value;
            PlayerPrefs.SetInt(KeyFps, value);
            ApplyGraphics();
            Changed?.Invoke();
        }

        public static void SetVSync(bool value)
        {
            if (VSync == value) return;
            VSync = value;
            PlayerPrefs.SetInt(KeyVSync, value ? 1 : 0);
            ApplyGraphics();
            Changed?.Invoke();
        }

        public static void SetShowFps(bool value)
        {
            if (ShowFps == value) return;
            ShowFps = value;
            PlayerPrefs.SetInt(KeyShowFps, value ? 1 : 0);
            Changed?.Invoke();
        }

        public static void SetFxIntensity(float value)
        {
            value = Mathf.Clamp01(value);
            if (Mathf.Approximately(FxIntensity, value)) return;
            FxIntensity = value;
            PlayerPrefs.SetFloat(KeyFxIntensity, value);
            Changed?.Invoke();
        }

        public static void SetPerfRecording(bool value)
        {
            if (PerfRecording == value) return;
            PerfRecording = value;
            PlayerPrefs.SetInt(KeyPerfRecording, value ? 1 : 0);
            Changed?.Invoke();
        }

        /// <summary>Performance recorder sampling interval in seconds. Only the PerfIntervals
        /// values are accepted — the Graphics stepper cycles exactly those, and an off-list value
        /// would desync its display.</summary>
        public static void SetPerfInterval(float value)
        {
            if (System.Array.IndexOf(PerfIntervals, value) < 0) return;
            if (Mathf.Approximately(PerfInterval, value)) return;
            PerfInterval = value;
            PlayerPrefs.SetFloat(KeyPerfInterval, value);
            Changed?.Invoke();
        }

        public static void SetRumble(RumbleAmount value)
        {
            if (Rumble == value) return;
            Rumble = value;
            PlayerPrefs.SetInt(KeyRumble, (int)value);
            Changed?.Invoke();
        }

        public static void ResetToDefaults()
        {
            MasterVolume = MusicVolume = SfxVolume = 1f;
            Muted = false;
            MouseSensitivity = StickSensitivity = 1f;
            Device = InputDevice.KeyboardMouse;
            Fullscreen = true;
            FpsCap = 120;
            VSync = false;
            ShowFps = false;
            PerfRecording = true;
            PerfInterval = 1f;
            Rumble = RumbleAmount.Full;
            FxIntensity = 1f;
            Resolution r = Screen.currentResolution;
            ResolutionWidth = r.width;
            ResolutionHeight = r.height;

            // Per-key delete rather than PlayerPrefs.DeleteAll: other systems may own keys one day,
            // and a settings reset must not blow them away
            PlayerPrefs.DeleteKey(KeyMaster);
            PlayerPrefs.DeleteKey(KeyMusic);
            PlayerPrefs.DeleteKey(KeySfx);
            PlayerPrefs.DeleteKey(KeyMuted);
            PlayerPrefs.DeleteKey("Inkform.mapEnabled");
            PlayerPrefs.DeleteKey(KeyMouseSens);
            PlayerPrefs.DeleteKey(KeyStickSens);
            PlayerPrefs.DeleteKey(KeyDevice);
            PlayerPrefs.DeleteKey(KeyBindings);
            PlayerPrefs.DeleteKey(KeyResW);
            PlayerPrefs.DeleteKey(KeyResH);
            PlayerPrefs.DeleteKey(KeyFullscreen);
            PlayerPrefs.DeleteKey(KeyFps);
            PlayerPrefs.DeleteKey(KeyVSync);
            PlayerPrefs.DeleteKey(KeyShowFps);
            PlayerPrefs.DeleteKey(KeyFxIntensity);
            PlayerPrefs.DeleteKey(KeyPerfRecording);
            PlayerPrefs.DeleteKey(KeyPerfInterval);
            PlayerPrefs.DeleteKey(KeyRumble);

            ApplyGraphics();
            ApplyAudio();
            Changed?.Invoke();
        }

        // ---- Audio application ----

        /// <summary>
        /// Lands Muted on the audio engine. Goes through the global AudioListener.volume rather than
        /// the AudioManager's per-Play volume maths: Play reads the volume properties once, when a
        /// sound starts (AudioManager.Play), so a mute routed through them would leave every already-
        /// playing loop audible until it ended. The listener volume takes effect on the same frame for
        /// everything. Nothing else in the project touches this global.
        /// </summary>
        private static void ApplyAudio()
        {
            AudioListener.volume = Muted ? 0f : 1f;
        }

        // ---- Graphics application ----

        private static void ApplyGraphics()
        {
            if (ResolutionWidth > 0 && ResolutionHeight > 0)
            {
                // FullScreenWindow (borderless) rather than ExclusiveFullScreen: switching out of an
                // exclusive mode mid-editor play can hang the editor; borderless applies everywhere.
                Screen.SetResolution(ResolutionWidth, ResolutionHeight,
                    Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
            }

            // VSync wins over the frame cap: targetFrameRate must be -1 for vsync to take effect,
            // mirroring that keeps the two options from silently fighting
            QualitySettings.vSyncCount = VSync ? 1 : 0;
            Application.targetFrameRate = VSync ? -1 : FpsCap;
        }

        // ---- Resolution list for the Graphics tab ----

        private static List<Resolution> cachedResolutions;

        /// <summary>Resolutions this machine offers, deduplicated (Unity repeats them per refresh
        /// rate). Empty screen list falls back to the current resolution, so the UI always has ≥1.</summary>
        public static IReadOnlyList<Resolution> AvailableResolutions
        {
            get
            {
                if (cachedResolutions == null)
                {
                    cachedResolutions = new List<Resolution>();
                    var seen = new HashSet<(int width, int height)>();
                    foreach (Resolution r in Screen.resolutions)
                    {
                        if (seen.Add((r.width, r.height)))
                            cachedResolutions.Add(r);
                    }
                    if (cachedResolutions.Count == 0)
                        cachedResolutions.Add(Screen.currentResolution);
                }
                return cachedResolutions;
            }
        }

        private static bool IsResolutionSupported(int width, int height)
        {
            foreach (Resolution r in AvailableResolutions)
            {
                if (r.width == width && r.height == height) return true;
            }
            return false;
        }
    }
}
