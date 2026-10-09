using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Inkform.UI
{
    /// <summary>
    /// The boot sequence the player sees after the engine splash: the sting and the studio card
    /// start together (fade in, dwell, fade out) on black, then UIManager.FinishBoot takes over —
    /// the black sheet dissolves into the cave backdrop and the main menu's title screen (game
    /// logo + "Press any key"), which is where the any-key gate now lives. An input during the
    /// studio card skips straight there: the whole intro is dismissible, which is what players
    /// expect (research round conclusion — the complaint is never "there is a logo", it is "I
    /// cannot leave").
    ///
    /// Art, sting and timings come from the BootLogos asset (Resources/UI/BootLogos); an empty
    /// studio slot degrades to a text placeholder, an empty sting to silence. Fades ride
    /// UiFx.Tween (unscaled). The sheet overrides the screen slide into plain opacity fades and
    /// never takes focus — a boot card is not a navigable panel.
    /// </summary>
    public class BootPanel : ToolkitPanel
    {
        private const string LogosPath = "UI/BootLogos";
        private const float SkipArmedDelay = 0.25f;   // swallow the key that dismissed the engine splash

        private readonly VisualElement studioCard;
        private readonly VisualElement studioImage;
        private readonly Label studioPlaceholder;

        private BootLogos logos;
        private bool studioHolding;     // studio card fully in, waiting out its dwell
        private float holdUntil;        // end of the studio dwell (while studioHolding)
        private float skipArmedAt;      // real time before which stray inputs are swallowed
        private bool finished;          // one-way latch: FinishBoot fires exactly once

        public BootPanel(VisualElement root, UIManager ui) : base(root, ui)
        {
            studioCard = Q("StudioCard");
            studioImage = Q("StudioImage");
            studioPlaceholder = Q<Label>("StudioPlaceholder");

            logos = Resources.Load<BootLogos>(LogosPath);
        }

        protected override void OnOpen()
        {
            studioHolding = false;
            finished = false;
            skipArmedAt = Time.unscaledTime + SkipArmedDelay;

            // The sting and the studio card start together: this panel opens the moment the menu
            // scene loads, i.e. right after the engine splash — the sequence's first sound marks
            // the studio card.
            if (logos != null && logos.sting != null) UI.PlayBootSting(logos.sting);

            ApplyCard(studioImage, studioPlaceholder, logos != null ? logos.studioLogo : null, "STUDIO LOGO");

            Fade(studioCard, 0f, 1f, FadeSeconds, () =>
            {
                holdUntil = Time.unscaledTime + StudioHold;
                studioHolding = true;
            });
        }

        protected internal override void Tick(float unscaledDelta)
        {
            if (!IsOpen || finished) return;

            // Skip to the title screen. Armed a beat after open so the dismissal of the engine
            // splash cannot punch straight through this sequence. (Esc / pad B / Start reach
            // FinishBoot through UIManager's Escape stack, which does not wait for this.)
            if (Time.unscaledTime >= skipArmedAt && AnyInputPressed())
            {
                Finish();
                return;
            }

            if (studioHolding && Time.unscaledTime >= holdUntil)
            {
                studioHolding = false;
                Fade(studioCard, 1f, 0f, FadeSeconds, Finish);
            }
        }

        private void Finish()
        {
            if (finished) return;
            finished = true;
            UI.FinishBoot();
        }

        // ---- Plumbing ----

        private float FadeSeconds => logos != null ? Mathf.Max(0.05f, logos.fadeSeconds) : 0.4f;
        private float StudioHold => logos != null ? Mathf.Max(0f, logos.studioHoldSeconds) : 2f;

        private static void ApplyCard(VisualElement image, Label placeholder, Sprite sprite, string fallbackText)
        {
            if (sprite != null)
            {
                image.style.backgroundImage = new StyleBackground(sprite);
                image.style.display = DisplayStyle.Flex;
                placeholder.style.display = DisplayStyle.None;
            }
            else
            {
                image.style.display = DisplayStyle.None;
                placeholder.text = fallbackText;
                placeholder.style.display = DisplayStyle.Flex;
            }
        }

        private static void Fade(VisualElement el, float from, float to, float duration, Action onDone) =>
            UiFx.Tween(el, duration, t => t, v => el.style.opacity = Mathf.Lerp(from, to, v), 0f, onDone);

        // The black sheet neither slides nor takes focus — the card fades on its own, and the
        // leave is a plain fade so the cave backdrop and title screen dissolve in under it.
        protected override void PlayEnter() => Root.style.opacity = new StyleFloat(1f);

        protected override void PlayLeave(Action onHidden) =>
            UiFx.Tween(Root, FadeSeconds, t => t, v => Root.style.opacity = 1f - v, 0f, () =>
            {
                Root.style.opacity = new StyleFloat(1f);   // reset for the next session's open
                onHidden?.Invoke();
            });
    }
}
