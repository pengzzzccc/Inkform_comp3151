using UnityEngine;

namespace Inkform.Audio
{
    /// <summary>Settings sliders (linear 0..1) to AudioMixer group volumes (dB).</summary>
    public static class AudioLevels
    {
        /// <summary>The mixer's silence floor.</summary>
        public const float SilentDb = -80f;

        /// <summary>Linear gain to decibels: 1 = 0 dB, every halving -6 dB, 0 = the -80 dB floor.</summary>
        public static float LinearToDb(float linear)
        {
            if (linear <= 0.0001f) return SilentDb;
            return Mathf.Max(SilentDb, 20f * Mathf.Log10(linear));
        }
    }

    /// <summary>
    /// Positional sound for a 2D game on Unity's 3D audio. Emitters sit on the world plane (z = 0),
    /// the AudioListener rides the camera PlaneDepth units in front of it (MainCamera z = -10), so
    /// an emitter's sideways offset turns into a natural left/right pan — on WebGL too, where the
    /// browser's panner does the work and AudioSource.panStereo is unsupported.
    ///
    /// The camera depth is folded into the linear rolloff: minDistance = PlaneDepth (a sound right
    /// under the camera is at full volume) and maxDistance = the 3D distance of a sound sitting
    /// falloffRange away on the plane, so it reaches silence exactly at the Cue's audible radius.
    /// </summary>
    public static class AudioSpatial
    {
        public const float PlaneDepth = 10f;

        public static float MinDistance => PlaneDepth;

        public static float MaxDistanceFor(float falloffRange)
        {
            float range = Mathf.Max(0.01f, falloffRange);
            return Mathf.Sqrt(range * range + PlaneDepth * PlaneDepth);
        }
    }
}
