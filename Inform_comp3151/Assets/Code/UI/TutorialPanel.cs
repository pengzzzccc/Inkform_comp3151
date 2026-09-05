using Inkform.Ability;
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
