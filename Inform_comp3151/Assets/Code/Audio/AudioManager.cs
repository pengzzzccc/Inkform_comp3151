using System.Collections.Generic;
using Inkform.Settings;
using UnityEngine.SceneManagement;
using UnityEngine;

namespace Inkform.Audio
{
    /// <summary>
    /// Audio voice host: keeps a pool of AudioSources (each with its own low-pass filter for
    /// distance/zone muffling), grows it up to a hard cap under pressure, steals the least
    /// important live voice when the cap is hit, and refreshes persistent loops (ambience) every
    /// frame. Zone reverb is NOT on these voices: it lives on the AudioListener's own filter (see
    /// DriveListenerReverb), so tails keep ringing after the pool recycles a source.
    /// Playback policy (random variants / cooldown / concurrency cap / distance falloff) is all
    /// configured in SoundCue; the audible maths lives in AudioPremix, the capacity decisions in
    /// VoiceArbiter — this class only executes them against real AudioSources.
    /// Attach to any scene object; persists across scenes by itself.
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        [Tooltip("Sources created at startup. The existing GameManager prefab serializes 24")]
        [SerializeField] private int poolSize = 24;

        [Tooltip("Ceiling for pool + live voices combined. Above this the pool stops growing and " +
            "steals instead. Must stay within the project's Real Voice count (Project Settings > " +
            "Audio) — sources beyond it are virtualized by Unity, silently inaudible")]
        [SerializeField] private int hardCap = 48;

        [Tooltip("How many sources one growth step adds. Coarse chunks, not one per post")]
        [SerializeField] private int growthStep = 4;

        [Header("Debug")]
        [Tooltip("Log every strategically dropped post (pool at hard cap, nothing stealable). " +
            "Dev switch — leave off in builds")]
        [SerializeField] private bool logDrops;

        [Tooltip("Play-mode counters: voices stolen to make room, posts dropped at the cap")]
        [SerializeField] private int stolenVoices;
        [SerializeField] private int droppedPosts;

        public int ActiveVoiceCount => active.Count;
        public int PooledSourceCount => pool.Count;

        private readonly Queue<Source> pool = new Queue<Source>();
        private readonly List<Voice> active = new List<Voice>();

        // Scratch list for arbitration, refilled per post — arbitration happens under pressure,
        // one allocation per post there beats a permanent second bookkeeping list
        private readonly List<VoiceFact> facts = new List<VoiceFact>();

        // SoundCue's runtime count lives on the asset and does not clear when exiting play mode; if the
        // previous run leaked activeCount, the Cue would be permanently muted next run. Zeroing it on
        // first use sidesteps that.
        private readonly HashSet<SoundCue> seen = new HashSet<SoundCue>();
        private int nextSourceId;

        // A source and its low-pass filter must be stored as a pair: every play resets the cutoff
        // by distance/zone; GetComponent per play is wasteful, fetch once at pool build
        private struct Source
        {
            public AudioSource src;
            public AudioLowPassFilter lpf;
        }

        // On recycle we must know which Cue to decrement, so the source, the Cue and the voice's
        // arbitration facts travel as one record. baseGain is everything except the settings
        // tracks (cue volume, falloff, zone scale) so a settings change can re-scale it live.
        private struct Voice
        {
            public Source source;
            public SoundCue cue;
            public float baseGain;
            public CuePriority priority;
            public float startedAt;     // Time.unscaledTime
            public bool persistent;     // loops refreshed per frame; never auto-recycled
            public Vector3 position;    // emitter position for the per-frame refresh
        }

        // Listener position. This component lives on a DontDestroyOnLoad object and does not follow the
        // camera, so it cannot use transform.position like FxDirector (which sits on the camera).
        // After a scene change the old listener becomes a Unity fake-null; it is re-looked-up on next use
        private Transform listenerCache;

        // Zone reverb rides the listener's own filter, not the voices: the listener's DSP processes
        // the whole mix and keeps running after any single clip ends, so reverb tails ring out
        // naturally instead of being cut the moment the pool recycles a one-shot's source
        private AudioReverbFilter listenerReverb;
        private Transform listenerReverbOwner;

        // Music runs on its own two dedicated sources, never the voice pool (see MusicPlayer)
        private MusicPlayer music;

        // Zone state: the listener's blended mix, plus the registry every live zone registers
        // into so a spatial post can ask "which zones contain this sound's origin"
        private readonly ZoneMixer zones = new ZoneMixer();
        private readonly List<AudioZone> zoneRegistry = new List<AudioZone>();

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

