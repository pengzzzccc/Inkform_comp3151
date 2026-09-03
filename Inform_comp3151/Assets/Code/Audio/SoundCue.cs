using Inkform.Tool;
using UnityEngine;
using UnityEngine.Audio;

namespace Inkform.Audio
{
    /// <summary>
    /// Config for one sound: multiple random variants + volume/pitch/concurrency limits, pure data asset.
    /// Created via Assets > Create > Audio > Sound Cue, referenced by AudioDirector in the Inspector.
    /// The playing itself is handled by AudioManager; this class only describes "how to play".
    /// </summary>
    [CreateAssetMenu(menuName = "Audio/Sound Cue")]
    public class SoundCue : ScriptableObject
    {
        public enum Category { Sfx, Music }

        public AudioClip[] clips;                    // variants, picked randomly to avoid repetition fatigue
        public AudioMixerGroup output;
        [Range(0f, 1f)] public float volume = 1f;
        [Tooltip("Which settings track scales this sound (AudioManager reads SettingsStore). Music Cues feed the music path (PlayMusic)")]
        public Category category = Category.Sfx;
        [Tooltip("Random pitch range. Never set either end to 0 — a source at pitch 0 never finishes playing and permanently occupies a pool slot")]
        public Vector2 pitchRange = new Vector2(0.95f, 1.05f);
        public bool loop;
        [Tooltip("Continue while AudioListener is paused. Enable only for menu/UI feedback.")]
        public bool ignoreListenerPause;
        [Tooltip("Minimum retrigger interval for the same Cue, prevents stacked pops within one frame")]
        public float cooldown = 0.05f;
        [Range(1, 8)] public int maxConcurrent = 3;
        [Tooltip("Which lane this sound competes in when the pool runs dry. Critical bypasses cooldown" +
            " and concurrency and may steal lower lanes — reserve for death/game-over feedback." +
            " Existing Cue assets predate this field and deserialize to Gameplay, unchanged behavior")]
        public CuePriority priority = CuePriority.Gameplay;

        [Header("Distance falloff")]
        // Defaulting to off is intentional: existing Cue assets do not store these fields, they take
        // these initial values after deserialization, behaving exactly as before distance was added —
        // this change must not suddenly alter every sound.
        [Tooltip("Off = always played as 2D. The player's own sounds (jump/land/eat) should keep it off — they are always at the camera center, distance means nothing")]
        public bool spatial = false;
        [Tooltip("Beyond this distance from the listener, attenuation bottoms out")]
        public float falloffRange = 20f;
        [Tooltip("Volume factor at max distance. 0 = completely silent")]
        [Range(0f, 1f)] public float minVolume = 0.15f;
        [Tooltip("Reverb tier ceiling. 0 = this sound never gets reverb")]
        [Range(0f, 1f)] public float reverbAmount = 1f;
        [Tooltip("Low-pass cutoff frequency (Hz) at max distance. Lower = duller. 22000 = no filtering")]
        public float minCutoff = 900f;

        // Runtime state. A ScriptableObject is an asset, its instance persists in editor memory — these
        // two values do not clear when exiting play mode; AudioManager resets them the first time this
        // Cue is used in each run.
        [System.NonSerialized] public float lastPlayTime = -999f;
        [System.NonSerialized] public int activeCount;

        /// <summary>Picks a random variant. Empty slots are skipped; returns null if all are empty.</summary>
        public AudioClip PickClip() => RandomPick.FromArray(clips);

        public float PickPitch() =>
            Random.Range(pitchRange.x, pitchRange.y);
    }
}
