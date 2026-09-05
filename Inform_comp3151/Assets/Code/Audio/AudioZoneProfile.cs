using UnityEngine;

namespace Inkform.Audio
{
    /// <summary>
    /// What a place sounds like: how quiet, how muffled, how wet. Referenced by AudioZone volumes
    /// placed in scenes; created via Assets > Create > Audio > Zone Profile. The values feed the
    /// strictest-wins combining in AudioPremix — a zone never brightens or dries a sound, it only
    /// constrains what passes through it (cutoff min / wet max / volume scale multiply).
    /// </summary>
    [CreateAssetMenu(menuName = "Audio/Zone Profile")]
    public class AudioZoneProfile : ScriptableObject
    {
        [Tooltip("Scales every zoned sound heard inside. 1 = unchanged")]
        [Range(0f, 1f)] public float volumeScale = 1f;

        [Tooltip("Low-pass cutoff (Hz) capping every zoned sound heard inside. 22000 = open air;" +
            " a muffled cave sits around 2000-4000")]
        public float cutoff = AudioPremix.FullBandwidth;

        [Tooltip("Reverb wet 0..1 applied to everything heard inside the zone (listener-side" +
            " global filter — tails ring out after sounds end). 0 = dry, 1 = soaked")]
        [Range(0f, 1f)] public float reverbWet;

        [Tooltip("Seconds to blend in when the listener enters")]
        public float blendIn = 0.5f;

        [Tooltip("Seconds to blend out when the listener leaves")]
        public float blendOut = 0.8f;

        public ZoneMix Mix => new ZoneMix(volumeScale, cutoff, reverbWet);
    }
}
