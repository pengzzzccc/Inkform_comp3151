using System;
using UnityEngine;

namespace Inkform.Bus
{
    /// <summary>
    /// Hazard bus: area-effect events like explosions are published by the hazard itself; those affected
    /// claim themselves. Pure signal style (no snapshot) — an explosion is instantaneous, there is no
    /// "current state" to speak of.
    /// </summary>
    public static class HazardBus
    {
        /// <summary>Explosion event: victim = affected object, center = blast center, force = push strength.</summary>
        public static event Action<GameObject, Vector2, float> Exploded;

        /// <summary>An explosion happened (overall, exactly once per explosion): center = blast center, radius = blast radius, force = push strength.
        /// Distinct from Exploded — that one fires per victim: N victims = N raises, zero victims = 0 raises,
        /// so overall feedback like "one shake per explosion" must listen to Blast.</summary>
        public static event Action<Vector2, float, float> Blast;

        /// <summary>A breakable was shattered (once per shattered object): center = center of the shattered object.
        /// Distinct from Blast — Blast means "an explosion occurred", this means "something got shattered".</summary>
        public static event Action<Vector2> Broken;

        /// <summary>A hazard's warning frame advanced by one: pos = position, step = new frame index, total = total frames.
        /// Proximity warning and fuse countdown share this signal — both are discrete steps of "how close to exploding".</summary>
        public static event Action<Vector2, int, int> Ticked;

        public static void RaiseExploded(GameObject victim, Vector2 center, float force)
        {
            Exploded?.Invoke(victim, center, force);
        }

        public static void RaiseBlast(Vector2 center, float radius, float force)
        {
            Blast?.Invoke(center, radius, force);
        }

        public static void RaiseBroken(Vector2 center)
        {
            Broken?.Invoke(center);
        }

        public static void RaiseTicked(Vector2 pos, int step, int total)
        {
            Ticked?.Invoke(pos, step, total);
        }

        // Static fields do not clear on scene reload; with Domain Reload off, dead subscribers from
        // the previous run linger
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Exploded = null;
            Blast = null;
            Broken = null;
            Ticked = null;
        }
    }
}
