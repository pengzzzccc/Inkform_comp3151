using Inkform.Bus;
using UnityEngine;
using UnityEngine.UI;

namespace Inkform.UI
{
    /// <summary>
    /// Makes a Toggle's state readable at a glance: the box fills with the accent colour and shows a
    /// tick when on, and a word beside it spells out ON / OFF. The stock control only faded a small
    /// blue square in and out inside a grey one, which said almost nothing.
    ///
    /// Unlike UiButtonFx this component owns the box's colour outright (UIBuilder sets the Toggle to
    /// Transition.None), because the "on" look is a state the ColorBlock cannot express — uGUI's
    /// transitions know about hover and press, not about isOn. That is safe here in a way it would
    /// not be for buttons: no toggle in this UI uses interactable to mean anything.
    ///
    /// Children are found by name in Awake, the same convention BasePanel uses, so UIBuilder only has
    /// to name them right — there is no Inspector wiring to lose when the prefabs are regenerated.
    /// </summary>
    [RequireComponent(typeof(Toggle))]
    public class UiToggleFx : UiSelectableFx
    {
        /// <summary>Names UIBuilder.AddToggle gives the pieces this component drives.</summary>
        public const string BackgroundName = "Background";
        public const string CheckName = "Check";
        public const string StateLabelName = "Lbl_State";

        // Colour units per second. Roughly matches the 0.1s fadeDuration the buttons' ColorBlock uses,
        // so a toggle and a button next to it feel like the same UI.
        private const float ColorSpeed = 6f;

        // How far the hovered box is pushed toward white. Enough to read as a highlight over both the
        // dark "off" box and the already-bright accent "on" box.
        private const float HoverLift = 0.18f;

        private Toggle toggle;
        private Image background;
        private GameObject check;
        private Text stateLabel;

        private bool lastKnownOn;

        protected override Selectable Target
        {
            get
            {
                if (toggle == null) toggle = GetComponent<Toggle>();
                return toggle;
            }
        }

        private void Awake()
        {
            toggle = GetComponent<Toggle>();

            Transform bg = transform.Find(BackgroundName);
            if (bg != null) background = bg.GetComponent<Image>();

            Transform chk = transform.Find(CheckName);
            if (chk != null) check = chk.gameObject;

            Transform lbl = transform.Find(StateLabelName);
            if (lbl != null) stateLabel = lbl.GetComponent<Text>();

            // The sound hangs off onValueChanged rather than off the click handlers in the base class
            // precisely because SettingsPanel.Refresh pulls values back with SetIsOnWithoutNotify,
            // which does not fire this. Re-opening the settings sheet therefore stays silent while a
            // real click still sounds.
            toggle.onValueChanged.AddListener(UiBus.RaiseToggled);

            lastKnownOn = toggle.isOn;
            ApplyState(instant: true);
        }

        private void OnEnable()
        {
            // A panel re-opening runs Refresh() around the same time this object is switched back on;
            // snapping here means the box never shows a stale colour for a frame.
            if (toggle != null)
            {
                lastKnownOn = toggle.isOn;
                ApplyState(instant: true);
            }
        }

        protected override void Update()
        {
            base.Update();

            if (toggle == null) return;

            // Polled rather than purely event-driven: SetIsOnWithoutNotify is a legitimate way for the
            // panel to push a value in (SettingsPanel.Refresh uses it for exactly that), and it raises
            // no event for a listener to catch. Three toggles exist in the whole UI, so this costs
            // nothing.
            if (toggle.isOn != lastKnownOn)
            {
                lastKnownOn = toggle.isOn;
                ApplyState(instant: false);
            }

            if (background != null)
            {
                Color target = TargetColor();
                if (background.color != target)
                    background.color = Color.Lerp(background.color, target, ColorSpeed * Time.unscaledDeltaTime);
            }
        }

        /// <summary>Silent on purpose. A toggle's click and its state change are the same gesture, so
        /// letting the base class announce a click here would stack a click sound on top of the
        /// toggle sound onValueChanged already raises.</summary>
        protected override void OnActivated() { }

        /// <summary>Box colour for the current on/off and hover state. Hover lifts whichever base
        /// colour the state already chose, so the highlight reads on both.</summary>
        private Color TargetColor()
        {
            Color baseColor = lastKnownOn ? UiPalette.Accent : UiPalette.ControlOff;
            if (!Interactable) return UiPalette.Disabled;
            return IsHighlighted ? Color.Lerp(baseColor, Color.white, HoverLift) : baseColor;
        }

        /// <summary>The tick and the word switch immediately — they are readouts, not motion. Only the
        /// colour animates. instant additionally snaps the colour, for the first frame after Awake or
        /// a re-enable where there is no previous state worth crossfading from.</summary>
        private void ApplyState(bool instant)
        {
            if (check != null) check.SetActive(lastKnownOn);

            if (stateLabel != null)
            {
                stateLabel.text = lastKnownOn ? "ON" : "OFF";
                stateLabel.color = lastKnownOn ? Color.white : UiPalette.OffText;
            }

            if (instant && background != null) background.color = TargetColor();
        }
    }
}
