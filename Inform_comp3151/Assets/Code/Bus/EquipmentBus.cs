using System;
using UnityEngine;

namespace Inkform.Bus
{
    /// <summary>One-shot equipment acquisition facts for feedback systems.</summary>
    public static class EquipmentBus
    {
        public static event Action RopeGunAcquired;

        public static void RaiseRopeGunAcquired() => RopeGunAcquired?.Invoke();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => RopeGunAcquired = null;
    }
}
