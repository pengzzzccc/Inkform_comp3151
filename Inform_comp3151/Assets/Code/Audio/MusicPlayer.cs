using UnityEngine;

namespace Inkform.Audio
{
    /// <summary>
    /// Two dedicated AudioSources for background music and the crossfade machinery driving them.
    /// Music never uses a one-shot voice: it must never be taken over by a burst of one-shots, and it
    /// keeps its own engine priority at 0 (highest) so Unity's virtualization spares it too.
    /// Fades run on unscaled time — a menu crossfade must advance even while the game is paused.
    /// Created and owned by AudioService; both sources play through the Cue's mixer group (the
    /// Music group by default), where the settings sliders apply.
    /// </summary>
    public class MusicPlayer : MonoBehaviour
    {
        private readonly AudioSource[] slots = new AudioSource[2];
        private readonly SoundCue[] slotCues = new SoundCue[2];
        private readonly MusicFader fader = new MusicFader();

        /// <summary>Builds the player as a child of the AudioService — children of a
        /// DontDestroyOnLoad root persist with it, so the music never hiccups on scene changes.</summary>
        public static MusicPlayer Create(Transform owner)
        {
            GameObject go = new GameObject("MusicPlayer");
            go.transform.SetParent(owner, false);
            return go.AddComponent<MusicPlayer>();
        }

        void Awake()
        {
            for (int i = 0; i < slots.Length; i++)
            {
                GameObject go = new GameObject($"MusicSlot_{i}");
                go.transform.SetParent(transform, false);
                AudioSource src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 0f;          // global bed, not a world emitter
                src.loop = true;
                src.priority = 0;               // engine virtualization ranks music above everything
                src.volume = 0f;
                slots[i] = src;
            }
        }

        /// <summary>Crossfades to a Cue (null stops the music). Requesting the track that is
        /// currently the audible one is absorbed by the fader as a no-op; after a stop (scene
        /// transition) the same cue requested again fades back in from silence.</summary>
        public void Play(SoundCue cue, float fadeSeconds)
        {
            int slot = fader.Request(cue, fadeSeconds);
            if (slot < 0 || cue == null) return;

            AudioClip clip = cue.PickClip();
            if (clip == null) return;      // Cue has no clip: nothing to fade to, old track keeps fading

            AudioSource src = slots[slot];
            src.outputAudioMixerGroup = AudioService.OutputFor(cue);
            src.clip = clip;
            src.pitch = cue.PickPitch();
            slotCues[slot] = cue;
            src.volume = 0f;               // fades up from silence
            src.Play();
        }

        public void Stop(float fadeSeconds) => fader.Stop(fadeSeconds);

        // Fades advance every frame; the slot volume is fade × Cue volume, the settings sliders
        // live on the mixer group
        void Update()
        {
            fader.Tick(Time.unscaledDeltaTime, out float v0, out float v1, out bool stop0, out bool stop1);
            ApplySlot(0, v0, stop0);
            ApplySlot(1, v1, stop1);
        }

        private void ApplySlot(int i, float fade, bool stop)
        {
            AudioSource src = slots[i];
            if (src == null) return;

            if (stop && src.clip != null)
            {
                src.Stop();
                src.clip = null;
                slotCues[i] = null;
                return;
            }

            SoundCue cue = slotCues[i];
            float cueVolume = cue != null ? cue.volume : 1f;
            src.volume = Mathf.Clamp01(fade * cueVolume);
        }
    }
}
