using System;
using UnityEngine;

namespace Inkform.Bus
{
    /// <summary>
    /// FX bus: command-style (not fact-style) — publishers say "give me a shake", nobody cares who
    /// shakes or how. Explosions therefore need no camera reference, and the camera does not know explosions exist.
    /// How strong an effect each game event deserves is translated uniformly by FxDirector.
    /// </summary>
    public static class FxBus
    {
        /// <summary>Screen shake: amount is a trauma increment (0~1); repeated requests accumulate rather than reset, decay speed is set by the camera.</summary>
        public static event Action<float> ShakeRequested;

        /// <summary>Hitstop: how long timeScale is zeroed, in unscaled seconds.</summary>
        public static event Action<float> HitStopRequested;

        /// <summary>Camera zoom punch: amount = instantaneous orthographicSize delta (negative = push in), duration = settle time.</summary>
        public static event Action<float, float> ZoomRequested;

        /// <summary>Post-processing punch: amount = vignette strength increment (0~1), duration = decay time.</summary>
        public static event Action<float, float> PunchRequested;

        /// <summary>Camera snaps onto the target immediately, no smoothing. Must be raised after the player
        /// teleports (respawn), or the camera drags its follow inertia all the way from the old position.</summary>
        public static event Action SnapRequested;

        public static void RaiseShake(float amount)
        {
            ShakeRequested?.Invoke(amount);
        }

        public static void RaiseHitStop(float duration)
        {
            HitStopRequested?.Invoke(duration);
        }

        public static void RaiseZoom(float amount, float duration)
        {
            ZoomRequested?.Invoke(amount, duration);
        }

        public static void RaisePunch(float amount, float duration)
        {
            PunchRequested?.Invoke(amount, duration);
        }

        public static void RaiseSnap()
        {
            SnapRequested?.Invoke();
        }

        // Static fields do not clear on scene reload; with Domain Reload off, dead subscribers from
        // the previous run linger
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            ShakeRequested = null;
            HitStopRequested = null;
            ZoomRequested = null;
            PunchRequested = null;
            SnapRequested = null;
        }
    }
}
