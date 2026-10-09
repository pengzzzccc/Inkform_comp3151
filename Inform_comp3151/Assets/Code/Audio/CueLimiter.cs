using System.Collections.Generic;

namespace Inkform.Audio
{
    /// <summary>
    /// Pure playback gate for one-shots: per-Cue cooldown and concurrency, plus which busy voice a
    /// new post may take over. No AudioSource access, so EditMode tests cover every rule.
    ///
    /// The per-Cue state lives here — on the AudioService instance — never on the SoundCue asset:
    /// an asset outlives play mode in the editor, and state stored on it leaks into the next run.
    /// </summary>
    public sealed class CueLimiter
    {
        private struct CueState
        {
            public float lastStart;
            public int active;
        }

        private readonly Dictionary<SoundCue, CueState> states = new Dictionary<SoundCue, CueState>();

        /// <summary>Facts about one busy voice, enough to rank it as a takeover candidate.</summary>
        public struct VoiceFact
        {
            public CuePriority priority;
            public float startedAt;
        }

        /// <summary>
        /// Whether the Cue may start now (cooldown elapsed and under its concurrency cap). Does not
        /// count the start — call Started once a voice actually plays it. Critical Cues always pass.
        /// </summary>
        public bool CanStart(SoundCue cue, float now)
        {
            if (cue.priority == CuePriority.Critical) return true;
            if (!states.TryGetValue(cue, out CueState state)) return true;
            if (now - state.lastStart < cue.cooldown) return false;
            return state.active < cue.maxConcurrent;
        }

        public void Started(SoundCue cue, float now)
        {
            states.TryGetValue(cue, out CueState state);
            state.lastStart = now;
            state.active++;
            states[cue] = state;
        }

        /// <summary>A voice carrying this Cue finished or was taken over.</summary>
        public void Released(SoundCue cue)
        {
            if (cue == null || !states.TryGetValue(cue, out CueState state)) return;
            state.active = state.active > 0 ? state.active - 1 : 0;
            states[cue] = state;
        }

        public int ActiveCount(SoundCue cue) =>
            cue != null && states.TryGetValue(cue, out CueState state) ? state.active : 0;

        /// <summary>
        /// Picks the busy voice a new post takes over, or -1 when none qualifies (the post is
        /// dropped). Only voices of the same or a less important lane are candidates — the enum
        /// counts downwards, Critical = 0 = most important — and among them the least important
        /// lane goes first, then the oldest.
        /// </summary>
        public static int PickVictim(IReadOnlyList<VoiceFact> busy, CuePriority incoming)
        {
            int best = -1;
            for (int i = 0; i < busy.Count; i++)
            {
                VoiceFact v = busy[i];
                if (v.priority < incoming) continue;    // more important than the newcomer: untouchable
                if (best < 0
                    || v.priority > busy[best].priority
                    || (v.priority == busy[best].priority && v.startedAt < busy[best].startedAt))
                    best = i;
            }
            return best;
        }
    }
}
