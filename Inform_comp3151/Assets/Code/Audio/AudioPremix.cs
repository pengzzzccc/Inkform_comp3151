using UnityEngine;

namespace Inkform.Audio
{
    /// <summary>
    /// What one audio zone does to what the listener hears: scales volume, caps the low-pass
    /// cutoff, adds reverb wet. Neutral is "no zone" (nothing changed). Volume and cutoff combine
    /// strictest-wins between the listener's and a spatial sound's emitter zones; reverb wet is
    /// consumed listener-side only — it drives one global filter on the AudioListener (tails ring
    /// out after clips end), so the emitter half of a zone does not wet anything.
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
    /// Pure parameter premix: turns (Cue, distance, settings volumes, zone states) into the
    /// numbers a voice needs — volume and low-pass cutoff. No AudioSource access, so EditMode
    /// tests cover the whole audible maths without a running audio engine.
    ///
    /// Volume and cutoff combining is deliberately "strictest wins" per parameter: a sound heard
    /// through the listener's cave AND emitted in another cave gets the smaller cutoff. The
    /// alternative — letting the emitter's zone overwrite the listener's — loses the
    /// "everything goes dull when I step into a cave" feel for sounds emitted outside it.
    /// Reverb is not premixed per voice at all; see WetToHundredthsDb for the listener-side path.
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
        ///
        /// The ratio is eased in quadratically rather than used raw: loudness perception is
        /// logarithmic, so a linear gain ramp reads as dropping steeply from the very first step
        /// away (Unity's own stock Linear rolloff is the one mode with exactly that complaint; the
        /// engine's default is logarithmic, and middleware leaves curve shaping to the designer).
        /// t² holds near-full gain through the inner half of the range and concentrates the fall
        /// into its outer half, while keeping both endpoints exactly where they were.
        /// </summary>
        public static float DistanceT(bool spatial, float falloffRange, Vector2 listenerPos, Vector2? emitterPos)
        {
            if (!spatial || !emitterPos.HasValue || falloffRange <= 0f) return 0f;
            float linear = Mathf.Clamp01(Vector2.Distance(emitterPos.Value, listenerPos) / falloffRange);
            return linear * linear;
        }

        /// <summary>
        /// Stereo pan, -1 (hard left) .. +1 (hard right), from the emitter's X offset relative to the
        /// listener, saturating at the Cue's falloffRange: at the edge of audibility the sound is fully
        /// panned, right at the ear it is centred. Only X matters — this is a 2D stereo field, vertical
        /// position carries no stereo information. Non-spatial Cues, missing positions or a missing
        /// listener return 0 (centred), the same degradation DistanceT uses.
        /// One saturation width for the whole project on purpose: pan and attenuation share the same
        /// spatial extent, so a sound that is barely audible is also barely panned.
        /// (WebGL ignores AudioSource.panStereo entirely — spatial sound there keeps volume and
        /// low-pass falloff but stays centred.)
        /// </summary>
        public static float Pan(bool spatial, float falloffRange, Vector2 listenerPos, Vector2? emitterPos)
        {
            if (!spatial || !emitterPos.HasValue || falloffRange <= 0f) return 0f;
            return Mathf.Clamp((emitterPos.Value.x - listenerPos.x) / falloffRange, -1f, 1f);
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

        /// <summary>Maps a 0..1 reverb wet amount onto the listener filter's wet-path levels, in
        /// hundredths of a dB (the unit AudioReverbFilter's level fields use): wet 1 = 0 dB (full),
        /// every halving = -6 dB, floor at -100 dB = effectively silent. Only the wet path is
        /// driven; the dry path stays at unity so the untouched signal never dips.</summary>
        public static float WetToHundredthsDb(float wet)
        {
            if (wet <= 0.0001f) return -10000f;
            return Mathf.Max(-10000f, 20f * Mathf.Log10(wet)) * 100f;
        }

        /// <summary>Which settings track scales this Cue. The Music track only matters once the
        /// music path exists; Cues can be tagged ahead of time.</summary>
        public static float TrackVolume(SoundCue cue, float musicVolume, float sfxVolume) =>
            cue.category == SoundCue.Category.Music ? musicVolume : sfxVolume;
    }
}
