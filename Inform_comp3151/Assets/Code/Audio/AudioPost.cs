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
    /// One sound request handed to AudioManager: the Cue, an optional emitter position, and the
    /// lane it competes in (always the Cue's own). The game-facing Play(cue, position) is a thin
    /// wrapper over this.
    /// </summary>
    public readonly struct AudioPost
    {
        public readonly SoundCue cue;
        public readonly Vector3? position;
        public readonly CuePriority priority;

        public AudioPost(SoundCue cue, Vector3? position = null)
        {
            this.cue = cue;
            this.position = position;
            this.priority = cue != null ? cue.priority : CuePriority.Gameplay;
        }
    }
}
