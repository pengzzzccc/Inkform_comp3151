using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UIElements;

namespace Inkform.UI
{
    /// <summary>
    /// The boot sequence the player sees after the engine splash: the sting and the studio card
    /// start together (fade in, dwell, fade out), then the game card fades in, then it WAITS:
    /// any key / click / pad button ends the sequence — UIManager.FinishBoot opens the main
    /// menu and starts the menu music a second later. An input during the cards skips straight
    /// to the end too: the whole intro is dismissible, which is what players expect (research
    /// round conclusion — the complaint is never "there is a logo", it is "I cannot leave").
    ///
    /// Art, sting and timings come from the BootLogos asset (Resources/UI/BootLogos); empty
    /// slots degrade to text placeholders and silence. Fades ride UiFx.Tween (unscaled). The
    /// sheet overrides the screen slide into plain opacity fades and never takes focus — a
    /// boot card is not a navigable panel, any input dismisses it.
    /// </summary>
    public class BootPanel : ToolkitPanel
    {
        private enum Phase { StudioIn, StudioHold, StudioOut, GameIn, Wait }

        private const string LogosPath = "UI/BootLogos";
        private const float GameFadeSeconds = 0.5f;
        private const float SkipArmedDelay = 0.25f;   // swallow the key that dismissed the engine splash

        private readonly VisualElement studioCard;
        private readonly VisualElement studioImage;
        private readonly Label studioPlaceholder;
        private readonly VisualElement gameCard;
        private readonly VisualElement gameImage;
        private readonly Label gamePlaceholder;
        private readonly Label skipHint;

        private BootLogos logos;
        private Phase phase = Phase.StudioIn;
        private float holdUntil;        // end of the studio dwell (StudioHold only)
        private float skipArmedAt;      // real time before which stray inputs are swallowed
        private bool finished;          // one-way latch: FinishBoot fires exactly once

        public BootPanel(VisualElement root, UIManager ui) : base(root, ui)
        {
            studioCard = Q("StudioCard");
            studioImage = Q("StudioImage");
            studioPlaceholder = Q<Label>("StudioPlaceholder");
            gameCard = Q("GameCard");
            gameImage = Q("GameImage");
            gamePlaceholder = Q<Label>("GamePlaceholder");
            skipHint = Q<Label>("SkipHint");

            logos = Resources.Load<BootLogos>(LogosPath);
        }

        protected override void OnOpen()
        {
            phase = Phase.StudioIn;
            finished = false;
            skipArmedAt = Time.unscaledTime + SkipArmedDelay;

            // The sting and the studio card start together: this panel opens the moment the menu
            // scene loads, i.e. right after the engine splash — the sequence's first sound marks
            // the studio card, not the game card.
            if (logos != null && logos.sting != null) UI.PlayBootSting(logos.sting);

            ApplyCard(studioCard, studioImage, studioPlaceholder,
                logos != null ? logos.studioLogo : null, "STUDIO LOGO");
            ApplyCard(gameCard, gameImage, gamePlaceholder,
                logos != null ? logos.gameLogo : null, GameTitle);
            gameCard.style.opacity = 0f;
            skipHint.style.opacity = 0f;

            Fade(studioCard, 0f, 1f, FadeSeconds, () =>
            {
                holdUntil = Time.unscaledTime + StudioHold;
                phase = Phase.StudioHold;
            });
        }

        protected internal override void Tick(float unscaledDelta)
        {
            if (!IsOpen || finished) return;

            // The any-key gate. Armed a beat after open so the dismissal of the engine splash
            // cannot punch straight through this sequence.
            if (Time.unscaledTime >= skipArmedAt && AnyInputPressed())
            {
                finished = true;
                UI.FinishBoot();
                return;
            }

            if (phase == Phase.StudioHold && Time.unscaledTime >= holdUntil)
            {
                phase = Phase.StudioOut;
                Fade(studioCard, 1f, 0f, FadeSeconds, BeginGameCard);
            }
        }

        private void BeginGameCard()
        {
            phase = Phase.GameIn;

            // Silent by design: the sting already marked the studio card; the game card fades in
            // unaccompanied and the sequence waits for input.
            Fade(gameCard, 0f, 1f, GameFadeSeconds, () =>
            {
                phase = Phase.Wait;
                Fade(skipHint, 0f, 0.7f, FadeSeconds, null);
            });
        }

        // ---- Plumbing ----

        private float FadeSeconds => logos != null ? Mathf.Max(0.05f, logos.fadeSeconds) : 0.4f;
        private float StudioHold => logos != null ? Mathf.Max(0f, logos.studioHoldSeconds) : 2f;
        private string GameTitle =>
            logos != null && !string.IsNullOrEmpty(logos.gameTitle) ? logos.gameTitle : "BOMB SLIME";

        private static void ApplyCard(VisualElement card, VisualElement image, Label placeholder,
            Sprite sprite, string fallbackText)
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

        private static bool AnyInputPressed()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null && kb.anyKey.wasPressedThisFrame) return true;

            Gamepad pad = Gamepad.current;
            if (pad != null && AnyButtonPressed(pad)) return true;

            Mouse mouse = Mouse.current;
            if (mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame)) return true;
            return false;
        }

        /// <summary>Gamepad exposes no anyButton property: walk the pad's controls and ask each
        /// button directly. Stick axes are AxisControls and skip themselves via the type filter.</summary>
        private static bool AnyButtonPressed(Gamepad pad)
        {
            foreach (InputControl control in pad.allControls)
            {
                if (control is ButtonControl button && button.wasPressedThisFrame) return true;
            }
            return false;
        }

        // The black sheet neither slides nor takes focus — cards fade individually, and the
        // leave is a plain fade so the menu slides in under a dissolving boot card.
        protected override void PlayEnter() => Root.style.opacity = new StyleFloat(1f);

        protected override void PlayLeave(Action onHidden) =>
            UiFx.Tween(Root, FadeSeconds, t => t, v => Root.style.opacity = 1f - v, 0f, () =>
            {
                Root.style.opacity = new StyleFloat(1f);   // reset for the next session's open
                onHidden?.Invoke();
            });
    }
}
