using Inkform.Ability;
using Inkform.Fx;
using Inkform.Input;
using UnityEngine;
using UnityEngine.UIElements;

namespace Inkform.UI
{
    /// <summary>
    /// Multi-page image tutorial, opened when a pickup grants an ability (ItemBus.AbilityUnlocked
    /// -> UIManager.OpenTutorial): one page per Sprite in the set matching the ability, PREV/NEXT
    /// step through them, CLOSE (or Escape) leaves. Toolkit restyle of the old TutorialPanel —
    /// dimmed stage, white outlined text, the shared floaty/press interaction.
    ///
    /// While the sheet is up the game is held still: gameplay input is locked and the world is
    /// frozen through scoped owner locks released on every close path, so the click that flips a
    /// page cannot fire the rope gun or jump (same mechanism as before, but the locks' owner is
    /// this panel object and UIManager releases them when it dies).
    ///
    /// Pages come from the TutorialPages asset instead of the old prefab's sprite arrays. The
    /// whole feature still hangs off its showOnPickup flag.
    /// </summary>
    public class TutorialPanel : ToolkitPanel
    {
        private readonly TutorialPages content;
        private readonly InputHandler input;
        private readonly GameTimeController gameTime;
        private readonly Object lockOwner;

        private readonly Button prevButton;
        private readonly Button nextButton;
        private readonly Button closeButton;
        private readonly Label pageLabel;
        private readonly VisualElement pageImage;

        private Sprite[] pages;
        private int index;

        public bool ShowOnPickup => content != null && content.showOnPickup;

        public TutorialPanel(VisualElement root, UIManager ui, TutorialPages content,
            InputHandler input, GameTimeController gameTime, Object lockOwner) : base(root, ui)
        {
            this.content = content;
            this.input = input;
            this.gameTime = gameTime;
            this.lockOwner = lockOwner;

            prevButton = Q<Button>("Btn_Prev");
            nextButton = Q<Button>("Btn_Next");
            closeButton = Q<Button>("Btn_Close");
            pageLabel = Q<Label>("Lbl_Page");
            pageImage = Q<VisualElement>("Page");

            Bind(prevButton, "Btn_Prev", OnPrev);
            Bind(nextButton, "Btn_Next", OnNext);
            Bind(closeButton, "Btn_Close", OnCloseClicked);
        }

        /// <summary>Whether a pickup granting this ability id has pages to show. UIManager gates
        /// the pause + open on this, so an unwired set never freezes the game for a blank sheet.</summary>
        public bool HasPages(string abilityId)
        {
            Sprite[] set = Pages(abilityId);
            return set != null && set.Length > 0;
        }

        /// <summary>Resets to the first page of the set matching the freshly unlocked ability,
        /// then shows the sheet (Open itself — callers have already checked HasPages).</summary>
        public void OpenWith(string abilityId)
        {
            pages = Pages(abilityId);
            index = 0;
            ShowPage();
            Open();
        }

        private Sprite[] Pages(string abilityId)
        {
            if (content == null) return null;
            if (abilityId == AbilityIds.Checkpoint) return content.checkpointPages;
            if (abilityId == AbilityIds.RopeGun) return content.ropeGunPages;
            return null;
        }

        // ---- Stage hold: gameplay input off, world frozen while the sheet is up ----

        protected override void OnOpen() => SetStageHeld(true);

        protected override void OnClose() => SetStageHeld(false);

        /// <summary>Safety net for teardown without a close pass (UIManager dying mid-sheet):
        /// an owner lock keyed by reference must not outlive its owner.</summary>
        protected internal override void Teardown() => SetStageHeld(false);

        private void SetStageHeld(bool held)
        {
            // The scoped locks key their owners by UnityEngine.Object reference (dead owners get
            // pruned), so a plain panel object cannot be the owner — lockOwner is. Explicit null
            // checks, never `?.`: a destroyed Unity object reads non-null to C#'s null-conditional
            // operator and the call would reach a dead component
            if (input != null && lockOwner != null) input.SetGameplayInputLocked(lockOwner, held);
            if (gameTime != null && lockOwner != null) gameTime.SetWorldFrozen(lockOwner, held);
        }

        private void OnPrev()
        {
            if (index <= 0) return;
            index--;
            ShowPage();
        }

        private void OnNext()
        {
            if (pages == null || index >= pages.Length - 1) return;
            index++;
            ShowPage();
        }

        private void OnCloseClicked() => UI.CloseTutorial();

        // End-of-set buttons disable instead of hiding: the footer row never reflows, and
        // FocusFirst skips disabled controls — so Submit always lands on a live control.
        private void ShowPage()
        {
            if (pages == null || pages.Length == 0) return;

            Sprite page = pages[Mathf.Clamp(index, 0, pages.Length - 1)];
            if (pageImage != null) pageImage.style.backgroundImage = new StyleBackground(page);
            if (pageLabel != null) pageLabel.text = $"{index + 1} / {pages.Length}";
            prevButton?.SetEnabled(index > 0);
            nextButton?.SetEnabled(index < pages.Length - 1);
        }
    }
}
