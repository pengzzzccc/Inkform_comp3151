using Inkform.Tool;
using UnityEngine;
using UnityEngine.Audio;

namespace Inkform.Audio
{
    /// <summary>
    /// Which lane a sound competes in when every one-shot voice is busy: a post may only take over
    /// a voice of the same or a less important lane. Also written to AudioSource.priority
    /// (0 = most important) so Unity's own voice virtualization ranks sounds the same way.
    /// Critical additionally bypasses the Cue's cooldown and concurrency caps — death feedback.
    /// </summary>
    public enum CuePriority { Critical = 0, Gameplay = 128, Ambience = 224 }

    /// <summary>
    /// Config for one sound: random variants + volume/pitch/concurrency limits, pure data asset.
    /// Created via Assets > Create > Audio > Sound Cue, referenced by AudioDirector and friends in
    /// the Inspector. AudioService plays one-shots and music from it, AmbientSource plays loops;
    /// this class only describes "how to play" and holds no runtime state.
    /// </summary>
    [CreateAssetMenu(menuName = "Audio/Sound Cue")]
    public class SoundCue : ScriptableObject
    {
        public enum Category { Sfx, Music }

        public AudioClip[] clips;                    // variants, picked randomly to avoid repetition fatigue
        [Tooltip("Mixer group this Cue plays through. Empty = routed by Category (Music or Sfx group of the game mixer)")]
        public AudioMixerGroup output;
        [Range(0f, 1f)] public float volume = 1f;
        [Tooltip("Default routing when Output is empty, which also decides the settings slider: Music Cues go to the Music group (PlayMusic), the rest to Sfx")]
        public Category category = Category.Sfx;
        [Tooltip("Random pitch range. Keep both ends positive — WebGL only supports positive pitch, and pitch 0 never finishes playing")]
        public Vector2 pitchRange = new Vector2(0.95f, 1.05f);
        public bool loop;
        [Tooltip("Continue while AudioListener is paused. Enable only for menu/UI feedback.")]
        public bool ignoreListenerPause;
        [Tooltip("Minimum retrigger interval for the same Cue, prevents stacked pops within one frame")]
        public float cooldown = 0.05f;
        [Range(1, 8)] public int maxConcurrent = 3;
        [Tooltip("Which lane this sound competes in when every voice is busy. Critical bypasses cooldown" +
            " and concurrency and may take over any voice — reserve for death/game-over feedback")]
        public CuePriority priority = CuePriority.Gameplay;

        [Header("Spatial (distance + pan)")]
        [Tooltip("Off = 2D, played at the camera. On = positional: Unity's 3D audio pans it by its X offset " +
            "and fades it linearly to silence at Falloff Range. The player's own sounds (jump/land/eat) " +
            "should keep it off — they are always at the camera center, distance means nothing")]
        public bool spatial = false;
        [Tooltip("Audible radius on the XY plane: full volume at the listener, silent at this distance")]
        public float falloffRange = 20f;

        /// <summary>Picks a random variant. Empty slots are skipped; returns null if all are empty.</summary>
        public AudioClip PickClip() => RandomPick.FromArray(clips);

        public float PickPitch() =>
            Random.Range(pitchRange.x, pitchRange.y);
    }
}
