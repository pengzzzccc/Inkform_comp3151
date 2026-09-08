using Inkform.Ability;
using Inkform.Fx;
using Inkform.Input;
using UnityEngine;
using UnityEngine.UI;

namespace Inkform.UI
{
    /// <summary>
    /// Multi-page image tutorial, opened when a pickup grants an ability (ItemBus.AbilityUnlocked ->
    /// UIManager.OpenTutorial): one page per Sprite in the set matching the ability, "&lt;" / "&gt;" step
    /// through them, Close (or Escape) leaves. Pages are plain images — no text layout — so authoring
    /// a tutorial is just dragging screenshots into the page arrays on this component.
    ///
    /// While the sheet is up the game is held still: gameplay input is locked and the world is frozen
    /// (see OnOpen). Only the UI layer keeps working, so the buttons stay clickable — and the click
    /// that flips a page can no longer fire the rope gun or jump, nor can a hazard kill the player
    /// mid-read. Both are scoped owner locks, released on every close path.
    ///
    /// Two page sets, keyed by the granted ability id (AbilityIds.Checkpoint from the timecard,
    /// AbilityIds.RopeGun from the rope gun card). Controls are found by name like every built
    /// panel: Btn_Prev / Btn_Next / Btn_Close, Lbl_Page (the "1 / 2" counter), Page (the Image).
    ///
    /// The whole feature hangs off ShowOnPickup — unchecking it on the Tutorial prefab silences
    /// every pickup's tutorial without touching any other code; the abilities still unlock.
    /// </summary>
    public class TutorialPanel : BasePanel
    {
        [Header("Tutorial")]
        [Tooltip("Unchecked = pickups never open this tutorial (they still unlock as usual)")]
        [SerializeField] private bool showOnPickup = true;

        [Tooltip("Pages shown after picking up the timecard (checkpoint ability). One sprite = one page")]
        [SerializeField] private Sprite[] checkpointPages;
        [Tooltip("Pages shown after picking up the rope gun card. One sprite = one page")]
        [SerializeField] private Sprite[] ropeGunPages;

        private Sprite[] pages;
        private int index;

        private Button prevButton;
        private Button nextButton;
        private Button closeButton;
        private Text pageLabel;
        private Image pageImage;

        // Resolved once when the sheet opens, from the persistent GameManager above the panel
        // (UI Canvas -> GameManager); released through the same cached references.
        private InputHandler input;
        private GameTimeController gameTime;

        public bool ShowOnPickup => showOnPickup;

        protected override void Awake()
        {
            base.Awake();

            if (prevButton == null) prevButton = FindButton("Btn_Prev");
            if (nextButton == null) nextButton = FindButton("Btn_Next");
            if (closeButton == null) closeButton = FindButton("Btn_Close");
            if (pageLabel == null) pageLabel = FindText("Lbl_Page");
            if (pageImage == null)
            {
                GameObject page = FindChild("Page");
                if (page != null) pageImage = page.GetComponent<Image>();
            }

            Bind(prevButton, "Btn_Prev", OnPrev);
            Bind(nextButton, "Btn_Next", OnNext);
            Bind(closeButton, "Btn_Close", OnCloseClicked);
        }

        /// <summary>Whether a pickup granting this ability id has pages to show. UIManager gates the
        /// pause + open on this, so an unwired set never freezes the game for a blank sheet.</summary>
        public bool HasPages(string abilityId)
        {
            Sprite[] set = Pages(abilityId);
            return set != null && set.Length > 0;
        }

        /// <summary>Resets to the first page of the set matching the freshly unlocked ability, then
        /// shows the sheet (Open itself — callers have already checked HasPages).</summary>
        public void OpenWith(string abilityId)
        {
            pages = Pages(abilityId);
            index = 0;
            ShowPage();
            Open();
        }

        private Sprite[] Pages(string abilityId)
        {
            if (abilityId == AbilityIds.Checkpoint) return checkpointPages;
            if (abilityId == AbilityIds.RopeGun) return ropeGunPages;
            return null;
        }

        // ---- Stage hold: gameplay input off, world frozen while the sheet is up ----

        // Scoped owner locks rather than UIManager.SetPaused: resuming a pause menu cannot clear
        // them, and the world freeze keeps the music and ambience playing (only a real user pause
        // silences the AudioListener). Opening here rather than in UIManager.OpenTutorial covers
        // every close path — the sheet is also closed directly by ApplySceneState on a scene load.
        protected override void OnOpen()
        {
            if (input == null) input = GetComponentInParent<InputHandler>();
            if (gameTime == null) gameTime = GetComponentInParent<GameTimeController>();
            SetStageHeld(true);
        }

        protected override void OnClose() => SetStageHeld(false);

        // A destroyed sheet must not keep the gate shut: an owner lock is keyed by reference, so a
        // lock outliving its owner would disable gameplay input forever.
        private void OnDestroy() => SetStageHeld(false);

        private void SetStageHeld(bool held)
        {
            // Explicit null checks, never `?.`: a destroyed Unity object reads non-null to C#'s
            // null-conditional operator and the call would reach a dead component
            if (input != null) input.SetGameplayInputLocked(this, held);
            if (gameTime != null) gameTime.SetWorldFrozen(this, held);
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

        private void OnCloseClicked()
        {
            if (UIManager.Instance != null) UIManager.Instance.CloseTutorial();
        }

        // End-of-set buttons grey out instead of hiding: the control row never reflows, and Select-
        // FirstControl skips non-interactable buttons — so Escape's Submit path (focus + activate)
        // always lands on a live control, never on a dead one.
        private void ShowPage()
        {
            if (pages == null || pages.Length == 0) return;

            Sprite page = pages[Mathf.Clamp(index, 0, pages.Length - 1)];
            if (pageImage != null) pageImage.sprite = page;
            if (pageLabel != null) pageLabel.text = $"{index + 1} / {pages.Length}";
            if (prevButton != null) prevButton.interactable = index > 0;
            if (nextButton != null) nextButton.interactable = index < pages.Length - 1;
        }
    }
}
