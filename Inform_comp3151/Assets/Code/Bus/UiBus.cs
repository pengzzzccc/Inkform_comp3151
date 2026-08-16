using System;
using UnityEngine;

namespace Inkform.Bus
{
    /// <summary>
    /// UI bus: menu interaction feedback. Same split as the gameplay buses — the control that was
    /// touched raises the signal, and whoever presents it (currently UIManager, which owns the Cue
    /// slots) subscribes. UiButtonFx / UiToggleFx never need to know the audio system exists.
    ///
    /// Unlike LifeBus / PlayerBus this bus keeps no snapshot: "the pointer is over a button" is
    /// already stored by the EventSystem, and nothing needs to read it back later.
    ///
    /// These are presentation signals only. A button's actual behaviour still goes through its own
    /// onClick listener (BasePanel.Bind) — do not route game logic through Clicked, which fires for
    /// every button on every panel and cannot say which one.
    /// </summary>
    public static class UiBus
    {
        /// <summary>Pointer entered a control, or navigation moved the selection onto it. Both are
        /// "the player is now looking at this", so they share one signal rather than splitting into
        /// mouse and gamepad variants.</summary>
        public static event Action Hovered;

        /// <summary>A control was activated — pointer click or the UI map's Submit.</summary>
        public static event Action Clicked;

        /// <summary>A toggle flipped; the argument is the new state. Raised only for genuine user
        /// input: the settings panel pulls values back with SetIsOnWithoutNotify (SettingsPanel.Refresh),
        /// which deliberately does not fire onValueChanged, so re-opening a panel stays silent.</summary>
        public static event Action<bool> Toggled;

        public static void RaiseHovered() => Hovered?.Invoke();

        public static void RaiseClicked() => Clicked?.Invoke();

        public static void RaiseToggled(bool on) => Toggled?.Invoke(on);

        // Static fields do not clear on scene reload; with Domain Reload off, dead subscribers from
        // the previous run linger (same reason as LifeBus.ResetStatics)
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Hovered = null;
            Clicked = null;
            Toggled = null;
        }
    }
}
