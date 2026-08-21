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
            RunProgressStore.Changed += Refresh;
            Refresh();
        }

        private void OnDisable() => RunProgressStore.Changed -= Refresh;
        private void Refresh() => ropeGun?.SetEquipped(RunProgressStore.RopeGunUnlocked);
    }
}
