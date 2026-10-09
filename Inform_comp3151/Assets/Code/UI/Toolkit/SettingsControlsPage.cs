using System;
using Inkform.Input;
using Inkform.Settings;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Inkform.UI
{
    /// <summary>
    /// The CONTROLS settings page, in sections: DEVICE (input device, rumble), SENSITIVITY,
    /// KEY BINDINGS (the active device's rebind table only, each key drawn as its button icon),
    /// GRAPPLING HOOK (the rope gun's snap options the RopeGun reads live from SettingsStore) and
    /// MAINTENANCE last (Unstuck, Reset Bindings — out of the way of a stray press). The page
    /// follows a live device switch while it is open: touching a pad swaps table and icons.
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

        private OptionRow deviceRow, rumbleRow, triggerRow, unstuckRow, resetBindingsRow;
        private Slider mouseSlider, stickSlider;
        private Label mouseLabel, stickLabel;
        private VisualElement kbmSection, padSection;
        private VisualElement[] kbmKeySlots, padKeySlots;
        private Label bindingsHeader;
        private readonly TutorialHintSet glyphSet = TutorialHintSet.Load();

        // What the device content was last drawn for: Tick redraws when either changes
        private SettingsStore.InputDevice shownDevice;
        private PromptScheme shownScheme;

        private OptionRow ropeWallSnapRow, ropeBombSnapRow, ropeAdaptiveRow;
        private Slider deadZoneSlider;
        private Label deadZoneLabel;

        public SettingsControlsPage(SettingsPanel owner) : base(owner, "UI/SettingsControls")
        {
            AddSubHeader("DEVICE");
            deviceRow = AddOptionRow("Device", DeviceText(SettingsStore.Device));
            deviceRow.Stepped += dir =>
            {
                var values = (SettingsStore.InputDevice[])Enum.GetValues(typeof(SettingsStore.InputDevice));
                int i = Array.IndexOf(values, SettingsStore.Device);
                i = (i + dir + values.Length) % values.Length;
                SettingsStore.SetDevice(values[i]);
                deviceRow.Value = DeviceText(values[i]);
                RefreshDeviceContent();
            };

            rumbleRow = AddOptionRow("Rumble", RumbleText(SettingsStore.RumbleLevel));
            rumbleRow.Stepped += dir =>
            {
                var levels = (RumbleLevel[])Enum.GetValues(typeof(RumbleLevel));
                int i = Array.IndexOf(levels, SettingsStore.RumbleLevel);
                i = (i + dir + levels.Length) % levels.Length;
                SettingsStore.SetRumbleLevel(levels[i]);
                rumbleRow.Value = RumbleText(levels[i]);
            };
            triggerRow = AddOnOffRow("Trigger Effects", () => SettingsStore.TriggerEffects, SettingsStore.SetTriggerEffects);

            AddSubHeader("SENSITIVITY");
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

            // The rebind tables switch with the device, like the old Dpd_Device did.
            bindingsHeader = AddSubHeader("KEY BINDINGS");
            kbmSection = AddSection();
            padSection = AddSection();
            kbmKeySlots = new VisualElement[KbmRows.Length];
            padKeySlots = new VisualElement[GamepadRows.Length];
            BuildRebindTable(kbmSection, KbmRows, kbmKeySlots, BindingTools.KbmGroup, BindingTools.GamepadGroup);
            BuildRebindTable(padSection, GamepadRows, padKeySlots, BindingTools.GamepadGroup, BindingTools.KbmGroup);

            AddSubHeader("GRAPPLING HOOK");

            ropeWallSnapRow = AddOnOffRow("Wall Snap", () => SettingsStore.RopeWallSnap, SettingsStore.SetRopeWallSnap);
            ropeBombSnapRow = AddOnOffRow("Bomb Snap", () => SettingsStore.RopeBombSnap, SettingsStore.SetRopeBombSnap);

            deadZoneLabel = AddSliderRow("Snap Dead Zone", 0f, 1f, out deadZoneSlider);
            deadZoneSlider.RegisterValueChangedCallback(e =>
            {
                SettingsStore.SetRopeSnapDeadZone(e.newValue);
                deadZoneLabel.text = e.newValue.ToString("0.00");
            });

            ropeAdaptiveRow = AddOnOffRow("Adaptive Speed", () => SettingsStore.RopeAdaptiveSpeed, SettingsStore.SetRopeAdaptiveSpeed);

            AddSubHeader("MAINTENANCE");
            unstuckRow = AddActionRow("Unstuck");
            unstuckRow.Confirmed += () => UI.Unstuck();

            resetBindingsRow = AddActionRow("Reset Bindings");
            resetBindingsRow.Confirmed += OnResetBindings;
        }

        public override void Refresh()
        {
            rumbleRow.Value = RumbleText(SettingsStore.RumbleLevel);
            triggerRow.Value = OnOffText(SettingsStore.TriggerEffects);
            SetSlider(mouseSlider, SettingsStore.MouseSensitivity, mouseLabel, SensText);
            SetSlider(stickSlider, SettingsStore.StickSensitivity, stickLabel, SensText);

            ropeWallSnapRow.Value = OnOffText(SettingsStore.RopeWallSnap);
            ropeBombSnapRow.Value = OnOffText(SettingsStore.RopeBombSnap);
            SetSlider(deadZoneSlider, SettingsStore.RopeSnapDeadZone, deadZoneLabel, v => v.ToString("0.00"));
            ropeAdaptiveRow.Value = OnOffText(SettingsStore.RopeAdaptiveSpeed);

            // Nothing to un-stick in the menu scene — there is no player there
            unstuckRow.SetEnabled(!UI.IsInMainMenu);

            RefreshDeviceContent();
        }

        // InputHandler / UIManager switch the device on live input: follow it while on stage
        public override void Tick()
        {
            if (SettingsStore.Device != shownDevice || InteractPromptIcons.DetectCurrent() != shownScheme)
                RefreshDeviceContent();
        }

        /// <summary>Device row, which table is shown, and every key icon (the pad face may have
        /// changed too — PlayStation and Xbox draw different buttons).</summary>
        private void RefreshDeviceContent()
        {
            shownDevice = SettingsStore.Device;
            shownScheme = InteractPromptIcons.DetectCurrent();

            bool kbm = shownDevice == SettingsStore.InputDevice.KeyboardMouse;
            deviceRow.Value = DeviceText(shownDevice);
            if (bindingsHeader != null) bindingsHeader.text = kbm ? "KEY BINDINGS (KEYBOARD)" : "KEY BINDINGS (GAMEPAD)";
            if (kbmSection != null) kbmSection.style.display = kbm ? DisplayStyle.Flex : DisplayStyle.None;
            if (padSection != null) padSection.style.display = kbm ? DisplayStyle.None : DisplayStyle.Flex;

            for (int i = 0; i < KbmRows.Length; i++)
                RefreshBindingRow(i, KbmRows, kbmKeySlots[i], BindingTools.KbmGroup);
            for (int i = 0; i < GamepadRows.Length; i++)
                RefreshBindingRow(i, GamepadRows, padKeySlots[i], BindingTools.GamepadGroup);
        }

        private void OnResetBindings()
        {
            BindingTools.ResetAllBindings();
            Refresh();
        }

        /// <summary>Builds one rebind table: a plain row per action, whole row is the button.
        /// Non-rebindable rows (Aim / the sticks) go inert instead of hidden, so the table
        /// layout never reflows. Fills <paramref name="keySlots"/> with each row's icon holder.</summary>
        private void BuildRebindTable(VisualElement section, BindingRow[] table, VisualElement[] keySlots,
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

                var key = new VisualElement { pickingMode = PickingMode.Ignore };
                key.AddToClassList("row-glyphs");
                button.Add(key);

                section.Add(button);
                keySlots[i] = key;

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

        private void OnRebindPressed(int row, BindingRow[] table, VisualElement keySlot, string group, string excludeGroup)
        {
            BindingRow cfg = table[row];
            InputAction action = GetAction(cfg.actionName);
            if (action == null) return;

            int index = BindingTools.FindBindingIndex(action, group, cfg.compositePart);
            if (index < 0) return;

            BindingTools.StartRebind(action, index, excludeGroup, () => RefreshBindingRow(row, table, keySlot, group));
        }

        /// <summary>Draws the row's current binding as its button icon (keycap for keys, pad art
        /// for the active face), from the live effective path so a rebind shows at once.</summary>
        private void RefreshBindingRow(int row, BindingRow[] table, VisualElement keySlot, string group)
        {
            if (keySlot == null) return;
            keySlot.Clear();

            BindingRow cfg = table[row];
            InputAction action = GetAction(cfg.actionName);
            int index = action != null ? BindingTools.FindBindingIndex(action, group, cfg.compositePart) : -1;
            if (index < 0)
            {
                keySlot.Add(GlyphElements.Create(new InputGlyphs.Glyph { label = "-" }));
                return;
            }

            // The pad table follows the pad in hand; with the keyboard active it shows Xbox faces
            PromptScheme scheme = group == BindingTools.KbmGroup ? PromptScheme.KeyboardMouse
                : shownScheme != PromptScheme.KeyboardMouse ? shownScheme : PromptScheme.Xbox;
            keySlot.Add(GlyphElements.Create(InputGlyphs.ResolvePath(
                action.bindings[index].effectivePath, BindingTools.GetDisplay(action, index), scheme, glyphSet)));
        }

        private static string RumbleText(RumbleLevel level) => level switch
        {
            RumbleLevel.Low => "LOW",
            RumbleLevel.Medium => "MEDIUM",
            RumbleLevel.High => "HIGH",
            _ => "OFF",
        };

        private static InputAction GetAction(string actionName)
        {
            var map = InputActions.Wrapper.Player.Get();
            return map != null ? map.FindAction(actionName, false) : null;
        }
    }
}
