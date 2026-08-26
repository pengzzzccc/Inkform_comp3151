using Inkform.Player;
using Inkform.Progression;

namespace Inkform.Interactable.Parts
{
    /// <summary>Active E/Y pickup that permanently unlocks the Rope Gun for the current save slot.</summary>
    public sealed class RopeGunPickupPart : PlayerInteractablePart
    {
        protected override void OnEnable()
        {
            base.OnEnable();
            EquipmentProgressStore.Changed += Refresh;
        }

        protected override void OnDisable()
        {
            EquipmentProgressStore.Changed -= Refresh;
            base.OnDisable();
        }

        private void Start() => Refresh();

        protected override string GetActionText(PlayerHandler player) =>
            EquipmentProgressStore.RopeGunUnlocked ? string.Empty : "Pick Up Rope Gun";

        protected override bool CanInteract(PlayerHandler player, out string unavailableReason)
        {
            if (!base.CanInteract(player, out unavailableReason)) return false;
            return !EquipmentProgressStore.RopeGunUnlocked;
        }

        protected override bool PerformInteraction(PlayerHandler player)
        {
            if (!EquipmentProgressStore.TryUnlockRopeGun()) return false;
            Refresh();
            return true;
        }

        private void Refresh()
        {
            if (Root != null && EquipmentProgressStore.RopeGunUnlocked)
                Root.gameObject.SetActive(false);
        }
    }
}
