using Inkform.Player;
using Inkform.Progression;
using UnityEngine;

namespace Inkform.Interactable.Parts
{
    /// <summary>Consumes one shared crystal per interaction; the PlayerInteractor owns hold-repeat timing.</summary>
    public sealed class CrystalIntakePart : PlayerInteractablePart
    {
        [Header("Crystal intake")]
        [SerializeField, Min(1)] private int targetCharge = 8;
        [SerializeField] private Animator animator;
        [SerializeField] private string depositTrigger = "Deposit";

        public override bool RepeatWhileHeld => true;
        public int TargetCharge => targetCharge;
        public bool IsCharged => RunProgressStore.ControlRoomCharge >= targetCharge;

        protected override string GetActionText(PlayerHandler player) => IsCharged
            ? "Intake Fully Charged"
            : $"Deposit Crystal ({RunProgressStore.ControlRoomCharge}/{targetCharge})";

        protected override bool CanInteract(PlayerHandler player, out string unavailableReason)
        {
            if (!base.CanInteract(player, out unavailableReason)) return false;
            if (IsCharged)
            {
                unavailableReason = "Charging complete";
                return false;
            }
            if (RunProgressStore.CrystalCount > 0) return true;
            unavailableReason = "Not Enough Crystals";
            return false;
        }

        protected override bool PerformInteraction(PlayerHandler player)
        {
            if (!RunProgressStore.TryDepositOne(targetCharge)) return false;
            if (animator != null && animator.runtimeAnimatorController != null
                && !string.IsNullOrWhiteSpace(depositTrigger))
                animator.SetTrigger(depositTrigger);
            return true;
        }

        private void OnValidate() => targetCharge = Mathf.Max(1, targetCharge);
    }
}
