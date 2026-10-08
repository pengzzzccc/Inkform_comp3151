using System;
using Inkform.Settings;
using UnityEngine;
using UnityEngine.UIElements;

namespace Inkform.UI
{
    /// <summary>
    /// Base class for one settings sub-page. Each page owns its own triple of files — a UXML
    /// template under Resources/UI, its USS, and a Page class — instantiated by the base
    /// constructor and mounted into the Settings shell by SettingsPanel, which routes between
    /// the pages (ShowTab) and forwards Refresh.
    ///
    /// The shared row vocabulary (option rows, slider rows, sub-headers) lives here so every
    /// page builds rows the same way; the row STYLES cascade from the shell's Settings.uss
    /// (a page is a descendant of the shell, so its stylesheet covers them), leaving each
    /// page's own .uss for page-private tweaks.
    /// </summary>
    public abstract class SettingsSubPage
    {
        protected readonly SettingsPanel Owner;

        /// <summary>The instantiated template container, display-toggled by the shell.</summary>
        public VisualElement Root { get; }

        /// <summary>The header title the shell shows while this page is active.</summary>
        public abstract string Title { get; }

        protected VisualElement rows;                       // row-factory target (#Rows)
        private ScrollView scroll;                          // #Scroll, parked at the top on show

        protected UIManager UI => Owner.Ui;

        protected SettingsSubPage(SettingsPanel owner, string uxml)
        {
            Owner = owner;

            VisualTreeAsset tree = Resources.Load<VisualTreeAsset>(uxml);
            if (tree == null)
            {
                Debug.LogWarning($"SettingsSubPage: missing UXML at Resources/{uxml} — {GetType().Name} is unavailable");
                Root = new VisualElement();
                return;
            }

            Root = tree.Instantiate();
            // TemplateContainers do not stretch on their own; pin to the shell's area so the
            // page's own .settings-scroll positioning applies (same trick as UIManager.AddPanel).
            Root.style.position = Position.Absolute;
            Root.style.left = 0f;
            Root.style.top = 0f;
            Root.style.right = 0f;
            Root.style.bottom = 0f;

            scroll = Root.Q<ScrollView>("Scroll");
            rows = Root.Q<VisualElement>("Rows");
        }

        /// <summary>Called by the shell every time the page becomes active (after the display
        /// flip): refresh row values and park the scroll at the top.</summary>
        public virtual void OnShown()
        {
            Refresh();
            if (scroll != null && scroll.verticalScroller != null) scroll.verticalScroller.value = 0f;
        }

        /// <summary>Pull SettingsStore's current values into this page's live controls.</summary>
        public virtual void Refresh() { }

        // ---- Shared row vocabulary (identical to the old single-panel factories) ----

        protected void AddSubHeader(string text)
        {
            var header = new Label(text);
            header.AddToClassList("subheader");
            header.AddToClassList("outline");
            header.pickingMode = PickingMode.Ignore;
            rows?.Add(header);
        }

        protected OptionRow AddOptionRow(string labelText, string initial)
        {
            var row = new OptionRow(labelText) { Value = initial };
            rows?.Add(row);
            return row;
        }

        /// <summary>A two-state row: every step (click included — OptionRow clicks step +1)
        /// flips the value, so stepping backwards and forwards through two options is the same
        /// move (the old two-arrow rows' shape). The current value is read from the store, not
        /// captured at build time, so Refresh/RESET ALL cannot leave the flip working off a stale
        /// copy.</summary>
        protected OptionRow AddOnOffRow(string labelText, Func<bool> get, Action<bool> set)
        {
            var row = AddOptionRow(labelText, OnOffText(get()));
            row.Stepped += _ =>
            {
                bool on = !get();
                set(on);
                row.Value = OnOffText(on);
            };
            return row;
        }

        /// <summary>A click-only row (no value, no chevrons): Unstuck, Reset Bindings.</summary>
        protected OptionRow AddActionRow(string labelText)
        {
            var row = AddOptionRow(labelText, string.Empty);
            row.ShowArrows(false);
            return row;
        }

        protected Label AddSliderRow(string labelText, float min, float max, out Slider slider)
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

            rows?.Add(row);
            return readout;
        }

        protected VisualElement AddSection()
        {
            var section = new VisualElement();
            rows?.Add(section);
            return section;
        }

        // ---- Shared formatting helpers ----

        // SetValueWithoutNotify: Refresh is a pull, firing onValueChanged here would push the
        // same value back into SettingsStore and rewrite PlayerPrefs for nothing
        protected static void SetSlider(Slider slider, float value, Label readout, Func<float, string> format)
        {
            if (slider == null) return;
            slider.SetValueWithoutNotify(value);
            if (readout != null && format != null) readout.text = format(value);
        }

        protected static string Percent(float value) => $"{Mathf.RoundToInt(value * 100f)}%";

        /// <summary>Sensitivity readout. A plain multiplier ("2.5"), not a percentage — the
        /// range runs to 5x and "500%" reads as a much bigger number than it is.</summary>
        protected static string SensText(float value) => value.ToString("0.0");

        protected static string FormatRes(int width, int height) => $"{width} x {height}";

        protected static string FpsText(int value) => value == 0 ? "Uncapped" : value.ToString();

        protected static string OnOffText(bool value) => value ? "ON" : "OFF";

        protected static string DeviceText(SettingsStore.InputDevice device) =>
            device == SettingsStore.InputDevice.KeyboardMouse ? "Keyboard + Mouse" : "Gamepad";

        /// <summary>Intervals are stored in seconds but shown as their reciprocal in Hz
        /// ("10 Hz" .. "0.2 Hz"), matching how the numbers read on the panel.</summary>
        protected static string RateText(float intervalSeconds)
        {
            float hz = 1f / Mathf.Max(intervalSeconds, 0.0001f);
            return $"{hz:0.#} Hz";
        }
    }
}
