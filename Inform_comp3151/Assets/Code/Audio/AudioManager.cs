using System.Collections.Generic;
using UnityEngine.Audio;
using UnityEngine;

namespace Inkform.Audio
{
    /// <summary>
    /// Audio pool: builds a batch of AudioSources at startup and recycles them, avoiding a new
    /// GameObject per sound. Playback policy (random variants / cooldown / concurrency cap / distance
    /// falloff) is all configured in SoundCue; this class only takes and returns sources.
    /// Attach to any scene object; persists across scenes by itself.
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        // The low-pass filter's "no filtering" tier. The human ear caps around 20kHz; anything above
        // it passes the full band
        private const float FullBandwidth = 22000f;

        [SerializeField] private int poolSize = 24;

        [Tooltip("Reverb tiers, near to far. Empty = no reverb; volume and low-pass still apply")]
        [SerializeField] private AudioMixerGroup[] reverbTiers;

        private readonly Queue<Source> pool = new Queue<Source>();
        private readonly List<Voice> active = new List<Voice>();

        // SoundCue's runtime count lives on the asset and does not clear when exiting play mode; if the
        // previous run leaked activeCount, the Cue would be permanently muted next run. Zeroing it on
        // first use sidesteps that.
        private readonly HashSet<SoundCue> seen = new HashSet<SoundCue>();

        // A source and its low-pass filter must be stored as a pair: every play resets the cutoff by
        // distance; GetComponent per play is wasteful, fetch once at pool build
        private struct Source
        {
            public AudioSource src;
            public AudioLowPassFilter lpf;
        }

        // On recycle we must know which Cue to decrement, so source and Cue must be stored as a pair
        private struct Voice
        {
            public Source source;
            public SoundCue cue;
        }

        // Listener position. This component lives on a DontDestroyOnLoad object and does not follow the
        // camera, so it cannot use transform.position like FxDirector (which sits on the camera).
        // After a scene change the old listener becomes a Unity fake-null; it is re-looked-up on next use
        private Transform listenerCache;

