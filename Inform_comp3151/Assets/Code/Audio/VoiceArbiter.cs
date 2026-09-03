using System.Collections.Generic;

namespace Inkform.Audio
{
    /// <summary>The stealable facts of one live voice, decoupled from AudioSource so arbitration
    /// can be unit-tested without a running audio engine.</summary>
    public struct VoiceFact
    {
        public CuePriority priority;
        public float startedAt;     // Time.unscaledTime when the voice started
        public bool persistent;     // ambient/music-style loop: stealing it is more noticeable, so
                                    // within the same priority it is stolen only after transients
    }

    /// <summary>
    /// Pure voice arbitration: where the next voice comes from when the pool is empty, and which
    /// live voice to evict when the pool is at its hard cap. Mirrors the middleware model (Wwise
    /// kill-voice / FMOD stealing): capacity first, priority-based eviction second, explicit drop
    /// last — never a silent unbounded grow.
    /// </summary>
    public static class VoiceArbiter
    {
        /// <summary>A voice younger than this has barely been heard; evicting it wastes the same
        /// capacity the evictor is trying to free. Only gates same-priority eviction.</summary>
        public const float MinStealAge = 0.1f;

        public enum Acquire { UsePooled, Grow, Steal }

        /// <summary>Pool still has sources → use one. Otherwise grow up to hardCap (the caller
        /// grows in chunks and re-checks). At hardCap the only way in is stealing a live voice.</summary>
        public static Acquire DecideAcquire(int pooledCount, int liveCount, int hardCap)
        {
            if (pooledCount > 0) return Acquire.UsePooled;
            if (liveCount < hardCap) return Acquire.Grow;
            return Acquire.Steal;
        }

        /// <summary>
        /// Picks the victim index among live voices for an incoming post, or -1 when nothing
        /// qualifies (the caller drops, with a counter and an optional log).
        ///
        /// Reminder: the enum counts downwards — Critical = 0 = most important, so "lower
        /// priority" is the LARGER value. Rules, best class first:
        /// 1. strictly less important than the newcomer is always evictable — the incoming sound
        ///    matters more; among them take the least important, transients before persistent
        ///    loops, oldest first;
        /// 2. otherwise same-priority voices that have been audible ≥ MinStealAge — oldest first,
        ///    transients before persistent. A sound never evicts a peer that just started;
        /// 3. more important than the newcomer is never touched.
        /// </summary>
        public static int PickVictim(IReadOnlyList<VoiceFact> live, CuePriority incoming, float now)
        {
            int best = -1;
            for (int i = 0; i < live.Count; i++)
            {
                CuePriority p = live[i].priority;
                if (p < incoming) continue;                        // rule 3: outranks the newcomer

                if (p > incoming)                                  // rule 1: outranked, always stealable
                {
                    if (best < 0 || live[best].priority < p
                        || (live[best].priority == p && Beats(live[i], live[best])))
                        best = i;
                    continue;
                }

                if (now - live[i].startedAt < MinStealAge) continue;   // rule 2: same lane needs age
                if (best >= 0 && live[best].priority > incoming) continue;   // a rule-1 victim always wins
                if (best < 0 || Beats(live[i], live[best])) best = i;
            }
            return best;
        }

        // "Beats" = the better victim: same priority → transients before persistent → oldest first
        private static bool Beats(VoiceFact candidate, VoiceFact incumbent)
        {
            if (candidate.persistent != incumbent.persistent) return !candidate.persistent;
            return candidate.startedAt < incumbent.startedAt;
        }
    }
}
