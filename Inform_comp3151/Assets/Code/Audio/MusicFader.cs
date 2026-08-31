using UnityEngine;

namespace Inkform.Audio
{
    /// <summary>
    /// Pure crossfade state machine for the two dedicated music channels. Owns no AudioSource —
    /// MusicPlayer ticks it with a clock (unscaled: menus and pauses must not stall a fade) and
    /// applies the emitted volumes to real sources. Fades are linear ramps between beds; the
    /// curve is not the interesting part, the routing is.
    ///
    /// Each channel tracks its own cue, volume, target and rate; "the current track" is simply
    /// whichever channel targets full volume (at most one). Requesting the current track is a
    /// no-op (re-entering a scene must not restart its music); requesting the track that is
    /// fading out reverses the fade in place — same sources, same clips, only the targets swap.
    /// </summary>
    public sealed class MusicFader
    {
        private struct Channel
        {
            public SoundCue cue;    // what this channel carries; null = idle
            public float volume;    // current fade 0..1 (cue/settings volume applied by the player)
            public float target;    // 1 = the audible role, 0 = fading away
            public float rate;      // fade units per second; PositiveInfinity = instant
        }

        private Channel ch0, ch1;

        public SoundCue Current => ch0.target > 0.5f ? ch0.cue : (ch1.target > 0.5f ? ch1.cue : null);

        /// <summary>
        /// Requests a track (null = stop the music). Returns the slot (0/1) whose source must be
        /// given the new clip, or -1 when there is nothing to do: same track already playing,
        /// the outgoing track re-requested (in-place fade reversal), or a stop with nothing
        /// playing.
        /// </summary>
        public int Request(SoundCue cue, float fadeSeconds)
        {
            float rate = fadeSeconds > 0f ? 1f / fadeSeconds : float.PositiveInfinity;
            int current = CurrentSlot;
            int other = current < 0 ? QuieterSlot : 1 - current;

            if (cue != null)
            {
                if (current >= 0 && CueOf(current) == cue) return -1;       // already the track
                if (CueOf(other) == cue && VolumeOf(other) > 0f)
                {
                    // Fade reversal: the outgoing track is wanted back — swap the roles in place,
                    // same physical sources, no clip change for the caller
                    if (current >= 0) SetTarget(current, 0f, rate);
                    SetTarget(other, 1f, rate);
                    return -1;
                }
            }
            else if (current < 0)
            {
                return -1;      // stopping while silent is a no-op
            }

            // Whatever is playing fades under the change (its clip stays until Tick reports the
            // fade finished); the new track fades in from silence on the other physical source
            if (current >= 0) SetTarget(current, 0f, rate);
            if (cue != null)
            {
                SetChannel(other, new Channel { cue = cue, volume = 0f, target = 1f, rate = rate });
                return other;
            }
            return -1;          // a stop only needs the outgoing fade
        }

        /// <summary>Fades whatever is playing down to silence over fadeSeconds; Current becomes
        /// null immediately (silence is the target, the fade is just the ride down).</summary>
        public void Stop(float fadeSeconds) => Request(null, fadeSeconds);

        /// <summary>Advances both fades by dt and reports each slot's fade volume plus which
        /// slots just finished fading a clip out (their sources should stop and free it).</summary>
        public void Tick(float dt, out float slot0Volume, out float slot1Volume,
            out bool slot0Stop, out bool slot1Stop)
        {
            Advance(ref ch0, dt, out slot0Volume, out slot0Stop);
            Advance(ref ch1, dt, out slot1Volume, out slot1Stop);
        }

        private static void Advance(ref Channel ch, float dt, out float volume, out bool stop)
        {
            // An infinite rate with dt == 0 would be NaN — snap the instant fades instead
            ch.volume = float.IsPositiveInfinity(ch.rate) ? ch.target : Mathf.MoveTowards(ch.volume, ch.target, ch.rate * dt);
            volume = ch.volume;
            stop = false;
            // A cue that fully faded away reports stop exactly once so its source frees the clip;
            // the channel then idles and never signals again
            if (ch.cue != null && ch.target <= 0f && ch.volume <= 0f)
            {
                stop = true;
                ch.cue = null;
            }
        }

        private int CurrentSlot => ch0.target > 0.5f ? 0 : (ch1.target > 0.5f ? 1 : -1);

        // Both channels idle: the quieter one (equal by symmetry, then slot 0) takes the new
        // track, so a fresh start never lands on a source still ringing out
        private int QuieterSlot => ch0.volume <= ch1.volume ? 0 : 1;

        private SoundCue CueOf(int slot) => slot == 0 ? ch0.cue : ch1.cue;

        private float VolumeOf(int slot) => slot == 0 ? ch0.volume : ch1.volume;

        private void SetTarget(int slot, float target, float rate)
        {
            Channel ch = slot == 0 ? ch0 : ch1;
            ch.target = target;
            ch.rate = rate;
            SetChannel(slot, ch);
        }

        private void SetChannel(int slot, Channel ch)
        {
            if (slot == 0) ch0 = ch; else ch1 = ch;
        }
    }
}
