using System;
using Inkform.Input;
using Inkform.Settings;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Inkform.UI
{
    /// <summary>
    /// Settings sheet: left navigation (Sound / Graphics / Controls) switches the right-hand content
    /// area, with Reset Settings and Back at the bottom right. All three tabs are live.
    ///
    /// This panel is the only writer of SettingsStore: controls push values out on change and pull
    /// them back in on OnOpen / Reset, never both at once — a subscribe-to-own-edits loop would turn
    /// every drag into a feedback cycle.
    ///
    /// Tabs discovered by name: Btn_TabSound / Btn_TabGraphics / Btn_TabControls, each with a matching
    /// content pane Content_Sound / Content_Graphics / Content_Controls. Back is Btn_Back, reset is
    /// Btn_Reset. The selected tab's button goes inert — that is also the "you are here" highlight.
    ///
    /// Each content pane is a ScrollRect, so its controls sit two levels down under Viewport/Content
    /// (see SoundRoot / GraphicsRoot / ControlsRoot). Controls per tab:
    ///
    /// Sound: Tgl_Mute, sliders Sld_Master / Sld_Music / Sld_Sfx with percent labels Lbl_Master /
    /// Lbl_Music / Lbl_Sfx.
    ///
    /// Graphics: Btn_ResLeft / Lbl_Res / Btn_ResRight, Btn_FullLeft / Lbl_Full / Btn_FullRight,
    /// Btn_FpsLeft / Lbl_Fps / Btn_FpsRight, Sld_Fx (labelled Low..High, no numeric readout),
    /// Tgl_ShowFps.
    ///
    /// Controls: device picker Dpd_Device (its order must match the InputDevice enum), sensitivity
    /// sliders Sld_Mouse / Sld_Stick with readouts Lbl_Mouse / Lbl_Stick, Btn_Unstuck,
    /// Btn_ResetBindings, and the two rebind sub-panes Content_Kbm /
    /// Content_Gamepad holding one row per KbmRows / GamepadRows entry — Lbl_KeyK{n} / Btn_BindK{n}
    /// and Lbl_KeyG{n} / Btn_BindG{n}.
    /// </summary>
    public class SettingsPanel : BasePanel
    {
        // Content panes are ScrollRects: the controls live under the scrolling Content rect, not on
        // the pane itself. One constant per tab keeps the lookups below readable.
        private const string SoundRoot = "Content_Sound/Viewport/Content";
        private const string GraphicsRoot = "Content_Graphics/Viewport/Content";
        private const string ControlsRoot = "Content_Controls/Viewport/Content";

        // ---- KBM binding row table. UIBuilder generates Lbl_KeyK{n} / Btn_BindK{n} in the same order;
        // compositePart selects a WASD direction (see BindingTools). Aim is display-only — a mouse-delta
        // binding has nothing meaningful to remap to. ----
        private struct BindingRow
        {
            public string actionName;
            public string displayName;
            public string compositePart;
            public bool rebindable;

            public BindingRow(string actionName, string displayName, string compositePart, bool rebindable)
            {
                this.actionName = actionName;
                this.displayName = displayName;
                this.compositePart = compositePart;
                this.rebindable = rebindable;
            }
        }

        private static readonly BindingRow[] KbmRows =
        {
            new BindingRow("Move", "Move Up", "up", true),
            new BindingRow("Move", "Move Down", "down", true),
            new BindingRow("Move", "Move Left", "left", true),
            new BindingRow("Move", "Move Right", "right", true),
            new BindingRow("Jump", "Jump", null, true),
            new BindingRow("Dash", "Dash", null, true),
            new BindingRow("RopeFire", "Rope Fire", null, true),
            new BindingRow("SpitBomb", "Spit Bomb", null, true),
            new BindingRow("Aim", "Aim (Mouse)", null, false),
        };

        // Gamepad rows mirror the KBM table; Move / Aim are the sticks — nothing to remap, display only.
        private static readonly BindingRow[] GamepadRows =
        {
            new BindingRow("Move", "Move (Left Stick)", null, false),
            new BindingRow("Aim", "Aim (Right Stick)", null, false),
            new BindingRow("Jump", "Jump", null, true),
            new BindingRow("Dash", "Dash", null, true),
            new BindingRow("RopeFire", "Rope Fire", null, true),
            new BindingRow("SpitBomb", "Spit Bomb", null, true),
        };

        [SerializeField] private Button soundTab;
        [SerializeField] private Button graphicsTab;
        [SerializeField] private Button controlsTab;
        [SerializeField] private GameObject soundContent;
        [SerializeField] private GameObject graphicsContent;
        [SerializeField] private GameObject controlsContent;

        [SerializeField] private Toggle muteToggle;
        [SerializeField] private Slider masterSlider;
        [SerializeField] private Slider musicSlider;
        [SerializeField] private Slider sfxSlider;
        [SerializeField] private Text masterLabel;
        [SerializeField] private Text musicLabel;
        [SerializeField] private Text sfxLabel;

        [SerializeField] private Button resLeft;
        [SerializeField] private Button resRight;
        [SerializeField] private Text resLabel;
        [SerializeField] private Button fullLeft;
        [SerializeField] private Button fullRight;
        [SerializeField] private Text fullLabel;
        [SerializeField] private Button fpsLeft;
        [SerializeField] private Button fpsRight;
        [SerializeField] private Text fpsLabel;
        [SerializeField] private Toggle showFpsToggle;
        [SerializeField] private Slider fxSlider;
        [SerializeField] private Toggle perfToggle;
        [SerializeField] private Button perfLeft;
        [SerializeField] private Button perfRight;
        [SerializeField] private Text perfLabel;
        [SerializeField] private Button rumbleLeft;
        [SerializeField] private Button rumbleRight;
        [SerializeField] private Text rumbleLabel;

        [SerializeField] private Dropdown deviceDropdown;
        [SerializeField] private GameObject kbmContent;
        [SerializeField] private GameObject gamepadContent;
        [SerializeField] private Slider mouseSlider;
        [SerializeField] private Text mouseLabel;
        [SerializeField] private Slider stickSlider;
        [SerializeField] private Text stickLabel;
        [SerializeField] private Button unstuckButton;
        [SerializeField] private Button resetBindingsButton;
        [SerializeField] private Text[] kbmKeyLabels;
        [SerializeField] private Button[] kbmRebindButtons;
        [SerializeField] private Text[] gamepadKeyLabels;
        [SerializeField] private Button[] gamepadRebindButtons;

        [SerializeField] private Button resetButton;
        [SerializeField] private Button backButton;

        protected override void Awake()
        {
            base.Awake();
            if (soundTab == null) soundTab = FindButton("Btn_TabSound");
            if (graphicsTab == null) graphicsTab = FindButton("Btn_TabGraphics");
            if (controlsTab == null) controlsTab = FindButton("Btn_TabControls");
            if (soundContent == null) soundContent = FindChild("Content_Sound");
            if (graphicsContent == null) graphicsContent = FindChild("Content_Graphics");
            if (controlsContent == null) controlsContent = FindChild("Content_Controls");

            if (muteToggle == null) muteToggle = FindToggle($"{SoundRoot}/Tgl_Mute");
            if (masterSlider == null) masterSlider = FindSlider($"{SoundRoot}/Sld_Master");
            if (musicSlider == null) musicSlider = FindSlider($"{SoundRoot}/Sld_Music");
            if (sfxSlider == null) sfxSlider = FindSlider($"{SoundRoot}/Sld_Sfx");
            if (masterLabel == null) masterLabel = FindText($"{SoundRoot}/Lbl_Master");
            if (musicLabel == null) musicLabel = FindText($"{SoundRoot}/Lbl_Music");
            if (sfxLabel == null) sfxLabel = FindText($"{SoundRoot}/Lbl_Sfx");

            if (resetButton == null) resetButton = FindButton("Btn_Reset");
            if (backButton == null) backButton = FindButton("Btn_Back");

            if (resLeft == null) resLeft = FindButton($"{GraphicsRoot}/Btn_ResLeft");
            if (resRight == null) resRight = FindButton($"{GraphicsRoot}/Btn_ResRight");
            if (resLabel == null) resLabel = FindText($"{GraphicsRoot}/Lbl_Res");
            if (fullLeft == null) fullLeft = FindButton($"{GraphicsRoot}/Btn_FullLeft");
            if (fullRight == null) fullRight = FindButton($"{GraphicsRoot}/Btn_FullRight");
            if (fullLabel == null) fullLabel = FindText($"{GraphicsRoot}/Lbl_Full");
            if (fpsLeft == null) fpsLeft = FindButton($"{GraphicsRoot}/Btn_FpsLeft");
            if (fpsRight == null) fpsRight = FindButton($"{GraphicsRoot}/Btn_FpsRight");
            if (fpsLabel == null) fpsLabel = FindText($"{GraphicsRoot}/Lbl_Fps");
            if (showFpsToggle == null) showFpsToggle = FindToggle($"{GraphicsRoot}/Tgl_ShowFps");
            if (fxSlider == null) fxSlider = FindSlider($"{GraphicsRoot}/Sld_Fx");
            if (perfToggle == null) perfToggle = FindToggle($"{GraphicsRoot}/Tgl_Perf");
            if (perfLeft == null) perfLeft = FindButton($"{GraphicsRoot}/Btn_PerfLeft");
            if (perfRight == null) perfRight = FindButton($"{GraphicsRoot}/Btn_PerfRight");
            if (perfLabel == null) perfLabel = FindText($"{GraphicsRoot}/Lbl_Perf");
            if (rumbleLeft == null) rumbleLeft = FindButton($"{ControlsRoot}/Btn_RumbleLeft");
            if (rumbleRight == null) rumbleRight = FindButton($"{ControlsRoot}/Btn_RumbleRight");
            if (rumbleLabel == null) rumbleLabel = FindText($"{ControlsRoot}/Lbl_Rumble");

            if (deviceDropdown == null) deviceDropdown = FindDropdown($"{ControlsRoot}/Dpd_Device");
            if (unstuckButton == null) unstuckButton = FindButton($"{ControlsRoot}/Btn_Unstuck");
            if (resetBindingsButton == null) resetBindingsButton = FindButton($"{ControlsRoot}/Btn_ResetBindings");
            if (mouseSlider == null) mouseSlider = FindSlider($"{ControlsRoot}/Sld_Mouse");
            if (mouseLabel == null) mouseLabel = FindText($"{ControlsRoot}/Lbl_Mouse");
            if (stickSlider == null) stickSlider = FindSlider($"{ControlsRoot}/Sld_Stick");
            if (stickLabel == null) stickLabel = FindText($"{ControlsRoot}/Lbl_Stick");
            if (kbmContent == null) kbmContent = FindChild($"{ControlsRoot}/Content_Kbm");
            if (gamepadContent == null) gamepadContent = FindChild($"{ControlsRoot}/Content_Gamepad");

            kbmKeyLabels = new Text[KbmRows.Length];
            kbmRebindButtons = new Button[KbmRows.Length];
            for (int i = 0; i < KbmRows.Length; i++)
            {
                kbmKeyLabels[i] = FindText($"{ControlsRoot}/Content_Kbm/Lbl_KeyK{i}");
                kbmRebindButtons[i] = FindButton($"{ControlsRoot}/Content_Kbm/Btn_BindK{i}");
            }

            gamepadKeyLabels = new Text[GamepadRows.Length];
            gamepadRebindButtons = new Button[GamepadRows.Length];
            for (int i = 0; i < GamepadRows.Length; i++)
            {
                gamepadKeyLabels[i] = FindText($"{ControlsRoot}/Content_Gamepad/Lbl_KeyG{i}");
                gamepadRebindButtons[i] = FindButton($"{ControlsRoot}/Content_Gamepad/Btn_BindG{i}");
            }

            Bind(soundTab, "Btn_TabSound", () => ShowTab(soundContent));
            Bind(graphicsTab, "Btn_TabGraphics", () => ShowTab(graphicsContent));
            Bind(controlsTab, "Btn_TabControls", () => ShowTab(controlsContent));

            Bind(resetButton, "Btn_Reset", OnReset);
            Bind(backButton, "Btn_Back", OnBack);

            // Sliders push values out on change; they never listen back (Refresh pulls in the other
            // direction on OnOpen / Reset), so a drag cannot feed back into itself.
            if (masterSlider != null)
                masterSlider.onValueChanged.AddListener(v => { SettingsStore.SetMasterVolume(v); SetLabel(masterLabel, v); });
            if (musicSlider != null)
                musicSlider.onValueChanged.AddListener(v => { SettingsStore.SetMusicVolume(v); SetLabel(musicLabel, v); });
            if (sfxSlider != null)
                sfxSlider.onValueChanged.AddListener(v => { SettingsStore.SetSfxVolume(v); SetLabel(sfxLabel, v); });

            if (muteToggle != null)
                muteToggle.onValueChanged.AddListener(v => SettingsStore.SetMuted(v));

            // FX has no numeric readout — the slider is labelled Low..High at its ends instead
            if (fxSlider != null)
                fxSlider.onValueChanged.AddListener(v => SettingsStore.SetFxIntensity(v));

            Bind(resLeft, "Btn_ResLeft", () => StepResolution(-1));
            Bind(resRight, "Btn_ResRight", () => StepResolution(+1));
            Bind(fpsLeft, "Btn_FpsLeft", () => StepFps(-1));
            Bind(fpsRight, "Btn_FpsRight", () => StepFps(+1));
            Bind(fullLeft, "Btn_FullLeft", ToggleFullscreen);
            Bind(fullRight, "Btn_FullRight", ToggleFullscreen);

            if (showFpsToggle != null)
                showFpsToggle.onValueChanged.AddListener(v => SettingsStore.SetShowFps(v));

            Bind(perfLeft, "Btn_PerfLeft", () => StepPerfRate(-1));
            Bind(perfRight, "Btn_PerfRight", () => StepPerfRate(+1));
            if (perfToggle != null)
                perfToggle.onValueChanged.AddListener(v => SettingsStore.SetPerfRecording(v));

            Bind(rumbleLeft, "Btn_RumbleLeft", () => StepRumble(-1));
            Bind(rumbleRight, "Btn_RumbleRight", () => StepRumble(+1));

            // Dropdown option order must match the InputDevice enum — UIBuilder fills it in that order
            if (deviceDropdown != null)
                deviceDropdown.onValueChanged.AddListener(i => SelectDevice((SettingsStore.InputDevice)i));

            Bind(unstuckButton, "Btn_Unstuck", OnUnstuck);
            Bind(resetBindingsButton, "Btn_ResetBindings", OnResetBindings);

            if (mouseSlider != null)
                mouseSlider.onValueChanged.AddListener(v => { SettingsStore.SetMouseSensitivity(v); SetSensLabel(mouseLabel, v); });
            if (stickSlider != null)
                stickSlider.onValueChanged.AddListener(v => { SettingsStore.SetStickSensitivity(v); SetSensLabel(stickLabel, v); });

            for (int i = 0; i < KbmRows.Length; i++)
                WireBindButton(kbmRebindButtons, i, KbmRows, kbmKeyLabels, BindingTools.KbmGroup, BindingTools.GamepadGroup);
            for (int i = 0; i < GamepadRows.Length; i++)
                WireBindButton(gamepadRebindButtons, i, GamepadRows, gamepadKeyLabels, BindingTools.GamepadGroup, BindingTools.KbmGroup);
        }

        protected override void OnOpen()
        {
            base.OnOpen();
            ShowTab(soundContent);      // settings always opens on the Sound tab
            Refresh();
        }

        protected override void OnClose()
        {
            base.OnClose();
            // A half-finished interactive rebind must not keep swallowing input after the panel closes
            BindingTools.CancelActive();
        }

        /// <summary>Pull SettingsStore's current values into every live control (OnOpen, after Reset).</summary>
        private void Refresh()
        {
            if (muteToggle != null) muteToggle.SetIsOnWithoutNotify(SettingsStore.Muted);
            SetSlider(masterSlider, SettingsStore.MasterVolume);
            SetSlider(musicSlider, SettingsStore.MusicVolume);
            SetSlider(sfxSlider, SettingsStore.SfxVolume);
            SetLabel(masterLabel, SettingsStore.MasterVolume);
            SetLabel(musicLabel, SettingsStore.MusicVolume);
            SetLabel(sfxLabel, SettingsStore.SfxVolume);

            SetLabel(resLabel, FormatRes(SettingsStore.ResolutionWidth, SettingsStore.ResolutionHeight));
            SetLabel(fullLabel, OnOffText(SettingsStore.Fullscreen));
            SetLabel(fpsLabel, FpsText(SettingsStore.FpsCap));
            if (showFpsToggle != null) showFpsToggle.SetIsOnWithoutNotify(SettingsStore.ShowFps);
            if (perfToggle != null) perfToggle.SetIsOnWithoutNotify(SettingsStore.PerfRecording);
            SetLabel(perfLabel, RateText(SettingsStore.PerfInterval));
            SetLabel(rumbleLabel, RumbleText(SettingsStore.Rumble));
            SetSlider(fxSlider, SettingsStore.FxIntensity);

            SetSlider(mouseSlider, SettingsStore.MouseSensitivity);
            SetSensLabel(mouseLabel, SettingsStore.MouseSensitivity);
            SetSlider(stickSlider, SettingsStore.StickSensitivity);
            SetSensLabel(stickLabel, SettingsStore.StickSensitivity);

            // Nothing to un-stick in the menu scene — there is no player there
            if (unstuckButton != null) unstuckButton.interactable = !UIManager.Instance.IsInMainMenu;

            if (deviceDropdown != null)
            {
                deviceDropdown.SetValueWithoutNotify((int)SettingsStore.Device);
                deviceDropdown.RefreshShownValue();
            }
            ShowDeviceContent();
            for (int i = 0; i < KbmRows.Length; i++)
                RefreshBindingRow(i, KbmRows, kbmKeyLabels, BindingTools.KbmGroup);
            for (int i = 0; i < GamepadRows.Length; i++)
                RefreshBindingRow(i, GamepadRows, gamepadKeyLabels, BindingTools.GamepadGroup);
        }

        private void OnReset()
        {
            SettingsStore.ResetToDefaults();
            // ResetToDefaults already deletes the persisted binding JSON; the shared asset's in-memory
            // overrides must go too, or the rows would show custom keys while the save is empty.
            BindingTools.ResetAllBindings();
            Refresh();
        }

        private void OnResetBindings()
        {
            BindingTools.ResetAllBindings();
            Refresh();
        }

        private void OnUnstuck() => UIManager.Instance.Unstuck();

        /// <summary>Cycles the machine's supported resolutions; the stored size is matched by value
        /// (not index — the list differs between machines, an index would be meaningless).</summary>
        private void StepResolution(int dir)
        {
            var list = SettingsStore.AvailableResolutions;
            int i = IndexOfResolution(list, SettingsStore.ResolutionWidth, SettingsStore.ResolutionHeight);
            i = (i + dir + list.Count) % list.Count;

            var r = list[i];
            SettingsStore.SetResolution(r.width, r.height);
            SetLabel(resLabel, FormatRes(r.width, r.height));
        }

        /// <summary>Cycles the FpsOptions list; the stored cap is matched by value for the same reason.</summary>
        private void StepFps(int dir)
        {
            int[] opts = SettingsStore.FpsOptions;
            int i = Array.IndexOf(opts, SettingsStore.FpsCap);
            if (i < 0) i = 1;   // stored value is not one of the options (e.g. hand-edited): land on 60

            i = (i + dir + opts.Length) % opts.Length;
            SettingsStore.SetFpsCap(opts[i]);
            SetLabel(fpsLabel, FpsText(opts[i]));
        }

        /// <summary>Cycles the PerfIntervals list (seconds); matched by value like StepFps.</summary>
        private void StepPerfRate(int dir)
        {
            float[] opts = SettingsStore.PerfIntervals;
            int i = Array.IndexOf(opts, SettingsStore.PerfInterval);
            if (i < 0) i = 2;   // stored value is not one of the options: land on 1 Hz

            i = (i + dir + opts.Length) % opts.Length;
            SettingsStore.SetPerfInterval(opts[i]);
            SetLabel(perfLabel, RateText(opts[i]));
        }

        // Two-state rows keep the same ◀ value ▶ shape as the cycling ones, so both arrows just flip
        // the value — stepping backwards and forwards through two options is the same move.
        private void StepRumble(int dir)
        {
            SettingsStore.SetRumble(!SettingsStore.Rumble);
            SetLabel(rumbleLabel, RumbleText(SettingsStore.Rumble));
        }

        private static string RumbleText(bool on) => on ? "On" : "Off";

        /// <summary>Intervals are stored in seconds but shown as their reciprocal in Hz
        /// ("10 Hz" .. "0.2 Hz"), matching how the numbers read on the panel.</summary>
        private static string RateText(float intervalSeconds)
        {
            float hz = 1f / Mathf.Max(intervalSeconds, 0.0001f);
            return $"{hz:0.#} Hz";
        }

        // Two-state rows keep the same ◀ value ▶ shape as the cycling ones, so both arrows just flip
        // the value — stepping backwards and forwards through two options is the same move.
        private void ToggleFullscreen()
        {
            SettingsStore.SetFullscreen(!SettingsStore.Fullscreen);
            SetLabel(fullLabel, OnOffText(SettingsStore.Fullscreen));
        }

        private static int IndexOfResolution(System.Collections.Generic.IReadOnlyList<Resolution> list, int width, int height)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].width == width && list[i].height == height) return i;
            }
            return 0;
        }

        // ---- Controls: device switch + KBM rows ----

        private void SelectDevice(SettingsStore.InputDevice device)
        {
            SettingsStore.SetDevice(device);
            ShowDeviceContent();
        }

        private void ShowDeviceContent()
        {
            bool kbm = SettingsStore.Device == SettingsStore.InputDevice.KeyboardMouse;
            if (kbmContent != null) kbmContent.SetActive(kbm);
            if (gamepadContent != null) gamepadContent.SetActive(!kbm);
        }

        /// <summary>Wires one rebind button: non-rebindable rows (Aim / sticks) go inert, the rest
        /// start an interactive rebind for their device group, excluding the opposite family.</summary>
        private void WireBindButton(Button[] buttons, int row, BindingRow[] rows, Text[] keyLabels,
            string group, string excludeGroup)
        {
            if (buttons[row] == null) return;
            if (!rows[row].rebindable)
            {
                buttons[row].interactable = false;
                return;
            }
            buttons[row].onClick.AddListener(() => OnRebindPressed(row, rows, keyLabels, group, excludeGroup));
        }

        private void OnRebindPressed(int row, BindingRow[] rows, Text[] keyLabels, string group, string excludeGroup)
        {
            BindingRow cfg = rows[row];
            InputAction action = GetAction(cfg.actionName);
            if (action == null) return;

            int index = BindingTools.FindBindingIndex(action, group, cfg.compositePart);
            if (index < 0) return;

            BindingTools.StartRebind(action, index, excludeGroup, () => RefreshBindingRow(row, rows, keyLabels, group));
        }

        private void RefreshBindingRow(int row, BindingRow[] rows, Text[] keyLabels, string group)
        {
            if (keyLabels[row] == null) return;

            BindingRow cfg = rows[row];
            InputAction action = GetAction(cfg.actionName);
            if (action == null) { keyLabels[row].text = "—"; return; }

            int index = BindingTools.FindBindingIndex(action, group, cfg.compositePart);
            keyLabels[row].text = index >= 0 ? BindingTools.GetDisplay(action, index) : "—";
        }

        private static InputAction GetAction(string actionName)
        {
            var map = InputActions.Wrapper.Player.Get();
            return map != null ? map.FindAction(actionName, false) : null;
        }

        // ---- Tabs ----

        // Null-tolerant: a missing content pane just means that tab shows nothing, rather than an NRE
        // that would take the whole sheet down (this runs from OnOpen, i.e. every time it is shown)
        private void ShowTab(GameObject active)
        {
            SetTab(soundContent, soundTab, active);
            SetTab(graphicsContent, graphicsTab, active);
            SetTab(controlsContent, controlsTab, active);
        }

        /// <summary>
        /// Shows or hides one tab's pane and puts its nav button in the matching state. The selected
        /// tab's button goes inert: that is the "you are here" highlight (Unity greys a disabled
        /// button) as well as a guard against re-clicking the tab you are already on — the same
        /// convention the save menu's placeholder arrows use.
        /// </summary>
        private static void SetTab(GameObject content, Button tab, GameObject active)
        {
            bool selected = content != null && content == active;

            if (content != null)
            {
                content.SetActive(selected);

                // Reopening a tab starts at the top rather than wherever it was last left scrolled
                if (selected)
                {
                    ScrollRect scroll = content.GetComponent<ScrollRect>();
                    if (scroll != null) scroll.verticalNormalizedPosition = 1f;
                }
            }

            if (tab != null) tab.interactable = !selected;
        }

        private void OnBack() => UIManager.Instance.CloseSettings();

        // ---- Control helpers ----

        /// <summary>Finds a Dropdown by relative path; missing returns null. Same shape as BasePanel's
        /// own finders — it has no Dropdown overload because this is the only panel that uses one.</summary>
        private Dropdown FindDropdown(string path)
        {
            Transform t = transform.Find(path);
            return t != null ? t.GetComponent<Dropdown>() : null;
        }

        // SetValueWithoutNotify: Refresh is a pull, firing onValueChanged here would push the same
        // value back into SettingsStore and rewrite PlayerPrefs for nothing
        private static void SetSlider(Slider slider, float value)
        {
            if (slider != null) slider.SetValueWithoutNotify(value);
        }

        private static void SetLabel(Text label, float value)
        {
            if (label != null) label.text = Percent(value);
        }

        private static void SetLabel(Text label, string text)
        {
            if (label != null) label.text = text;
        }

        /// <summary>Sensitivity readout. A plain multiplier ("2.5"), not a percentage — the range runs
        /// to 5x and "500%" reads as a much bigger number than it is.</summary>
        private static void SetSensLabel(Text label, float value)
        {
            if (label != null) label.text = value.ToString("0.0");
        }

        private static string Percent(float value) => $"{Mathf.RoundToInt(value * 100f)}%";

        private static string FormatRes(int width, int height) => $"{width} x {height}";

        private static string FpsText(int value) => value == 0 ? "Uncapped" : value.ToString();

        private static string OnOffText(bool value) => value ? "ON" : "OFF";
    }
}
