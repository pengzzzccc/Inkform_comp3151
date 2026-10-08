using UnityEngine.UIElements;

namespace Inkform.UI
{
    /// <summary>
    /// The settings ROOT page: the category list the sheet opens on (AUDIO / VIDEO / CONTROLS /
    /// RESET ALL). Pure navigation — the buttons route through the shell's ShowTab, and RESET
    /// ALL goes to the shell's reset pass (store defaults + binding reset + every page refresh).
    /// </summary>
    public class SettingsRootPage : SettingsSubPage
    {
        public override string Title => "OPTIONS";

        public SettingsRootPage(SettingsPanel owner) : base(owner, "UI/SettingsRoot")
        {
            BindPageButton("Btn_Audio", () => owner.ShowTab(SettingsTab.Audio));
            BindPageButton("Btn_Video", () => owner.ShowTab(SettingsTab.Video));
            BindPageButton("Btn_Controls", () => owner.ShowTab(SettingsTab.Controls));
            BindPageButton("Btn_ResetAll", owner.ResetAll);
        }

        // Page buttons skip the shell's open-grace guard on purpose: the worst a stray click on
        // ROOT can do is open a category, and Esc backs right out — the grace exists to stop a
        // stray input from executing the pause sheet's RESUME, not navigation.
        private void BindPageButton(string name, System.Action onClick)
        {
            Button button = Root.Q<Button>(name);
            if (button == null)
            {
                UnityEngine.Debug.LogWarning($"SettingsRootPage: no element named '{name}' — that entry does nothing");
                return;
            }

            button.clicked += () =>
            {
                Inkform.Bus.UiBus.RaiseClicked();
                UiFx.Bounce(button);   // same namespace (Inkform.UI), no qualifier needed
                onClick();
            };
            button.RegisterCallback<PointerEnterEvent>(_ => Inkform.Bus.UiBus.RaiseHovered());
            button.RegisterCallback<FocusInEvent>(_ => Inkform.Bus.UiBus.RaiseHovered());
        }
    }
}
