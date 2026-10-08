using System;
using UnityEngine;

namespace Inkform.Bus
{
    /// <summary>
    /// UI bus: menu interaction feedback. Same split as the gameplay buses — the control that was
    /// touched raises the signal, and whoever presents it (currently UIManager, which owns the Cue
    /// slots) subscribes. The raising controls (ToolkitPanel.Bind, OptionRow, the settings pages)
    /// never need to know the audio system exists.
    ///
    /// Unlike LifeBus / PlayerBus this bus keeps no snapshot: nothing needs to read "the pointer
    /// is over a button" back later.
    ///
    /// These are presentation signals only. A button's actual behaviour still goes through its own
    /// click handler (ToolkitPanel.Bind) — do not route game logic through Clicked, which fires for
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

        /// <summary>An option row's value was stepped or the row was confirmed; the argument picks
        /// the cue — true for a right step / click ("on"), false for a left step ("off"). Raised
        /// only for genuine user input: the pages write values back through OptionRow.Value
        /// (SettingsSubPage.Refresh), which raises nothing, so re-opening a page stays silent.</summary>
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
