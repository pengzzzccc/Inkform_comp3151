using System.Collections.Generic;
using UnityEngine;

namespace Inkform.Audio
{
    /// <summary>
    /// Pure listener-side zone state: which zones the player is inside (a stack, so overlaps
    /// compose), what the combined target mix is, and the current blend while transitioning.
    /// No MonoBehaviour, no colliders — AudioZone does the physics, AudioManager ticks this with
    /// unscaled time (a hitstop must not freeze a cave fade), EditMode tests drive it directly.
    ///
    /// Overlapping zones combine per parameter, strictest wins: volume scales multiply, cutoffs
    /// take the minimum, wet takes the maximum. Entering/exiting retargets the blend; the blend
    /// time is the entering/leaving zone's own profile setting.
    /// </summary>
    public sealed class ZoneMixer
    {
        private struct Entry
        {
            public object zone;     // the AudioZone instance, as an opaque token
            public ZoneMix mix;
        }

        private readonly List<Entry> stack = new List<Entry>();

        private ZoneMix current = ZoneMix.Neutral;   // what the listener hears right now
        private ZoneMix target = ZoneMix.Neutral;    // where the blend is heading
        private ZoneMix blendFrom;
        private float blendElapsed;
        private float blendDuration;

        public ZoneMix ListenerState => current;
        public bool InZone => stack.Count > 0;

        public void Push(object zone, ZoneMix mix, float blendIn)
        {
            // Re-entry without an exit replaces in place — a doubled entry would stack its effect
            for (int i = 0; i < stack.Count; i++)
            {
                if (!ReferenceEquals(stack[i].zone, zone)) continue;
                stack[i] = new Entry { zone = zone, mix = mix };
                Retarget(blendIn);
                return;
            }
            stack.Add(new Entry { zone = zone, mix = mix });
            Retarget(blendIn);
        }

        public void Pop(object zone, float blendOut)
        {
            for (int i = 0; i < stack.Count; i++)
            {
                if (!ReferenceEquals(stack[i].zone, zone)) continue;
                stack.RemoveAt(i);
                break;
            }
            Retarget(blendOut);
        }

        /// <summary>Scene teardown: drop the stack and snap straight back to open air — the world
        /// the zones described is gone, there is nothing meaningful left to fade from.</summary>
        public void Clear()
        {
            stack.Clear();
            Retarget(0f);
        }

        public void Tick(float dt)
        {
            if (blendDuration <= 0f) return;
            blendElapsed += dt;
            float k = Mathf.Clamp01(blendElapsed / blendDuration);
            current = new ZoneMix(
                Mathf.Lerp(blendFrom.volumeScale, target.volumeScale, k),
                Mathf.Lerp(blendFrom.cutoff, target.cutoff, k),
                Mathf.Lerp(blendFrom.reverbWet, target.reverbWet, k));
        }

        /// <summary>Strictest-wins combination, exposed because AudioManager also uses it to fold
        /// the zones containing a spatial sound's emitter (StateAt).</summary>
        public static ZoneMix Combine(ZoneMix a, ZoneMix b) => new ZoneMix(
            a.volumeScale * b.volumeScale,
            Mathf.Min(a.cutoff, b.cutoff),
            Mathf.Max(a.reverbWet, b.reverbWet));

        private void Retarget(float duration)
        {
            blendFrom = current;
            target = CombineStack();
            blendElapsed = 0f;
            blendDuration = Mathf.Max(0f, duration);
            if (blendDuration <= 0f) current = target;      // instant retarget, no blend
        }

        private ZoneMix CombineStack()
        {
            ZoneMix mix = ZoneMix.Neutral;
            for (int i = 0; i < stack.Count; i++) mix = Combine(mix, stack[i].mix);
            return mix;
        }
    }
}
