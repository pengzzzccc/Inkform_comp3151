using UnityEngine;
using UnityEngine.UIElements;

namespace Inkform.UI
{
    /// <summary>
    /// Main menu sheet, Celeste OuiMainMenu layout: title block and a menu column on the left —
    /// a big BEGIN above the smaller OPTIONS / EXIT. This panel only reports button presses to
    /// the UIManager, which decides everything (same stance as the old uGUI MainMenuPanel): Play
    /// goes through the save menu, Settings opens the options sheet, Exit quits.
    /// </summary>
    public class MainMenuPanel : ToolkitPanel
    {
        /// <summary>Celeste's menu list slides in from off-screen left (TweenFrom -500), unlike
        /// every other sheet which comes in from the right.</summary>
        protected override float EnterFromX => -500f;

        public MainMenuPanel(VisualElement root, UIManager ui) : base(root, ui)
        {
            Bind(Q<Button>("Btn_Begin"), "Btn_Begin", OnBegin);
            Bind(Q<Button>("Btn_Settings"), "Btn_Settings", OnSettings);
            Bind(Q<Button>("Btn_Exit"), "Btn_Exit", OnExit);

            // PlayerSettings > bundleVersion, shown bottom left. Missing label is legal (the
            // sheet still works without it), same tolerance as every other Bind.
            Label version = Q<Label>("Lbl_Version");
            if (version != null) version.text = $"v{Application.version}";
        }

        private void OnBegin()
        {
            // Design flow: Play -> save menu (slot selection) -> game.
            UI.Open<SaveMenuPanel>();
        }

        private void OnSettings() => UI.OpenSettings();

        private void OnExit() => UI.Quit();
    }
}
