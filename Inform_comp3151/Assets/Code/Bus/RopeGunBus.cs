using System;
using UnityEngine;

namespace Inkform.Bus
{
    /// <summary>
    /// Rope gun bus: signals for "some other system must notify the rope gun", like range overrides.
    /// Pure signal, no snapshot — an override is instantaneous; the rope gun tracks its own state.
    /// RopeRangeZone raises RangeOverride/RangeRestored.
    /// </summary>
    public static class RopeGunBus
    {
        /// <summary>Range overridden (entered a special zone): range = max range the zone specifies.</summary>
        public static event Action<float> RangeOverride;

        /// <summary>Range override lifted (left the zone): the rope gun restores its own default range.</summary>
        public static event Action RangeRestored;

        public static void RaiseRangeOverride(float range) => RangeOverride?.Invoke(range);

        public static void RaiseRangeRestored() => RangeRestored?.Invoke();

        // Static fields do not clear on scene reload; with Domain Reload off, dead subscribers from
        // the previous run linger
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            RangeOverride = null;
            RangeRestored = null;
        }
    }
}
