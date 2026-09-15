using System;
using Inkform.Input;
using Inkform.Settings;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Inkform.UI
{
    /// <summary>
    /// Settings sheet, rebuilt as Celeste's OuiOptions: one scrolling page under a big header,
    /// grouped by SOUND / GRAPHICS / CONTROLS SubHeaders — the old three-tab layout is gone,
    /// every setting it carried is here.
    ///
    /// Two-state and cycling settings are OptionRows ("< value >", left/right steps while
    /// focused); volumes and sensitivities are Celeste-styled sliders with a live readout. The
    /// two rebind tables (keyboard / gamepad, switched by the Device row) port the old
    /// UIBuilder-generated rows.
    ///
    /// This panel is the only writer of SettingsStore: controls push values out on change and
    /// pull them back in on OnOpen / Reset, never both at once — a subscribe-to-own-edits loop
    /// would turn every drag into a feedback cycle.
    /// </summary>
    public class SettingsPanel : ToolkitPanel
    {
        // ---- Binding row table, straight from the old panel. compositePart selects a WASD
        // direction (see BindingTools). Aim is display-only — a mouse-delta binding has nothing
        // meaningful to remap to. ----
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

        private readonly ScrollView scroll;
        private readonly VisualElement rows;

        // Sound
        private OptionRow muteRow;
        private Slider masterSlider, musicSlider, sfxSlider;
        private Label masterLabel, musicLabel, sfxLabel;

        // Graphics
        private OptionRow resRow, fullRow, fpsRow, showFpsRow, perfRow, perfRateRow;
        private Slider fxSlider;

        // Controls
        private OptionRow deviceRow, rumbleRow, unstuckRow, resetBindingsRow;
        private Slider mouseSlider, stickSlider;
        private Label mouseLabel, stickLabel;
        private VisualElement kbmSection, padSection;
        private Button[] kbmBindButtons;
        private Button[] padBindButtons;
        private Label[] kbmKeyLabels, padKeyLabels;

        public SettingsPanel(VisualElement root, UIManager ui) : base(root, ui)
        {
            scroll = Q<ScrollView>("Scroll");
            rows = Q<VisualElement>("Rows");

            BuildSoundGroup();
            BuildGraphicsGroup();
            BuildControlsGroup();

            Bind(Q<Button>("Btn_Reset"), "Btn_Reset", OnReset);
            Bind(Q<Button>("Btn_Back"), "Btn_Back", OnBack);
        }

        // ---- Group builders ----

        private void BuildSoundGroup()
        {
            AddSubHeader("SOUND");

            muteRow = AddOptionRow("Mute", OnOffText(SettingsStore.Muted));
            muteRow.Stepped += dir =>
            {
                SettingsStore.SetMuted(dir > 0);
                muteRow.Value = OnOffText(dir > 0);
            };

            masterLabel = AddSliderRow("Main Volume", 0f, 1f, out masterSlider);
            musicLabel = AddSliderRow("Music Volume", 0f, 1f, out musicSlider);
            sfxLabel = AddSliderRow("SFX Volume", 0f, 1f, out sfxSlider);

            masterSlider.RegisterValueChangedCallback(e =>
            {
                SettingsStore.SetMasterVolume(e.newValue);
                masterLabel.text = Percent(e.newValue);
            });
            musicSlider.RegisterValueChangedCallback(e =>
            {
                SettingsStore.SetMusicVolume(e.newValue);
                musicLabel.text = Percent(e.newValue);
            });
            sfxSlider.RegisterValueChangedCallback(e =>
            {
                SettingsStore.SetSfxVolume(e.newValue);
                sfxLabel.text = Percent(e.newValue);
            });
        }

        private void BuildGraphicsGroup()
        {
            AddSubHeader("GRAPHICS");

            resRow = AddOptionRow("Resolution", FormatRes(SettingsStore.ResolutionWidth, SettingsStore.ResolutionHeight));
            resRow.Stepped += StepResolution;

            fullRow = AddOnOffRow("Fullscreen", SettingsStore.Fullscreen, SettingsStore.SetFullscreen);

            fpsRow = AddOptionRow("FPS Cap", FpsText(SettingsStore.FpsCap));
            fpsRow.Stepped += StepFps;

            AddSliderRow("FX Intensity", 0f, 1f, out fxSlider);
            fxSlider.RegisterValueChangedCallback(e => SettingsStore.SetFxIntensity(e.newValue));

            showFpsRow = AddOnOffRow("Show FPS", SettingsStore.ShowFps, SettingsStore.SetShowFps);

            perfRow = AddOnOffRow("Perf Recording", SettingsStore.PerfRecording, SettingsStore.SetPerfRecording);

            perfRateRow = AddOptionRow("Perf Rate", RateText(SettingsStore.PerfInterval));
            perfRateRow.Stepped += StepPerfRate;
        }

        private void BuildControlsGroup()
        {
            AddSubHeader("CONTROLS");

            deviceRow = AddOptionRow("Device", DeviceText(SettingsStore.Device));
            deviceRow.Stepped += dir =>
            {
                var values = (SettingsStore.InputDevice[])Enum.GetValues(typeof(SettingsStore.InputDevice));
                int i = Array.IndexOf(values, SettingsStore.Device);
                i = (i + dir + values.Length) % values.Length;
                SettingsStore.SetDevice(values[i]);
                deviceRow.Value = DeviceText(values[i]);
                ShowDeviceContent();
            };

            rumbleRow = AddOnOffRow("Rumble", SettingsStore.Rumble, SettingsStore.SetRumble);

            mouseLabel = AddSliderRow("Mouse Sensitivity", SettingsStore.MinSensitivity, SettingsStore.MaxSensitivity, out mouseSlider);
            stickLabel = AddSliderRow("Controller Sensitivity", SettingsStore.MinSensitivity, SettingsStore.MaxSensitivity, out stickSlider);
            mouseSlider.RegisterValueChangedCallback(e =>
            {
                SettingsStore.SetMouseSensitivity(e.newValue);
                mouseLabel.text = SensText(e.newValue);
            });
            stickSlider.RegisterValueChangedCallback(e =>
            {
                SettingsStore.SetStickSensitivity(e.newValue);
                stickLabel.text = SensText(e.newValue);
            });

            // The rebind tables switch with the device row, like the old Dpd_Device did.
            kbmSection = AddSection();
            padSection = AddSection();
            kbmKeyLabels = new Label[KbmRows.Length];
            padKeyLabels = new Label[GamepadRows.Length];
            kbmBindButtons = BuildRebindTable(kbmSection, KbmRows, kbmKeyLabels, BindingTools.KbmGroup, BindingTools.GamepadGroup);
            padBindButtons = BuildRebindTable(padSection, GamepadRows, padKeyLabels, BindingTools.GamepadGroup, BindingTools.KbmGroup);

            unstuckRow = AddActionRow("Unstuck");
            unstuckRow.Confirmed += () => UI.Unstuck();

            resetBindingsRow = AddActionRow("Reset Bindings");
            resetBindingsRow.Confirmed += OnResetBindings;
        }

        // ---- Row factories ----

        private void AddSubHeader(string text)
        {
            var header = new Label(text);
            header.AddToClassList("subheader");
            header.AddToClassList("outline");
            header.pickingMode = PickingMode.Ignore;
            rows.Add(header);
        }

        private OptionRow AddOptionRow(string labelText, string initial)
        {
            var row = new OptionRow(labelText) { Value = initial };
            rows.Add(row);
            return row;
        }

        /// <summary>A two-state row: both directions flip the value, so stepping backwards and
        /// forwards through two options is the same move (the old two-arrow rows' shape).</summary>
        private OptionRow AddOnOffRow(string labelText, bool initial, Action<bool> set)
        {
            var row = AddOptionRow(labelText, OnOffText(initial));
            row.Stepped += dir =>
            {
                bool on = dir > 0;
                set(on);
                row.Value = OnOffText(on);
            };
            return row;
        }

        /// <summary>A click-only row (no value, no chevrons): Unstuck, Reset Bindings.</summary>
        private OptionRow AddActionRow(string labelText)
        {
            var row = AddOptionRow(labelText, string.Empty);
            row.ShowArrows(false);
            return row;
        }

        private Label AddSliderRow(string labelText, float min, float max, out Slider slider)
        {
            var row = new VisualElement();
            row.AddToClassList("slider-row");

            var label = new Label(labelText);
            label.AddToClassList("row-label");
            label.AddToClassList("outline");
            label.pickingMode = PickingMode.Ignore;
            row.Add(label);

            slider = new Slider(min, max);
            slider.AddToClassList("settings-slider");

            var readout = new Label();
            readout.AddToClassList("row-readout");
            readout.AddToClassList("outline");
            readout.pickingMode = PickingMode.Ignore;
            row.Add(readout);
            row.Add(slider);

            // The row itself is not focusable — the slider is. Mirror the slider's focus onto
            // the row so the label gets the green highlight (:focus-within is not portable).
            slider.RegisterCallback<FocusInEvent>(_ => row.AddToClassList("focused"));
            slider.RegisterCallback<FocusOutEvent>(_ => row.RemoveFromClassList("focused"));
            slider.RegisterCallback<PointerEnterEvent>(_ => Inkform.Bus.UiBus.RaiseHovered());

            rows.Add(row);
            return readout;
        }

        private VisualElement AddSection()
        {
            var section = new VisualElement();
            rows.Add(section);
            return section;
        }

        /// <summary>Builds one rebind table: a plain row per action, whole row is the button.
        /// Non-rebindable rows (Aim / the sticks) go inert instead of hidden, so the table
        /// layout never reflows. Fills <paramref name="keyLabels"/> with each row's key readout.</summary>
        private Button[] BuildRebindTable(VisualElement section, BindingRow[] table, Label[] keyLabels,
            string group, string excludeGroup)
        {
            var buttons = new Button[table.Length];

            for (int i = 0; i < table.Length; i++)
            {
                int row = i;    // captured per iteration, same reason as everywhere else

                var button = new Button { text = string.Empty };
                button.AddToClassList("option-row");
                button.AddToClassList("floaty");

                var action = new Label(table[i].displayName);
                action.AddToClassList("row-label");
                action.AddToClassList("outline");
                action.pickingMode = PickingMode.Ignore;
                button.Add(action);

                var key = new Label();
                key.AddToClassList("row-value");
                key.AddToClassList("outline");
                key.pickingMode = PickingMode.Ignore;
                button.Add(key);

                section.Add(button);
                buttons[i] = button;
                keyLabels[i] = key;

                if (!table[i].rebindable)
                {
                    button.SetEnabled(false);
                    continue;
                }

                button.clicked += () =>
                {
                    Inkform.Bus.UiBus.RaiseClicked();
                    OnRebindPressed(row, table, key, group, excludeGroup);
                };
                button.RegisterCallback<PointerEnterEvent>(_ => Inkform.Bus.UiBus.RaiseHovered());
                button.RegisterCallback<FocusInEvent>(_ => Inkform.Bus.UiBus.RaiseHovered());
            }

            return buttons;
        }

        // ---- Lifecycle ----

        protected override void OnOpen()
        {
            Refresh();
            // Settings always opens at the top of the page (the old per-tab ScrollRect reset).
            if (scroll != null && scroll.verticalScroller != null)
                scroll.verticalScroller.value = 0f;
        }

        protected override void OnClose()
        {
            // A half-finished interactive rebind must not keep swallowing input after close
            BindingTools.CancelActive();
        }

        /// <summary>Pull SettingsStore's current values into every live control (OnOpen, after Reset).</summary>
        private void Refresh()
        {
            muteRow.Value = OnOffText(SettingsStore.Muted);
            SetSlider(masterSlider, SettingsStore.MasterVolume, masterLabel, Percent);
            SetSlider(musicSlider, SettingsStore.MusicVolume, musicLabel, Percent);
            SetSlider(sfxSlider, SettingsStore.SfxVolume, sfxLabel, Percent);

            resRow.Value = FormatRes(SettingsStore.ResolutionWidth, SettingsStore.ResolutionHeight);
            fullRow.Value = OnOffText(SettingsStore.Fullscreen);
            fpsRow.Value = FpsText(SettingsStore.FpsCap);
            SetSlider(fxSlider, SettingsStore.FxIntensity, null, null);
            showFpsRow.Value = OnOffText(SettingsStore.ShowFps);
            perfRow.Value = OnOffText(SettingsStore.PerfRecording);
            perfRateRow.Value = RateText(SettingsStore.PerfInterval);

            deviceRow.Value = DeviceText(SettingsStore.Device);
            rumbleRow.Value = OnOffText(SettingsStore.Rumble);
            SetSlider(mouseSlider, SettingsStore.MouseSensitivity, mouseLabel, SensText);
            SetSlider(stickSlider, SettingsStore.StickSensitivity, stickLabel, SensText);

            // Nothing to un-stick in the menu scene — there is no player there
            unstuckRow.SetEnabled(!UI.IsInMainMenu);

            ShowDeviceContent();
            for (int i = 0; i < KbmRows.Length; i++)
                RefreshBindingRow(i, KbmRows, kbmKeyLabels[i], BindingTools.KbmGroup);
            for (int i = 0; i < GamepadRows.Length; i++)
                RefreshBindingRow(i, GamepadRows, padKeyLabels[i], BindingTools.GamepadGroup);
        }

        private void ShowDeviceContent()
        {
            bool kbm = SettingsStore.Device == SettingsStore.InputDevice.KeyboardMouse;
            if (kbmSection != null) kbmSection.style.display = kbm ? DisplayStyle.Flex : DisplayStyle.None;
            if (padSection != null) padSection.style.display = kbm ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void OnReset()
        {
            SettingsStore.ResetToDefaults();
            // ResetToDefaults already deletes the persisted binding JSON; the shared asset's
            // in-memory overrides must go too, or the rows would show custom keys while the
            // save is empty.
            BindingTools.ResetAllBindings();
            Refresh();
        }

        private void OnResetBindings()
        {
            BindingTools.ResetAllBindings();
            Refresh();
        }

        private void OnBack() => UI.CloseSettings();

        // ---- Cycling rows ----

        /// <summary>Cycles the machine's supported resolutions; the stored size is matched by
        /// value (not index — the list differs between machines, an index would be meaningless).</summary>
        private void StepResolution(int dir)
        {
            var list = SettingsStore.AvailableResolutions;
            int i = IndexOfResolution(list, SettingsStore.ResolutionWidth, SettingsStore.ResolutionHeight);
            i = (i + dir + list.Count) % list.Count;

            var r = list[i];
            SettingsStore.SetResolution(r.width, r.height);
            resRow.Value = FormatRes(r.width, r.height);
        }

        /// <summary>Cycles the FpsOptions list; the stored cap is matched by value for the same reason.</summary>
        private void StepFps(int dir)
        {
            int[] opts = SettingsStore.FpsOptions;
            int i = Array.IndexOf(opts, SettingsStore.FpsCap);
            if (i < 0) i = 1;   // stored value is not one of the options (e.g. hand-edited): land on 60

            i = (i + dir + opts.Length) % opts.Length;
            SettingsStore.SetFpsCap(opts[i]);
            fpsRow.Value = FpsText(opts[i]);
        }

        /// <summary>Cycles the PerfIntervals list (seconds); matched by value like StepFps.</summary>
        private void StepPerfRate(int dir)
        {
            float[] opts = SettingsStore.PerfIntervals;
            int i = Array.IndexOf(opts, SettingsStore.PerfInterval);
            if (i < 0) i = 2;   // stored value is not one of the options: land on 1 Hz

            i = (i + dir + opts.Length) % opts.Length;
            SettingsStore.SetPerfInterval(opts[i]);
            perfRateRow.Value = RateText(opts[i]);
        }

        private static int IndexOfResolution(System.Collections.Generic.IReadOnlyList<Resolution> list, int width, int height)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].width == width && list[i].height == height) return i;
            }
            return 0;
        }

        // ---- Rebind rows ----

        private void OnRebindPressed(int row, BindingRow[] table, Label keyLabel, string group, string excludeGroup)
        {
            BindingRow cfg = table[row];
            InputAction action = GetAction(cfg.actionName);
            if (action == null) return;

            int index = BindingTools.FindBindingIndex(action, group, cfg.compositePart);
            if (index < 0) return;

            BindingTools.StartRebind(action, index, excludeGroup, () => RefreshBindingRow(row, table, keyLabel, group));
        }

        private void RefreshBindingRow(int row, BindingRow[] table, Label keyLabel, string group)
        {
            if (keyLabel == null) return;

            BindingRow cfg = table[row];
            InputAction action = GetAction(cfg.actionName);
            if (action == null) { keyLabel.text = "-"; return; }

            int index = BindingTools.FindBindingIndex(action, group, cfg.compositePart);
            keyLabel.text = index >= 0 ? BindingTools.GetDisplay(action, index) : "-";
        }

        private static InputAction GetAction(string actionName)
        {
            var map = InputActions.Wrapper.Player.Get();
            return map != null ? map.FindAction(actionName, false) : null;
        }

        // ---- Formatting helpers (ported verbatim) ----

        // SetValueWithoutNotify: Refresh is a pull, firing onValueChanged here would push the
        // same value back into SettingsStore and rewrite PlayerPrefs for nothing
        private static void SetSlider(Slider slider, float value, Label readout, Func<float, string> format)
        {
            if (slider == null) return;
            slider.SetValueWithoutNotify(value);
            if (readout != null && format != null) readout.text = format(value);
        }

        private static string Percent(float value) => $"{Mathf.RoundToInt(value * 100f)}%";

        /// <summary>Sensitivity readout. A plain multiplier ("2.5"), not a percentage — the
        /// range runs to 5x and "500%" reads as a much bigger number than it is.</summary>
        private static string SensText(float value) => value.ToString("0.0");

        private static string FormatRes(int width, int height) => $"{width} x {height}";

        private static string FpsText(int value) => value == 0 ? "Uncapped" : value.ToString();

        private static string OnOffText(bool value) => value ? "ON" : "OFF";

        private static string DeviceText(SettingsStore.InputDevice device) =>
            device == SettingsStore.InputDevice.KeyboardMouse ? "Keyboard + Mouse" : "Gamepad";

        /// <summary>Intervals are stored in seconds but shown as their reciprocal in Hz
        /// ("10 Hz" .. "0.2 Hz"), matching how the numbers read on the panel.</summary>
        private static string RateText(float intervalSeconds)
        {
            float hz = 1f / Mathf.Max(intervalSeconds, 0.0001f);
            return $"{hz:0.#} Hz";
        }
    }
}
