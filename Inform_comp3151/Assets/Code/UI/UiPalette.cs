using UnityEngine;

namespace Inkform.UI
{
    /// <summary>
    /// The one place UI colours are defined. Lived inside UIBuilder while the builder was the only
    /// thing that painted anything; it moved out here once UiToggleFx started driving a control's
    /// colour at runtime, so the "on" blue in the built prefab and the "on" blue the toggle lerps to
    /// cannot drift apart.
    ///
    /// Runtime rather than editor-only for the same reason: UIBuilder (editor) already references
    /// this assembly, but a runtime component cannot reference the editor one.
    /// </summary>
    public static class UiPalette
    {
        public static readonly Color Sheet = new Color(0.05f, 0.06f, 0.10f, 0.92f);   // full-screen panel backdrop
        public static readonly Color Card = new Color(0.10f, 0.12f, 0.18f, 0.96f);    // floating card / popup list
        public static readonly Color Pane = new Color(0.14f, 0.16f, 0.22f, 0.9f);     // settings content pane
        public static readonly Color Control = new Color(0.22f, 0.26f, 0.36f, 1f);    // buttons, slider tracks, toggle boxes
        public static readonly Color Accent = new Color(0.35f, 0.55f, 0.9f, 1f);      // slider fill, toggle "on" box
        public static readonly Color Track = new Color(0.10f, 0.12f, 0.16f, 1f);      // scrollbar track
        public static readonly Color Handle = new Color(0.45f, 0.50f, 0.62f, 1f);     // scrollbar handle
        public static readonly Color Hint = new Color(0.62f, 0.64f, 0.70f, 1f);       // small explanatory text
        public static readonly Color Key = new Color(0.80f, 0.80f, 0.80f, 1f);        // current key binding text
        public static readonly Color None = new Color(0f, 0f, 0f, 0f);                // see UIBuilder.BuildPanelPrefab

        // Selectable states. These are ColorBlock entries, and a ColorBlock *multiplies* the target
        // Image's own colour — so UIBuilder paints button Images white and lets these carry the real
        // colour. Tinting a dark Image instead is what made hover invisible before: Unity's stock
        // highlighted colour is 0.96 grey, and 0.96 x Control moves it by about two RGB steps.
        public static readonly Color ControlHover = new Color(0.34f, 0.42f, 0.58f, 1f);
        public static readonly Color ControlPressed = new Color(0.16f, 0.20f, 0.28f, 1f);
        public static readonly Color Disabled = new Color(0.16f, 0.18f, 0.24f, 0.6f);

        // Toggle states
        public static readonly Color ControlOff = new Color(0.16f, 0.18f, 0.24f, 1f);  // unchecked box
        public static readonly Color OffText = new Color(0.55f, 0.58f, 0.65f, 1f);     // the "OFF" word, dimmer than "ON"
    }
}
