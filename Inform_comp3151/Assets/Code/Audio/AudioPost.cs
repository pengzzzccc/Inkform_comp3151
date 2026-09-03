using UnityEngine;

namespace Inkform.Audio
{
    /// <summary>
    /// Which lane a sound competes in when the pool runs dry. Critical (death/game-over feedback) may
    /// bypass cooldown and concurrency caps and steal lower lanes; Ambience loops are stolen first.
    /// Mapped onto AudioSource.priority (0 = highest) so Unity's built-in virtualization ranks voices
    /// the same way the pool's own arbitration does — the two systems must never disagree.
    /// </summary>
    public enum CuePriority { Critical = 0, Gameplay = 128, Ambience = 224 }

    /// <summary>
    /// One sound request handed to AudioManager. The game-facing Play(cue) is a thin wrapper over
    /// this; callers that need the extra knobs (spatial position, a priority bump, zone bypass)
    /// build a Post instead of growing Play's parameter list — Play's signature is a compatibility
    /// surface the existing callers must not be forced to touch.
    /// </summary>
    public readonly struct AudioPost
    {
        public readonly SoundCue cue;
        public readonly Vector3? position;
        public readonly CuePriority priority;
        /// <summary>Skips zone mixing entirely (no muffle/wet/volume scale). Cues flagged
        /// ignoreListenerPause default to this: both flags mark "system feedback, not part of the
        /// world", so a menu click must not go dull inside a cave.</summary>
        public readonly bool ignoreZone;

        public AudioPost(SoundCue cue, Vector3? position = null, CuePriority? priority = null, bool ignoreZone = false)
        {
            this.cue = cue;
            this.position = position;
            // Priority falls back to the Cue's own unless the caller overrides it for this one post
            // (e.g. the same explosion Cue promoted to Critical during a death sequence)
            this.priority = priority != null ? priority.Value
                : cue != null ? cue.priority : CuePriority.Gameplay;
            this.ignoreZone = ignoreZone;
        }
    }
}
