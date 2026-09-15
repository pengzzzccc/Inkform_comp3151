using Inkform.Save;
using UnityEngine.UIElements;

namespace Inkform.UI
{
    /// <summary>
    /// Credits/summary sheet of a finished run, shown in the end scene. Restyled after Celeste's
    /// AreaCompleteTitle: a letter-dropped title over the run's deaths and total time, with one
    /// CONFIRM control. Numbers are painted on open (after SaveNow stamps the live counters,
    /// matching the old EndPanel) so the final room's stretch is included.
    /// </summary>
    public class EndPanel : ToolkitPanel
    {
        public EndPanel(VisualElement root, UIManager ui) : base(root, ui)
        {
            Bind(Q<Button>("Btn_Back"), "Btn_Back", OnBackClicked);
        }

        protected override void OnOpen()
        {
            SaveStore.SaveNow();

            int slot = SaveStore.ActiveSlot;
            SaveData data = slot >= 0 ? SaveStore.Get(slot) : null;
            int deaths = data != null ? data.deaths : 0;
            float seconds = data != null ? data.playSeconds : 0f;

            Q<Label>("Lbl_Deaths").text = $"Deaths  {deaths}";
            Q<Label>("Lbl_Time").text = $"Total time  {SaveMenuPanel.FormatDuration(seconds)}";

            UiFx.DropTitle(Q<VisualElement>("TitleRow"), "RUN COMPLETE", 76f);
        }

        private void OnBackClicked() => UI.ReturnToMainMenu();
    }
}