        // Static fields do not clear on scene reload; with Domain Reload off, a stale Instance from
        // the previous run would make every later duplicate manager destroy itself on sight
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            if (Application.isPlaying) DontDestroyOnLoad(gameObject);

            for (int i = 0; i < poolSize; i++)
                pool.Enqueue(CreateSource());

            music = MusicPlayer.Create(transform);
        }

        // One subscription, one refresh path: Play reads the volumes when a sound starts, and this
        // handler re-scales every already-playing voice when they change — both matter (a slider
        // drag must retune running loops, not just future sounds)
        void OnEnable()
        {
            SettingsStore.Changed += RefreshActiveVolumes;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        void OnDisable()
        {
            SettingsStore.Changed -= RefreshActiveVolumes;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        // The manager outlives scenes, its zone state must not: zones from the unloaded scene pop
        // themselves via OnDisable, and whatever slipped through (destroyed colliders, mid-blend
        // transitions) resets to open air for the new scene's spawn
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            zoneRegistry.RemoveAll(z => z == null);
            zones.Clear();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // Auto-recycle when finished. Not "coroutine per clip duration" because: once a source is
        // manually stopped it returns to the pool and may be taken by another Cue, and the stale
        // coroutine would then stop the new sound and decrement the wrong Cue's count. isPlaying has
        // no such issue; loop sounds keep isPlaying true forever and stay until explicit Stop —
        // persistent voices are additionally re-premixed here every frame, so walking towards a
        // campfire swells it and stepping into a cave dulls it mid-loop.
        void Update()
        {
            // Unscaled: a hitstop must not freeze a cave fade halfway
            zones.Tick(Time.unscaledDeltaTime);
            DriveListenerReverb();
            // Zone components destroyed by teardown leave fake-null entries behind; prune lazily
            zoneRegistry.RemoveAll(z => z == null);

            for (int i = active.Count - 1; i >= 0; i--)
            {
                Voice voice = active[i];
                if (voice.source.src == null)
                {
                    // The source object died (scene teardown edge cases). Persistent voices get a
                    // replacement source and keep looping; one-shots are simply done
                    if (voice.persistent)
                    {
                        voice.source = RestartPersistent(voice);
                        active[i] = voice;
                        continue;
                    }
                    active.RemoveAt(i);
                    if (voice.cue != null) voice.cue.activeCount = Mathf.Max(0, voice.cue.activeCount - 1);
                    pool.Enqueue(CreateSource());
                    continue;
                }
                if (voice.persistent)
                {
                    RefreshPersistent(ref voice);
                    active[i] = voice;
                    continue;
                }
                if (!voice.source.src.isPlaying) ReleaseAt(i);
            }
        }

        /// <summary>Plays a Cue. With position null (or the Cue's spatial off) no distance falloff applies;
        /// otherwise volume is scaled and high frequencies cut by distance from the listener.
        /// Returns null when blocked by cooldown/concurrency cap, or when the pool is at its hard
        /// cap with nothing stealable left (never silently below the cap — grows there first).</summary>
        public AudioSource Play(SoundCue cue, Vector3? position = null) =>
            Post(new AudioPost(cue, position));

        /// <summary>Full-fat entry point: priority, zone bypass and position in one value. Play is
        /// the compatibility alias the existing callers keep using.</summary>
        public AudioSource Post(AudioPost post)
        {
            SoundCue cue = post.cue;
            if (cue == null) return null;

            if (seen.Add(cue))          // first time seeing this Cue this run: clear counts left over from the last run
            {
                cue.activeCount = 0;
                cue.lastPlayTime = -999f;
            }

            // Cooldown must use unscaled time: explosions trigger hitstop which crushes timeScale to 0;
            // with Time.time the cooldown would not advance during hitstop. Critical feedback
            // (death stinger) must fire no matter how recently the Cue played — that is the point
            // of the lane, so it skips both gates
            bool critical = post.priority == CuePriority.Critical;
            if (!critical)
            {
                if (Time.unscaledTime - cue.lastPlayTime < cue.cooldown) return null;
                if (cue.activeCount >= cue.maxConcurrent) return null;
            }

            AudioClip clip = cue.PickClip();
            if (clip == null) return null;      // Cue has no clip, or every slot is empty

            Source s = AcquireSource(post.priority);
            if (s.src == null) return null;     // at cap and nothing stealable: already counted + logged
            return StartVoice(s, cue, clip, post.priority, post.position,
                persistent: false, cue.loop, zoned: !post.ignoreZone && !cue.ignoreListenerPause);
        }

        /// <summary>
        /// Takes a persistent voice for a looping world sound (AmbientSource). No cooldown or
        /// concurrency gates — a loop is one voice by construction — and the voice is refreshed
        /// every frame in Update until ReleaseAmbient, so its falloff and zone treatment track the
        /// listener live. The position is snapshotted here: emitters are expected to be static
        /// (a campfire does not walk); moving emitters would need their own per-frame push.
        /// Returns null when the Cue is unconfigured or the pool is at its cap with nothing this
        /// priority may steal.
        /// </summary>
        public AudioSource RegisterAmbient(SoundCue cue, Vector3 position)
        {
            if (cue == null) return null;

            if (seen.Add(cue)) { cue.activeCount = 0; cue.lastPlayTime = -999f; }

            AudioClip clip = cue.PickClip();
            if (clip == null) return null;

            Source s = AcquireSource(cue.priority);
            if (s.src == null) return null;
            return StartVoice(s, cue, clip, cue.priority, position,
                persistent: true, loop: true, zoned: !cue.ignoreListenerPause);
        }

        /// <summary>Returns an ambient loop's voice to the pool (AmbientSource.OnDisable).</summary>
        public void ReleaseAmbient(AudioSource src) => Stop(src);

        // Everything after source acquisition is shared by one-shots and ambient loops: configure
        // the source, premix the audible parameters, start playback, record the voice
        private AudioSource StartVoice(Source s, SoundCue cue, AudioClip clip,
            CuePriority priority, Vector3? position, bool persistent, bool loop, bool zoned)
        {
            AudioSource src = s.src;
            src.clip = clip;
            src.pitch = cue.PickPitch();
            src.loop = loop;
            src.ignoreListenerPause = cue.ignoreListenerPause;
            // Same ranking the arbiter uses, mirrored onto the engine's own virtualization: when
            // Unity runs out of real voices it keeps the sources it considers important, and its
            // definition of important must match ours
            src.priority = (int)priority;

            // Zone states come from the mixer; Neutral outside zones / for zone-bypassing posts.
            // A Cue that ignores listener pause also ignores zones — both flags mean "system
            // feedback, not part of the world" (menu clicks must not go dull inside a cave)
            ZoneMix listenerZone = zoned ? ListenerZoneMix() : ZoneMix.Neutral;
            ZoneMix emitterZone = zoned && cue.spatial && position.HasValue
                ? EmitterZoneMix(position.Value)
                : ZoneMix.Neutral;

            float t = Falloff(cue, position);
            Voice voice = new Voice
            {
                source = s,
                cue = cue,
                priority = priority,
                startedAt = Time.unscaledTime,
                persistent = persistent,
                position = position.GetValueOrDefault(Vector3.zero),
                baseGain = cue.volume * Mathf.Lerp(1f, cue.minVolume, t)
                    * Mathf.Min(listenerZone.volumeScale, emitterZone.volumeScale),
            };
            src.volume = Mathf.Clamp01(voice.baseGain
                * SettingsStore.MasterVolume
                * AudioPremix.TrackVolume(cue, SettingsStore.MusicVolume, SettingsStore.SfxVolume));
            s.lpf.cutoffFrequency = AudioPremix.Cutoff(cue, t, listenerZone, emitterZone);
            src.outputAudioMixerGroup = cue.output;

            src.Play();
            cue.lastPlayTime = Time.unscaledTime;
            cue.activeCount++;
            active.Add(voice);

            return src;
        }

        /// <summary>Where the next source comes from: pooled, grown, or stolen — VoiceArbiter decides,
        /// this executes and keeps the debug counters honest.</summary>
        private Source AcquireSource(CuePriority incoming)
        {
            switch (VoiceArbiter.DecideAcquire(pool.Count, active.Count, hardCap))
            {
                case VoiceArbiter.Acquire.UsePooled:
                    return TakeLiveSource();

                case VoiceArbiter.Acquire.Grow:
                {
                    int living = pool.Count + active.Count;
                    int grow = Mathf.Min(growthStep, hardCap - living);
                    for (int i = 0; i < grow; i++) pool.Enqueue(CreateSource());
                    return TakeLiveSource();
                }

                default:
                {
                    facts.Clear();
                    for (int i = 0; i < active.Count; i++)
                        facts.Add(new VoiceFact
                        {
                            priority = active[i].priority,
                            startedAt = active[i].startedAt,
                            persistent = active[i].persistent,
                        });
                    int victim = VoiceArbiter.PickVictim(facts, incoming, Time.unscaledTime);
                    if (victim < 0)
                    {
                        droppedPosts++;
                        if (logDrops)
                            Debug.LogWarning($"[Audio] dropped {incoming} post: {active.Count} voices at hard cap {hardCap}, nothing stealable", this);
                        return default;
                    }
                    stolenVoices++;
                    ReleaseAt(victim);      // stops the victim and returns its source to the pool
                    return TakeLiveSource();
                }
            }
        }

        /// <summary>How far from the listener, normalized to [0,1]. Without spatial / no position /
        /// no AudioListener in the scene, returns 0 — "right at the ear" — degrading to pre-distance behavior.</summary>
        private float Falloff(SoundCue cue, Vector3? position)
        {
            if (!cue.spatial || !position.HasValue || cue.falloffRange <= 0f) return 0f;
            Transform ear = Listener;
            if (ear == null) return 0f;
            return AudioPremix.DistanceT(true, cue.falloffRange, ear.position, position.Value);
        }

        // ---- Zone access points: the listener's blended mix, and the strictest mix of every
        // registered zone containing an emitter position ----

        private ZoneMix ListenerZoneMix() => zones.ListenerState;

        private ZoneMix EmitterZoneMix(Vector3 position)
        {
            ZoneMix mix = ZoneMix.Neutral;
            for (int i = 0; i < zoneRegistry.Count; i++)
            {
                AudioZone zone = zoneRegistry[i];
                if (zone == null || !zone.Contains(position)) continue;
                mix = ZoneMixer.Combine(mix, zone.Mix);
            }
            return mix;
        }

        // ---- Zone lifecycle, driven by AudioZone triggers ----

        public void RegisterZone(AudioZone zone)
        {
            if (zone != null && !zoneRegistry.Contains(zone)) zoneRegistry.Add(zone);
        }

        public void UnregisterZone(AudioZone zone) => zoneRegistry.Remove(zone);

        public void PushListenerZone(object zone, ZoneMix mix, float blendIn) =>
            zones.Push(zone, mix, blendIn);

        public void PopListenerZone(object zone, float blendOut) =>
            zones.Pop(zone, blendOut);

        /// <summary>Crossfades the background music to a Cue (category Music, loop on). Same-track
        /// requests are absorbed as no-ops; music never occupies or gets stolen from the voice
        /// pool. SceneMusic is the usual caller.</summary>
        public void PlayMusic(SoundCue cue, float fadeSeconds = 1.5f) => music?.Play(cue, fadeSeconds);

        /// <summary>Fades the music out over fadeSeconds.</summary>
        public void StopMusic(float fadeSeconds = 1.5f) => music?.Stop(fadeSeconds);

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

            if (v.source.src == null)
            {
                if (v.cue != null) v.cue.activeCount = Mathf.Max(0, v.cue.activeCount - 1);
                pool.Enqueue(CreateSource());
                return;
            }

            v.source.src.Stop();
            v.source.src.clip = null;

            // Must restore: the source is recycled, and after a distant explosion crushed the cutoff to
            // a few hundred Hz, the next nearby sound borrowing this source (e.g. the player's jump)
            // would mysteriously go dull. outputAudioMixerGroup needs no restore — every
            // Play sets it explicitly
            v.source.lpf.cutoffFrequency = AudioPremix.FullBandwidth;

            if (v.cue != null) v.cue.activeCount = Mathf.Max(0, v.cue.activeCount - 1);
            pool.Enqueue(v.source);
        }

        private Source CreateSource()
        {
            GameObject go = new GameObject($"AudioSource_{nextSourceId++}");
            go.transform.SetParent(transform);
            AudioSource src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            AudioLowPassFilter lpf = go.AddComponent<AudioLowPassFilter>();
            lpf.cutoffFrequency = AudioPremix.FullBandwidth;
            return new Source { src = src, lpf = lpf };
        }

        // Zone reverb driver: one AudioReverbFilter on whatever carries the AudioListener. The
        // listener's filter is the master bus — everything the player hears inside a wet zone gets
        // the tail, and because the bus never stops when a pooled voice recycles, tails ring out
        // to their natural end. Reverb deliberately ignores zones' emitter side: a global filter
        // cannot wet one sound and dry another, and "the cave I am in colors what I hear" is the
        // audible half of the strictest-wins rule anyway (the emitter side still muffs and scales)
        private void DriveListenerReverb()
        {
            // Re-attach when the listener is (re)found: scene changes swap the camera, and the
            // filter belongs to whatever carries the AudioListener now
            Transform ear = Listener;
            if (listenerReverbOwner != ear || listenerReverb == null)
            {
                listenerReverbOwner = ear;
                listenerReverb = ear != null ? ear.GetComponent<AudioReverbFilter>() : null;
                if (listenerReverb == null && ear != null)
                {
                    listenerReverb = ear.gameObject.AddComponent<AudioReverbFilter>();
                    listenerReverb.reverbPreset = AudioReverbPreset.Cave;   // decay/diffusion flavor; levels driven below
                }
            }
            if (listenerReverb == null) return;

            float wet = zones.ListenerState.reverbWet;
            bool audible = wet > 0.01f;
            listenerReverb.enabled = audible;
            if (!audible) return;

            float hundredths = AudioPremix.WetToHundredthsDb(wet);
            listenerReverb.room = hundredths;           // early reflections level
            listenerReverb.roomHF = hundredths;         // ...at high frequency
            listenerReverb.reverbLevel = hundredths;    // late reverberation level — the audible tail
        }

        // Dequeue skips dead slots (Unity fake-null after partial teardown) and compensates the
        // pool with fresh sources, so a destroyed source never shrinks the working capacity
        private Source TakeLiveSource()
        {
            int missing = 0;
            while (pool.Count > 0)
            {
                Source source = pool.Dequeue();
                if (source.src == null) { missing++; continue; }
                if (source.lpf == null) source.lpf = source.src.gameObject.AddComponent<AudioLowPassFilter>();
                for (int i = 0; i < missing; i++) pool.Enqueue(CreateSource());
                return source;
            }
            if (missing > 0)
            {
                Source replacement = CreateSource();
                for (int i = 1; i < missing; i++) pool.Enqueue(CreateSource());
                return replacement;
            }
            return default;
        }

        // A destroyed persistent voice is rebuilt, not dropped: its AmbientSource still expects a
        // live loop, so the fresh source restarts the Cue from its own facts
        private Source RestartPersistent(Voice voice)
        {
            Source s = CreateSource();
            if (voice.cue != null)
            {
                s.src.clip = voice.cue.PickClip();
                s.src.pitch = voice.cue.PickPitch();
                s.src.loop = true;
                s.src.ignoreListenerPause = voice.cue.ignoreListenerPause;
                s.src.priority = (int)voice.priority;
                s.src.volume = 0f;      // silent until the next refresh pass sets the real gain
                s.src.Play();
            }
            return s;
        }

        // Per-frame re-premix of a persistent loop: distance is re-measured, so walking towards a
        // campfire swells it, and zone changes re-cut it, so stepping into a cave dulls mid-loop
        private void RefreshPersistent(ref Voice voice)
        {
            SoundCue cue = voice.cue;
            if (cue == null || voice.source.src == null) return;

            Vector3? pos = cue.spatial ? (Vector3?)voice.position : null;
            float t = Falloff(cue, pos);

            bool zoned = !cue.ignoreListenerPause;
            ZoneMix listenerZone = zoned ? ListenerZoneMix() : ZoneMix.Neutral;
            ZoneMix emitterZone = zoned && cue.spatial ? EmitterZoneMix(voice.position) : ZoneMix.Neutral;

            voice.baseGain = cue.volume * Mathf.Lerp(1f, cue.minVolume, t)
                * Mathf.Min(listenerZone.volumeScale, emitterZone.volumeScale);
            voice.source.src.volume = Mathf.Clamp01(voice.baseGain
                * SettingsStore.MasterVolume
                * AudioPremix.TrackVolume(cue, SettingsStore.MusicVolume, SettingsStore.SfxVolume));
            voice.source.lpf.cutoffFrequency = AudioPremix.Cutoff(cue, t, listenerZone, emitterZone);
        }

        // Settings changed: re-scale every live voice through its stored baseGain. Persistent
        // voices get the full treatment again next Update anyway; doing the volume here too keeps
        // slider drags instant for them as well
        private void RefreshActiveVolumes()
        {
            for (int i = 0; i < active.Count; i++)
            {
                Voice voice = active[i];
                if (voice.source.src == null || voice.cue == null) continue;
                float track = AudioPremix.TrackVolume(voice.cue, SettingsStore.MusicVolume, SettingsStore.SfxVolume);
                voice.source.src.volume = Mathf.Clamp01(voice.baseGain * SettingsStore.MasterVolume * track);
            }
        }
    }
}
