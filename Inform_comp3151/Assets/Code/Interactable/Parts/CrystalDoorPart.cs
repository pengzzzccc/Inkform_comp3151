using Inkform.Player;
using Inkform.Progression;
using UnityEngine;

namespace Inkform.Interactable.Parts
{
    /// <summary>Atomically spends crystals and permanently opens one stable-ID door.</summary>
    public sealed class CrystalDoorPart : PlayerInteractablePart
    {
        [Header("Crystal door")]
        [SerializeField] private string doorId;
        [SerializeField, Min(1)] private int crystalCost = 1;
        [SerializeField] private Collider2D blockingCollider;
        [SerializeField] private Animator animator;
        [SerializeField] private string openParameter = "Open";

        protected override void OnEnable()
        {
            base.OnEnable();
            RunProgressStore.Changed += Refresh;
        }

        protected override void OnDisable()
        {
            RunProgressStore.Changed -= Refresh;
            base.OnDisable();
        }

        public override void Attach(Interactable root)
        {
            base.Attach(root);
            if (blockingCollider == null) blockingCollider = root.GetComponent<Collider2D>();
            if (animator == null) animator = root.GetComponentInChildren<Animator>();
            Refresh();
        }

        protected override string GetActionText(PlayerHandler player) => IsOpen
            ? string.Empty
            : $"Activate Door ({crystalCost} Crystals)";

        protected override bool CanInteract(PlayerHandler player, out string unavailableReason)
        {
            if (!base.CanInteract(player, out unavailableReason)) return false;
            if (IsOpen) return false;
            if (string.IsNullOrWhiteSpace(doorId))
            {
                unavailableReason = "Door is not configured";
                return false;
            }
            if (RunProgressStore.CrystalCount >= crystalCost) return true;
            unavailableReason = $"Need {crystalCost - RunProgressStore.CrystalCount} more crystal(s)";
            return false;
        }

        protected override bool PerformInteraction(PlayerHandler player)
        {
            bool opened = RunProgressStore.TryActivateDoor(doorId, crystalCost);
            Refresh();
            return opened;
        }

        private bool IsOpen => RunProgressStore.IsDoorActivated(doorId);

        private void Refresh()
        {
            bool open = IsOpen;
            if (blockingCollider != null) blockingCollider.enabled = !open;
            if (animator != null && animator.runtimeAnimatorController != null
                && !string.IsNullOrWhiteSpace(openParameter))
                animator.SetBool(openParameter, open);
        }

        private void OnValidate()
        {
            crystalCost = Mathf.Max(1, crystalCost);
            if (string.IsNullOrWhiteSpace(doorId)) return;
            doorId = doorId.Trim();
        }
    }
}
