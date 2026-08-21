using Inkform.Player;
using Inkform.Progression;

namespace Inkform.Interactable.Parts
{
    /// <summary>Reusable active pickup for permanent run rewards such as the Rope Gun.</summary>
    public sealed class ProgressionPickupPart : PlayerInteractablePart
    {
        [UnityEngine.SerializeField] private ProgressionRewardType rewardType;

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

        private void Start() => Refresh();

        protected override string GetActionText(PlayerHandler player)
        {
            if (HasReward()) return string.Empty;
            return rewardType == ProgressionRewardType.RopeGun
                ? "Pick Up Rope Gun"
                : "Pick Up Elevator Controller";
        }

        protected override bool CanInteract(PlayerHandler player, out string unavailableReason)
        {
            if (!base.CanInteract(player, out unavailableReason)) return false;
            return !HasReward();
        }

        protected override bool PerformInteraction(PlayerHandler player)
        {
            if (!RunProgressStore.GrantReward(rewardType)) return false;
            Refresh();
            return true;
        }

        private bool HasReward() => rewardType == ProgressionRewardType.RopeGun
            ? RunProgressStore.RopeGunUnlocked
            : RunProgressStore.ElevatorControllerAcquired;

        private void Refresh()
        {
            if (Root != null && HasReward()) Root.gameObject.SetActive(false);
        }
    }
}