        private Transform Listener
        {
            get
            {
                if (listenerCache == null)
                {
                    // Use Any rather than First: the scene should only ever have one enabled
                    // AudioListener, there is no "which one" to speak of
                    AudioListener l = FindAnyObjectByType<AudioListener>();
                    listenerCache = l != null ? l.transform : null;
                }
                return listenerCache;
            }
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            for (int i = 0; i < poolSize; i++)
            {
                GameObject go = new GameObject($"AudioSource_{i}");
                go.transform.SetParent(transform);
                AudioSource src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;

                // Always 2D; passing a position never switches to 3D. Unity's 3D falloff would stack
                // on top of our own distance factor, and since the camera sits at z = -10 its computed
                // distance is always ≥10, crushing explosions to near inaudible. Distance feel comes
                // entirely from Falloff() measured on the XY plane; z does not participate.
                src.spatialBlend = 0f;

                AudioLowPassFilter lpf = go.AddComponent<AudioLowPassFilter>();
                lpf.cutoffFrequency = FullBandwidth;

                pool.Enqueue(new Source { src = src, lpf = lpf });
            }
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // Auto-recycle when finished. Not "coroutine per clip duration" because: once a source is
        // manually stopped it returns to the pool and may be taken by another Cue, and the stale
        // coroutine would then stop the new sound and decrement the wrong Cue's count. isPlaying has
        // no such issue; loop sounds keep isPlaying true forever and naturally stay until explicit Stop.
        void Update()
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                if (active[i].source.src == null) { active.RemoveAt(i); continue; }   // source destroyed unexpectedly
                if (!active[i].source.src.isPlaying) ReleaseAt(i);
            }
        }

        /// <summary>Plays a Cue. With position null (or the Cue's spatial off) no distance falloff applies;
        /// otherwise volume is scaled, reverb added, high frequencies cut by distance from the listener.
        /// Returns null when blocked by cooldown/concurrency cap or the pool is empty (drops, never grows).</summary>
        public AudioSource Play(SoundCue cue, Vector3? position = null)
        {
            if (cue == null) return null;

            if (seen.Add(cue))          // first time seeing this Cue this run: clear counts left over from the last run
            {
                cue.activeCount = 0;
                cue.lastPlayTime = -999f;
            }

            // Cooldown must use unscaled time: explosions trigger hitstop which crushes timeScale to 0;
            // with Time.time the cooldown would not advance during hitstop
            if (Time.unscaledTime - cue.lastPlayTime < cue.cooldown) return null;
            if (cue.activeCount >= cue.maxConcurrent) return null;
            if (pool.Count == 0) return null;   // pool empty, drop rather than grow

            AudioClip clip = cue.PickClip();
            if (clip == null) return null;      // Cue has no clip, or every slot is empty

            Source s = pool.Dequeue();
            AudioSource src = s.src;

            src.clip = clip;
            src.pitch = cue.PickPitch();
            src.loop = cue.loop;

            // Distance factor: 0 = right at the listener's ear, 1 = far enough to attenuate fully.
            // All three presentations share this one value
            float t = Falloff(cue, position);

            src.volume = cue.volume * Mathf.Lerp(1f, cue.minVolume, t);
            s.lpf.cutoffFrequency = Mathf.Lerp(FullBandwidth, cue.minCutoff, t);
            src.outputAudioMixerGroup = PickGroup(cue, t);

            src.Play();
            cue.lastPlayTime = Time.unscaledTime;
            cue.activeCount++;
            active.Add(new Voice { source = s, cue = cue });

            return src;
        }

        /// <summary>How far from the listener, normalized to [0,1]. Without spatial / no position /
        /// no AudioListener in the scene, returns 0 — "right at the ear" — degrading to pre-distance behavior.</summary>
        private float Falloff(SoundCue cue, Vector3? position)
        {
            if (!cue.spatial || !position.HasValue || cue.falloffRange <= 0f) return 0f;

            Transform ear = Listener;
            if (ear == null) return 0f;

            // Measure only on the XY plane (the Vector2 conversion drops z): the camera sits at z = -10,
            // counting z would put even a sound at the player's feet at 10 units minimum
            return Mathf.Clamp01(Vector2.Distance(position.Value, ear.position) / cue.falloffRange);
        }

        /// <summary>Picks a reverb tier by distance. Reverb intensity can only be discrete tiers —
        /// AudioMixer effect parameters are group-level: every sound in a group shares one setting,
        /// so each sound cannot have its own.</summary>
        private AudioMixerGroup PickGroup(SoundCue cue, float t)
        {
            // Empty is legal: degrades to volume + low-pass only, distance feel intact. The game runs
            // before a mixer exists; dragging one in later just works — consistent with the project's
            // "empty slot = silent skip" convention
            if (!cue.spatial || cue.reverbAmount <= 0f) return cue.output;
            if (reverbTiers == null || reverbTiers.Length == 0) return cue.output;

            int tier = Mathf.Min((int)(t * cue.reverbAmount * reverbTiers.Length), reverbTiers.Length - 1);
            return reverbTiers[tier] != null ? reverbTiers[tier] : cue.output;
        }

        /// <summary>Stops a still-playing source early. Loop sounds can only end this way (they never
        /// finish naturally). The caller need not pass the Cue — passing the wrong one would decrement
        /// the wrong count; it is taken from the Voice itself.</summary>
        public void Stop(AudioSource src)
        {
            if (src == null) return;

            int i = active.FindIndex(v => v.source.src == src);
            if (i < 0) return;              // not issued from this pool, or already recycled
            ReleaseAt(i);
        }

        private void ReleaseAt(int i)
        {
            Voice v = active[i];
            active.RemoveAt(i);

            v.source.src.Stop();
            v.source.src.clip = null;

            // Must restore: the source is recycled, and after a distant explosion crushed the cutoff to
            // a few hundred Hz, the next nearby sound borrowing this source (e.g. the player's jump)
            // would mysteriously go dull. outputAudioMixerGroup needs no restore — every Play sets it explicitly
            v.source.lpf.cutoffFrequency = FullBandwidth;

            if (v.cue != null) v.cue.activeCount--;
            pool.Enqueue(v.source);
        }
    }
}
