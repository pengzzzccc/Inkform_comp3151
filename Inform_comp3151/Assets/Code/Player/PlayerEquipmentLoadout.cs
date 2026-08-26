using Inkform.Progression;
using UnityEngine;

namespace Inkform.Player
{
    [RequireComponent(typeof(RopeGun))]
    public sealed class PlayerEquipmentLoadout : MonoBehaviour
    {
        private RopeGun ropeGun;

        private void Awake() => ropeGun = GetComponent<RopeGun>();
        private void OnEnable()
        {
            EquipmentProgressStore.Changed += Refresh;
            Refresh();
        }

        private void OnDisable() => EquipmentProgressStore.Changed -= Refresh;
        private void Refresh() => ropeGun?.SetEquipped(EquipmentProgressStore.RopeGunUnlocked);
    }
}
