using Inkform.Input;
using Inkform.Settings;
using UnityEngine.UIElements;

namespace Inkform.UI
{
    /// <summary>Which settings sub-page is on stage. Namespace-level so the ROOT page's
    /// navigation buttons can route through the shell's ShowTab.</summary>
    internal enum SettingsTab { Root, Audio, Video, Controls }

    /// <summary>
    /// Settings shell, two-level like Celeste's OuiOptions: the ROOT page lists the categories,
    /// each category is its own page. Every page owns a triple of files — SettingsRoot /
    /// SettingsAudio / SettingsVideo / SettingsControls (.uxml + .uss + Page class) — mounted
    /// here into #Pages and routed by ShowTab; the shared row vocabulary lives on the
    /// SettingsSubPage base and the row styles cascade from this shell's Settings.uss.
    ///
    /// Esc/BACK back out one level (HandleBack answers the UIManager's escape stack), and
    /// opening the sheet always lands on ROOT. RESET ALL (ROOT page) funnels through ResetAll:
    /// store defaults + binding reset + every page refreshed.
    /// </summary>
    public class SettingsPanel : ToolkitPanel
    {
        private readonly Label title;
        private readonly SettingsSubPage[] pages;
        private SettingsTab tab;

        /// <summary>The UIManager handle page classes need (Unstuck etc.) — the ToolkitPanel's
        /// own UI is protected.</summary>
        internal UIManager Ui => UI;

        public SettingsPanel(VisualElement root, UIManager ui) : base(root, ui)
        {
            title = Q<Label>("Title");

            pages = new SettingsSubPage[]
            {
                new SettingsRootPage(this),
                new SettingsAudioPage(this),
                new SettingsVideoPage(this),
                new SettingsControlsPage(this),
            };

            VisualElement mount = Q("Pages");
            foreach (SettingsSubPage page in pages) mount?.Add(page.Root);

            Bind(Q<Button>("Btn_Back"), "Btn_Back", OnBack);

            ShowTab(SettingsTab.Root);
        }

        // ---- Routing ----

        internal void ShowTab(SettingsTab value)
        {
            tab = value;

            for (int i = 0; i < pages.Length; i++)
            {
                bool active = (int)value == i;
                pages[i].Root.style.display = active ? DisplayStyle.Flex : DisplayStyle.None;
                if (active)
                {
                    title.text = pages[i].Title;
                    pages[i].OnShown();   // refresh + scroll to top
                }
            }

            // Refocus for gamepad/keyboard: FocusFirst skips hidden pages, so this lands on the
            // freshly shown page's first live control.
            FocusFirst();
        }

        /// <summary>Esc / the BACK button: back out one level. True = consumed internally (a
        /// category page returned to ROOT); false = already at ROOT, the caller closes the sheet.
        /// The UIManager's escape stack routes through here.</summary>
        public bool HandleBack()
        {
            if (tab == SettingsTab.Root) return false;
            ShowTab(SettingsTab.Root);
            return true;
        }

        /// <summary>ROOT's RESET ALL: store defaults + the shared binding reset, then every page
        /// re-reads the store. ResetToDefaults already deletes the persisted binding JSON; the
        /// shared asset's in-memory overrides must go too, or the rows would show custom keys
        /// while the save is empty.</summary>
        internal void ResetAll()
        {
            SettingsStore.ResetToDefaults();
            BindingTools.ResetAllBindings();
            foreach (SettingsSubPage page in pages) page.Refresh();
        }

        // ---- Lifecycle ----

        protected override void OnOpen()
        {
            // Always land on ROOT: whatever category page was last shown, a fresh visit starts
            // at the category list (same stance as Celeste's options).
            ShowTab(SettingsTab.Root);
        }

        protected override void OnClose()
        {
            // A half-finished interactive rebind must not keep swallowing input after close
            BindingTools.CancelActive();
        }

        private void OnBack()
        {
            if (!HandleBack()) UI.CloseSettings();
        }
    }
}
