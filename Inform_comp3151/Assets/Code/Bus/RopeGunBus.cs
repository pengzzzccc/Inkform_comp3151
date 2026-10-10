using System;
using UnityEngine;

namespace Inkform.Bus
{
    /// <summary>
    /// Rope gun bus: the rope gun announcing its own actions (direction-carrying facts), so any
    /// system — haptics, FX, audio — can react without holding a RopeGun reference. Pure signals,
    /// no snapshot.
    /// </summary>
    public static class RopeGunBus
    {
        /// <summary>Fired: dir = normalized fire direction (always exactly toward the reticle).</summary>
        public static event Action<Vector2> Fired;

        /// <summary>Hit: dir = normalized direction from the player toward the anchor (terrain hit point / carriable).</summary>
        public static event Action<Vector2> Hit;

        /// <summary>Cancelled: the player deliberately broke an active pull (re-pressed fire, dashed,
        /// jumped off the rope). Natural endings — arrival, stuck timeout, death, swallow — do not
        /// raise this; they are not cancellations.</summary>
        public static event Action RopeCancelled;

        /// <summary>Released: an active pull ended on its own — arrived at the anchor, stuck timeout,
        /// or the grabbed carriable could not be swallowed. Not raised for a successful swallow
        /// (ItemBus.ItemStored covers it), death, a destroyed target, or a miss retract.</summary>
        public static event Action RopeReleased;

        public static void RaiseFired(Vector2 dir) => Fired?.Invoke(dir);

        public static void RaiseHit(Vector2 dir) => Hit?.Invoke(dir);

        public static void RaiseRopeCancelled() => RopeCancelled?.Invoke();

        public static void RaiseRopeReleased() => RopeReleased?.Invoke();

        // Static fields do not clear on scene reload; with Domain Reload off, dead subscribers from
        // the previous run linger
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Fired = null;
            Hit = null;
            RopeCancelled = null;
            RopeReleased = null;
        }
    }
}
