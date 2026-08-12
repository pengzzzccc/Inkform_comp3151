using UnityEngine;
using UnityEngine.UI;

namespace Inkform.UI
{
    /// <summary>
    /// Settings sheet: left navigation (Sound / Graphics / Controls) switches the right-hand content
    /// area, with Reset Settings and Back at the bottom. Content sheets are placeholders — this version
    /// only wires the tab switching and Back, no actual settings are read or written yet.
    ///
    /// Tabs discovered by name: Btn_TabSound / Btn_TabGraphics / Btn_TabControls, each with a matching
    /// content child Content_Sound / Content_Graphics / Content_Controls. Back is Btn_Back.
    /// </summary>
    public class SettingsPanel : BasePanel
    {
        [SerializeField] private Button soundTab;
        [SerializeField] private Button graphicsTab;
        [SerializeField] private Button controlsTab;
        [SerializeField] private GameObject soundContent;
        [SerializeField] private GameObject graphicsContent;
        [SerializeField] private GameObject controlsContent;
        [SerializeField] private Button resetButton;
        [SerializeField] private Button backButton;

        protected override void Awake()
        {
            base.Awake();
            if (soundTab == null) soundTab = FindButton("Btn_TabSound");
            if (graphicsTab == null) graphicsTab = FindButton("Btn_TabGraphics");
            if (controlsTab == null) controlsTab = FindButton("Btn_TabControls");
            if (soundContent == null) soundContent = FindChild("Content_Sound");
            if (graphicsContent == null) graphicsContent = FindChild("Content_Graphics");
            if (controlsContent == null) controlsContent = FindChild("Content_Controls");
            if (resetButton == null) resetButton = FindButton("Btn_Reset");
            if (backButton == null) backButton = FindButton("Btn_Back");

            Bind(soundTab, "Btn_TabSound", () => ShowTab(soundContent));
            Bind(graphicsTab, "Btn_TabGraphics", () => ShowTab(graphicsContent));
            Bind(controlsTab, "Btn_TabControls", () => ShowTab(controlsContent));

            // Reset is a placeholder: nothing to reset until settings are actually persisted.
            if (resetButton != null) resetButton.interactable = false;

            Bind(backButton, "Btn_Back", OnBack);
        }

        protected override void OnOpen()
        {
            base.OnOpen();
            ShowTab(soundContent);      // settings always opens on the Sound tab
        }

        // Null-tolerant: a missing content pane just means that tab shows nothing, rather than an NRE
        // that would take the whole sheet down (this runs from OnOpen, i.e. every time it is shown)
        private void ShowTab(GameObject active)
        {
            if (soundContent != null) soundContent.SetActive(active == soundContent);
            if (graphicsContent != null) graphicsContent.SetActive(active == graphicsContent);
            if (controlsContent != null) controlsContent.SetActive(active == controlsContent);
        }

        private void OnBack() => UIManager.Instance.CloseSettings();
    }
}
