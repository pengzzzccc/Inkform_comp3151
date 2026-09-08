using Inkform.Save;
using UnityEngine;
using UnityEngine.UI;

namespace Inkform.UI
{
    /// <summary>
    /// Credits/summary sheet of a finished run, shown in the end scene (WorldDefinition.endRoom ->
    /// SceneDirector.IsEndScene -> UIManager opens this instead of the gameplay HUD). Purely
    /// presentational: the numbers come from the run's save slot, painted on open like the save
    /// sheet — the last stretch of play has to be stamped in first, or the final room's time and
    /// deaths would be missing from the summary.
    ///
    /// Controls are found by name like every built panel: Lbl_Deaths / Lbl_Time / Btn_Back.
    /// </summary>
    public class EndPanel : BasePanel
    {
        private Button backButton;
        private Text deathsText;
        private Text timeText;

        protected override void Awake()
        {
            base.Awake();

            if (backButton == null) backButton = FindButton("Btn_Back");
            if (deathsText == null) deathsText = FindText("Lbl_Deaths");
            if (timeText == null) timeText = FindText("Lbl_Time");

            Bind(backButton, "Btn_Back", OnBackClicked);
        }

        // Painted on open rather than in Awake: a run finished since the sheet was last shown has
        // to read as changed. SaveNow stamps the live counters onto the slot — deaths and playSeconds
        // are only written on progress saves, so without it the summary lags behind by one room.
        protected override void OnOpen()
        {
            SaveStore.SaveNow();

            int slot = SaveStore.ActiveSlot;
            SaveData data = slot >= 0 ? SaveStore.Get(slot) : null;
            int deaths = data != null ? data.deaths : 0;
            float seconds = data != null ? data.playSeconds : 0f;

            if (deathsText != null) deathsText.text = $"Deaths  {deaths}";
            if (timeText != null) timeText.text = $"Total time  {SaveMenuPanel.FormatDuration(seconds)}";
        }

        private void OnBackClicked()
        {
            if (UIManager.Instance != null) UIManager.Instance.ReturnToMainMenu();
        }
    }
}
