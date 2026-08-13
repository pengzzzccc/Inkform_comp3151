using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Inkform.UI
{
    /// <summary>
    /// Save menu sheet: three save slots (each shows "No Records" for now) flanked by left/right
    /// arrows. Saving is a stub — slots always display "No Records" and any slot starts a fresh run
    /// via UIManager.StartNewGame.
    ///
    /// Buttons discovered by name: Btn_Slot0..Btn_Slot2, Btn_Left / Btn_Right (arrows, disabled stub).
    ///
    /// No Back button by design (the sheet's mockup has none): Escape backs out of this sheet, which
    /// UIManager.Update already handles ahead of the pause/main-menu cases.
    /// </summary>
    public class SaveMenuPanel : BasePanel
    {
        [SerializeField] private Button[] slotButtons;
        [SerializeField] private Button leftArrow;
        [SerializeField] private Button rightArrow;

        protected override void Awake()
        {
            base.Awake();
            if (slotButtons == null || slotButtons.Length == 0) slotButtons = FindSlotButtons();
            if (leftArrow == null) leftArrow = FindButton("Btn_Left");
            if (rightArrow == null) rightArrow = FindButton("Btn_Right");

            if (slotButtons.Length == 0)
                Debug.LogWarning($"{name}: no Btn_Slot0..2 among the panel's direct children — no way to start a game from here", this);

            for (int i = 0; i < slotButtons.Length; i++) Bind(slotButtons[i], $"Btn_Slot{i}", OnSlotPressed);

            // Arrows are a placeholder: no save paging exists yet, keep them visible but inert.
            if (leftArrow != null) leftArrow.interactable = false;
            if (rightArrow != null) rightArrow.interactable = false;
        }

        private void OnSlotPressed()
        {
            // Stub: any slot starts a fresh run. A real implementation would load that slot's save.
            UIManager.Instance.StartNewGame();
        }

        private Button[] FindSlotButtons()
        {
            var found = new List<Button>();
            for (int i = 0; i < 3; i++)
            {
                Button b = FindButton($"Btn_Slot{i}");
                if (b != null) found.Add(b);
            }
            return found.ToArray();
        }
    }
}
