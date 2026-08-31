using UnityEngine;

namespace Inkform.Audio
{
    /// <summary>
    /// What one audio zone does to a sound passing through it: scales volume, caps the low-pass
    /// cutoff, adds reverb wet. Neutral is "no zone" (nothing changed). Used both for the
    /// listener's blended state (ZoneMixer) and for per-source lookups, so all combining rules
    /// live next to the struct they manipulate.
    /// </summary>
    public readonly struct ZoneMix
    {
        public readonly float volumeScale;
        public readonly float cutoff;
        public readonly float reverbWet;

        public ZoneMix(float volumeScale, float cutoff, float reverbWet)
        {
            this.volumeScale = volumeScale;
            this.cutoff = cutoff;
            this.reverbWet = reverbWet;
        }

        public static readonly ZoneMix Neutral =
            new ZoneMix(1f, AudioPremix.FullBandwidth, 0f);
    }

    /// <summary>
    /// Pure parameter premix: turns (Cue, distance, settings volumes, zone states) into the three
    /// numbers a voice needs — volume, low-pass cutoff, reverb wet. No AudioSource access, so
    /// EditMode tests cover the whole audible maths without a running audio engine.
    ///
    /// Zone combining is deliberately "strictest wins" per parameter: a sound heard through the
    /// listener's cave AND emitted in another cave gets the smaller cutoff and the larger wet.
    /// The alternative — letting the emitter's zone overwrite the listener's — loses the
    /// "everything goes dull when I step into a cave" feel for sounds emitted outside it.
    /// </summary>
    public static class AudioPremix
    {
        // The low-pass filter's "no filtering" tier. The human ear caps around 20kHz; anything above
        // it passes the full band
        public const float FullBandwidth = 22000f;

        /// <summary>
        /// Distance factor 0..1 shared by volume and cutoff: 0 = right at the listener's ear,
        /// 1 = far enough to attenuate fully. Non-spatial Cues, missing positions or a missing
        /// listener degrade to 0 ("at the ear"), which is pre-distance behavior.
        /// Measured on the XY plane only (z dropped): the camera sits at z = -10, counting z would
        /// put even a sound at the player's feet at 10 units minimum.
        /// </summary>
        public static float DistanceT(bool spatial, float falloffRange, Vector2 listenerPos, Vector2? emitterPos)
        {
            if (!spatial || !emitterPos.HasValue || falloffRange <= 0f) return 0f;
            return Mathf.Clamp01(Vector2.Distance(emitterPos.Value, listenerPos) / falloffRange);
        }

        /// <summary>Cue volume × distance falloff × settings tracks × zone scale. Zone scale is the
        /// stricter (smaller) of listener and emitter zones.</summary>
        public static float Volume(SoundCue cue, float t, float master, float track,
            in ZoneMix listener, in ZoneMix emitter)
        {
            float falloffGain = Mathf.Lerp(1f, cue.minVolume, t);
            float zoneScale = Mathf.Min(listener.volumeScale, emitter.volumeScale);
            return Mathf.Clamp01(cue.volume * falloffGain * master * track * zoneScale);
        }

        /// <summary>
        /// Low-pass cutoff in Hz. The listener's zone caps every zoned sound ("in a cave everything
        /// goes dull", the player's own footsteps included); a spatial sound additionally takes its
        /// emitter's zone cap and its own distance curve — all three combined by min, the strictest
        /// wins. Non-spatial sounds keep the full band outside zones: their "position" is the
        /// camera-centered mix, distance means nothing.
        /// </summary>
        public static float Cutoff(SoundCue cue, float t, in ZoneMix listener, in ZoneMix emitter)
        {
            float cutoff = listener.cutoff;
            if (cue.spatial)
                cutoff = Mathf.Min(cutoff, emitter.cutoff, Mathf.Lerp(FullBandwidth, cue.minCutoff, t));
            // AudioLowPassFilter's legal range bottoms out at 10 Hz
            return Mathf.Max(10f, cutoff);
        }

        /// <summary>Reverb wet 0..1: the wetter of the two zones, scaled by the Cue's own amount
        /// (0 = this sound never gets reverb, whatever the zone says).</summary>
        public static float Wet(SoundCue cue, in ZoneMix listener, in ZoneMix emitter)
        {
            return Mathf.Clamp01(Mathf.Max(listener.reverbWet, emitter.reverbWet) * cue.reverbAmount);
        }

        /// <summary>Which settings track scales this Cue. The Music track only matters once the
        /// music path exists; Cues can be tagged ahead of time.</summary>
        public static float TrackVolume(SoundCue cue, float musicVolume, float sfxVolume) =>
            cue.category == SoundCue.Category.Music ? musicVolume : sfxVolume;
    }
}
