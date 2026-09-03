using UnityEngine;
using UnityEngine.UI;

namespace Inkform.UI
{
    /// <summary>
    /// Pause sheet over the frozen game view: Resume / Settings / Save &amp; Quit. The UIManager drives
    /// the pause state machine (timeScale, input, cursor); this panel only forwards its buttons.
    ///
    /// Buttons discovered by name: Btn_Resume / Btn_Settings / Btn_SaveAndQuit.
    ///
    /// The "Save &amp; Quit" button only quits — no save system exists yet, see UIManager.QuitToMainMenu.
    /// </summary>
    public class PausePanel : BasePanel
    {
        [SerializeField] private Button resumeButton;
        [SerializeField] private Button settingsButton;
        [SerializeField] private Button saveAndQuitButton;

        protected override void Awake()
        {
            base.Awake();
            if (resumeButton == null) resumeButton = FindButton("Btn_Resume");
            if (settingsButton == null) settingsButton = FindButton("Btn_Settings");
            if (saveAndQuitButton == null) saveAndQuitButton = FindButton("Btn_SaveAndQuit");

            Bind(resumeButton, "Btn_Resume", OnResume);
            Bind(settingsButton, "Btn_Settings", OnSettings);
            Bind(saveAndQuitButton, "Btn_SaveAndQuit", OnQuitToMainMenu);
        }

        private void OnResume() => UIManager.Instance.Resume();
        private void OnSettings() => UIManager.Instance.OpenSettings();
        private void OnQuitToMainMenu() => UIManager.Instance.QuitToMainMenu();
    }
}
