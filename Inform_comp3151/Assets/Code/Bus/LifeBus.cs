using Inkform.Life;
using Inkform.Tool;
using System;
using UnityEngine;

namespace Inkform.Bus
{
    /// <summary>
    /// Life bus: death / respawn / checkpoints. Death is an instantaneous signal, while death count and
    /// "currently dead?" are persistent state — so it raises events and stores snapshots — like PlayerBus,
    /// the bus itself handles dedup.
    /// The deceased claims itself via ctx.Victim; the publisher (Spike) never needs to know the player,
    /// and the player never needs to know the Spike.
    /// </summary>
    public static class LifeBus
    {
        /// <summary>Death: ctx carries the victim, the killing point, and the cause.
        /// Only the player dies, and each death raises exactly once, so overall feedback like
        /// screen shake / sound can listen to this directly — no need for the two-layer
        /// Exploded (per-victim) / Blast (overall) split like HazardBus.
        /// "How this death should play out" is decided by DeathDirector selecting a strategy per
        /// ctx.Cause; this bus does not care.</summary>
        public static event Action<DeathContext> Died;

        /// <summary>Respawn: victim = the one respawning, pos = respawn position.</summary>
        public static event Action<GameObject, Vector2> Respawned;

        /// <summary>A checkpoint was activated: pos = the respawn position from now on.</summary>
        public static event Action<Vector2> CheckpointSet;

        // Current snapshot: subscribers may read anytime instead of tracking their own copy
        public static int DeathCount { get; private set; }
        public static bool IsDead { get; private set; }

        public static void RaiseDied(in DeathContext ctx)
        {
#if UNITY_EDITOR
            // F3 invincibility: the hit, knockback and explosion all still happen — the death
            // itself never does. Single choke point; no hazard needs to know the cheat exists
            if (DebugCheats.Invincible) return;
#endif
            // Dedup: several spikes may hit the player in the same frame; without this, the count
            // doubles and fragments spawn double (same reason as HazardBus.Exploded — that one
            // guards against being called twice, this one against multiple sources)
            if (IsDead) return;
            IsDead = true;
            DeathCount++;

            Died?.Invoke(ctx);
        }

        public static void RaiseRespawned(GameObject victim, Vector2 pos)
        {
            IsDead = false;
            Respawned?.Invoke(victim, pos);
        }

        public static void RaiseCheckpointSet(Vector2 pos)
        {
            CheckpointSet?.Invoke(pos);
        }

        // Static fields do not clear on scene reload; with Domain Reload off, dead subscribers from
        // the previous run linger
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Died = null;
            Respawned = null;
            CheckpointSet = null;
            // The count must clear too: the ScriptableObject "leftover from previous run" pitfall
            // exists for static fields as well
            DeathCount = 0;
            IsDead = false;
        }
    }
}
