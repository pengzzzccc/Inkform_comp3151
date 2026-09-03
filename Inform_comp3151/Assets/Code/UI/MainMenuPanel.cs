using UnityEngine;
using UnityEngine.UI;

namespace Inkform.UI
{
    /// <summary>
    /// Main menu sheet: game title + Play / Settings / Exit. This panel only reports button presses to
    /// the UIManager — it decides nothing. Play goes through the save menu (the design's slot flow),
    /// which is where a run is continued or started.
    ///
    /// Buttons are discovered at runtime from the panel's own children by name (Btn_Play / Btn_Settings
    /// / Btn_Exit) — no serialized references to wire by hand. The naming convention is defined here.
    /// </summary>
    public class MainMenuPanel : BasePanel
    {
        [SerializeField] private Button playButton;
        [SerializeField] private Button settingsButton;
        [SerializeField] private Button exitButton;

        protected override void Awake()
        {
            base.Awake();
            if (playButton == null) playButton = FindButton("Btn_Play");
            if (settingsButton == null) settingsButton = FindButton("Btn_Settings");
            if (exitButton == null) exitButton = FindButton("Btn_Exit");

            Bind(playButton, "Btn_Play", OnPlay);
            Bind(settingsButton, "Btn_Settings", OnSettings);
            Bind(exitButton, "Btn_Exit", OnExit);
        }

        private void OnPlay()
        {
            // Design flow: Play -> save menu (slot selection) -> game. Which of the two a slot does
            // is the save menu's business; this sheet only opens it.
            UIManager.Instance.Open<SaveMenuPanel>();
        }

        private void OnSettings() => UIManager.Instance.OpenSettings();
        private void OnExit() => UIManager.Instance.Quit();
    }
}
