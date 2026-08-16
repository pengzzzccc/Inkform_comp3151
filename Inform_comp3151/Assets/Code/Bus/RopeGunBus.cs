using System;
using UnityEngine;

namespace Inkform.Bus
{
    /// <summary>
    /// Rope gun bus: signals for "some other system must notify the rope gun", like range overrides.
    /// Pure signal, no snapshot — an override is instantaneous; the rope gun tracks its own state.
    /// RopeRangeZone raises RangeOverride/RangeRestored.
    /// Fired / Hit are the rope gun announcing its own actions (direction-carrying facts), so any
    /// system — haptics, FX, audio — can react without holding a RopeGun reference.
    /// </summary>
    public static class RopeGunBus
    {
        /// <summary>Range overridden (entered a special zone): range = max range the zone specifies.</summary>
        public static event Action<float> RangeOverride;

        /// <summary>Range override lifted (left the zone): the rope gun restores its own default range.</summary>
        public static event Action RangeRestored;

        /// <summary>Fired: dir = normalized fire direction (always exactly toward the reticle).</summary>
        public static event Action<Vector2> Fired;

        /// <summary>Hit: dir = normalized direction from the player toward the anchor (terrain hit point / carriable).</summary>
        public static event Action<Vector2> Hit;

        public static void RaiseRangeOverride(float range) => RangeOverride?.Invoke(range);

        public static void RaiseRangeRestored() => RangeRestored?.Invoke();

        public static void RaiseFired(Vector2 dir) => Fired?.Invoke(dir);

        public static void RaiseHit(Vector2 dir) => Hit?.Invoke(dir);

        // Static fields do not clear on scene reload; with Domain Reload off, dead subscribers from
        // the previous run linger
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            RangeOverride = null;
            RangeRestored = null;
            Fired = null;
            Hit = null;
        }
    }
}
