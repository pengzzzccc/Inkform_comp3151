using System;
using Inkform.Input;
using Inkform.Settings;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Inkform.UI
{
    /// <summary>
    /// The CONTROLS settings page: input device, rumble, the two sensitivities, the per-device
    /// rebind tables (switched by the Device row), Unstuck, Reset Bindings — and the GRAPPING
    /// HOOK section: the rope gun's snap options the RopeGun reads live from SettingsStore.
    /// </summary>
    public class SettingsControlsPage : SettingsSubPage
    {
        public override string Title => "CONTROLS";

        // compositePart selects a WASD direction (see BindingTools). Aim is display-only — a
        // mouse-delta binding has nothing meaningful to remap to.
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

        private OptionRow deviceRow, rumbleRow, unstuckRow, resetBindingsRow;
        private Slider mouseSlider, stickSlider;
        private Label mouseLabel, stickLabel;
        private VisualElement kbmSection, padSection;
        private Label[] kbmKeyLabels, padKeyLabels;

        private OptionRow ropeWallSnapRow, ropeBombSnapRow, ropeAdaptiveRow;
        private Slider deadZoneSlider;
        private Label deadZoneLabel;

        public SettingsControlsPage(SettingsPanel owner) : base(owner, "UI/SettingsControls")
        {
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

            rumbleRow = AddOnOffRow("Rumble", () => SettingsStore.Rumble, SettingsStore.SetRumble);

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
            BuildRebindTable(kbmSection, KbmRows, kbmKeyLabels, BindingTools.KbmGroup, BindingTools.GamepadGroup);
            BuildRebindTable(padSection, GamepadRows, padKeyLabels, BindingTools.GamepadGroup, BindingTools.KbmGroup);

            unstuckRow = AddActionRow("Unstuck");
            unstuckRow.Confirmed += () => UI.Unstuck();

            resetBindingsRow = AddActionRow("Reset Bindings");
            resetBindingsRow.Confirmed += OnResetBindings;

            AddSubHeader("GRAPPING HOOK");

            ropeWallSnapRow = AddOnOffRow("Wall Snap", () => SettingsStore.RopeWallSnap, SettingsStore.SetRopeWallSnap);
            ropeBombSnapRow = AddOnOffRow("Bomb Snap", () => SettingsStore.RopeBombSnap, SettingsStore.SetRopeBombSnap);

            deadZoneLabel = AddSliderRow("Snap Dead Zone", 0f, 1f, out deadZoneSlider);
            deadZoneSlider.RegisterValueChangedCallback(e =>
            {
                SettingsStore.SetRopeSnapDeadZone(e.newValue);
                deadZoneLabel.text = e.newValue.ToString("0.00");
            });

            ropeAdaptiveRow = AddOnOffRow("Adaptive Speed", () => SettingsStore.RopeAdaptiveSpeed, SettingsStore.SetRopeAdaptiveSpeed);
        }

        public override void Refresh()
        {
            deviceRow.Value = DeviceText(SettingsStore.Device);
            rumbleRow.Value = OnOffText(SettingsStore.Rumble);
            SetSlider(mouseSlider, SettingsStore.MouseSensitivity, mouseLabel, SensText);
            SetSlider(stickSlider, SettingsStore.StickSensitivity, stickLabel, SensText);

            ropeWallSnapRow.Value = OnOffText(SettingsStore.RopeWallSnap);
            ropeBombSnapRow.Value = OnOffText(SettingsStore.RopeBombSnap);
            SetSlider(deadZoneSlider, SettingsStore.RopeSnapDeadZone, deadZoneLabel, v => v.ToString("0.00"));
            ropeAdaptiveRow.Value = OnOffText(SettingsStore.RopeAdaptiveSpeed);

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

        private void OnResetBindings()
        {
            BindingTools.ResetAllBindings();
            Refresh();
        }

        /// <summary>Builds one rebind table: a plain row per action, whole row is the button.
        /// Non-rebindable rows (Aim / the sticks) go inert instead of hidden, so the table
        /// layout never reflows. Fills <paramref name="keyLabels"/> with each row's key readout.</summary>
        private void BuildRebindTable(VisualElement section, BindingRow[] table, Label[] keyLabels,
            string group, string excludeGroup)
        {
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
        }

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
    }
}
