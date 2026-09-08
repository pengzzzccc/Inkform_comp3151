#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.Reflection;
using Inkform.Audio;
using NUnit.Framework;
using UnityEngine;

namespace Inkform.Tests
{
    /// <summary>
    /// Audio system edge tests. The audible maths (AudioPremix), the capacity decisions
    /// (VoiceArbiter), the zone blend (ZoneMixer) and the music crossfade (MusicFader) are pure
    /// classes and tested directly; only the pool grow/steal behavior goes through the real
    /// AudioManager, using the same reflection idiom as InkformRuntimeEdgeTests.
    /// </summary>
    public sealed class InkformAudioTests
    {
        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
        }

        // ---- VoiceArbiter: where the next source comes from ----

        [Test]
        public void VoiceArbiter_PooledFirst_ThenGrow_ThenSteal()
        {
            Assert.AreEqual(VoiceArbiter.Acquire.UsePooled,
                VoiceArbiter.DecideAcquire(pooledCount: 3, liveCount: 10, hardCap: 48));
            Assert.AreEqual(VoiceArbiter.Acquire.Grow,
                VoiceArbiter.DecideAcquire(pooledCount: 0, liveCount: 10, hardCap: 48));
            Assert.AreEqual(VoiceArbiter.Acquire.Steal,
                VoiceArbiter.DecideAcquire(pooledCount: 0, liveCount: 48, hardCap: 48));
        }

        [Test]
        public void VoiceArbiter_IncomingStealsStrictlyLowerPriority_EvenWhenFresh()
        {
            var live = new List<VoiceFact>
            {
                new VoiceFact { priority = CuePriority.Gameplay, startedAt = 100f, persistent = false },
                new VoiceFact { priority = CuePriority.Ambience, startedAt = 100.05f, persistent = true },
            };
            // No age gate protects a lower lane: Critical feedback must always get through
            int victim = VoiceArbiter.PickVictim(live, CuePriority.Critical, now: 100.1f);
            Assert.AreEqual(1, victim, "the lower-priority voice is the victim regardless of age");
        }

        [Test]
        public void VoiceArbiter_LowerLanePrefersLowestThenTransientThenOldest()
        {
            var live = new List<VoiceFact>
            {
                new VoiceFact { priority = CuePriority.Ambience, startedAt = 5f, persistent = false },  // oldest transient
                new VoiceFact { priority = CuePriority.Ambience, startedAt = 7f, persistent = false },
                new VoiceFact { priority = CuePriority.Ambience, startedAt = 4f, persistent = true },   // oldest, but persistent
            };
            int victim = VoiceArbiter.PickVictim(live, CuePriority.Gameplay, now: 100f);
            Assert.AreEqual(0, victim, "transients are evicted before persistent loops of the same lane");
        }

        [Test]
        public void VoiceArbiter_SameLaneRequiresMinAge_AndPicksOldest()
        {
            var live = new List<VoiceFact>
            {
                new VoiceFact { priority = CuePriority.Gameplay, startedAt = 9.95f },  // 0.05s old: protected
            };
            Assert.AreEqual(-1, VoiceArbiter.PickVictim(live, CuePriority.Gameplay, now: 10f),
                "a same-lane voice younger than MinStealAge is never cannibalized");

            live.Add(new VoiceFact { priority = CuePriority.Gameplay, startedAt = 9.5f });   // 0.5s old
            live.Add(new VoiceFact { priority = CuePriority.Gameplay, startedAt = 9.8f });   // 0.2s old
            Assert.AreEqual(1, VoiceArbiter.PickVictim(live, CuePriority.Gameplay, now: 10f),
                "among qualifying same-lane voices the oldest is stolen");
        }

        [Test]
        public void VoiceArbiter_HigherLaneIsUntouchable()
        {
            var live = new List<VoiceFact>
            {
                new VoiceFact { priority = CuePriority.Critical, startedAt = 0f },
            };
            Assert.AreEqual(-1, VoiceArbiter.PickVictim(live, CuePriority.Gameplay, now: 100f));
        }

        // ---- AudioPremix: the audible maths, including the strictest-wins zone combination ----

        private static SoundCue NewCue(float volume = 1f, float minVolume = 0.15f, float minCutoff = 900f,
            bool spatial = false, float falloffRange = 20f, float reverbAmount = 1f,
            SoundCue.Category category = SoundCue.Category.Sfx)
        {
            SoundCue cue = ScriptableObject.CreateInstance<SoundCue>();
            cue.volume = volume;
            cue.minVolume = minVolume;
            cue.minCutoff = minCutoff;
            cue.spatial = spatial;
            cue.falloffRange = falloffRange;
            cue.reverbAmount = reverbAmount;
            cue.category = category;
            return cue;
        }

        [Test]
        public void Premix_DistanceT_MeasuresXYPlaneAndClamps()
        {
            // The camera sits at z = -10; counting z would attenuate even sounds at the player's feet.
            // The XY ratio is eased in quadratically (10/20 → 0.25, not 0.5): near sounds hold close
            // to full gain and the fall concentrates in the outer half of the range.
            Assert.AreEqual(0.25f, AudioPremix.DistanceT(true, 20f, new Vector2(0f, 0f), new Vector2(10f, 0f)), 1e-4f);
            Assert.AreEqual(0.25f, AudioPremix.DistanceT(true, 20f, new Vector2(0f, 0f),
                new Vector3(10f, 0f, -50f)), 1e-4f, "z must not contribute");
            Assert.AreEqual(1f, AudioPremix.DistanceT(true, 20f, new Vector2(0f, 0f), new Vector2(30f, 0f)), 1e-4f,
                "clamped to 1 before easing, so beyond the range it stays fully attenuated");
            Assert.AreEqual(0f, AudioPremix.DistanceT(false, 20f, new Vector2(0f, 0f), new Vector2(30f, 0f)),
                "non-spatial cues are always 'at the ear'");
        }

        [Test]
        public void Premix_VolumeTakesStricterZoneScale()
        {
            SoundCue cue = NewCue(volume: 1f, minVolume: 0.5f);
            ZoneMix cave = new ZoneMix(0.5f, 3000f, 0f);
            ZoneMix emitter = new ZoneMix(0.2f, 22000f, 0f);

            float neutral = AudioPremix.Volume(cue, t: 1f, master: 1f, track: 1f,
                ZoneMix.Neutral, ZoneMix.Neutral);
            Assert.AreEqual(0.5f, neutral, 1e-4f, "at full distance volume bottoms out at minVolume");

            float zoned = AudioPremix.Volume(cue, t: 1f, master: 1f, track: 1f, cave, emitter);
            Assert.AreEqual(0.5f * 0.2f, zoned, 1e-4f, "the stricter of the two zone scales wins");
        }

        [Test]
        public void Premix_CutoffTakesStrictestOfListenerZone_EmitterZone_AndDistance()
        {
            SoundCue cue = NewCue(minCutoff: 900f, spatial: true);
            ZoneMix listenerCave = new ZoneMix(1f, 5000f, 0f);
            ZoneMix emitterCave = new ZoneMix(1f, 3000f, 0f);

            Assert.AreEqual(3000f, AudioPremix.Cutoff(cue, t: 0f, listenerCave, emitterCave), 1e-3f,
                "no distance falloff yet, the emitter's cave is the strictest cap");
            Assert.AreEqual(900f, AudioPremix.Cutoff(cue, t: 1f, listenerCave, emitterCave), 1e-3f,
                "at full distance the cue's own curve is strictest");

            SoundCue flat = NewCue(spatial: false);
            Assert.AreEqual(5000f, AudioPremix.Cutoff(flat, t: 1f, listenerCave, ZoneMix.Neutral), 1e-3f,
                "non-spatial sounds take the listener zone only — a cave dulls even the player's own steps");
        }

        [Test]
        public void Premix_WetToHundredthsDb_FollowsLogCurveWithFloor()
        {
            // hundredths-of-dB mapping for AudioReverbFilter level fields: full wet = 0, halving
            // = -6 dB, silence floors at -100 dB — the mapping the listener filter is driven with
            Assert.AreEqual(0f, AudioPremix.WetToHundredthsDb(1f), 0.01f);
            Assert.AreEqual(-602.06f, AudioPremix.WetToHundredthsDb(0.5f), 1f);
            Assert.AreEqual(-4000f, AudioPremix.WetToHundredthsDb(0.01f), 1f);
            Assert.AreEqual(-10000f, AudioPremix.WetToHundredthsDb(0f), 0.01f);
        }

        [Test]
        public void Premix_TrackVolume_SelectsSettingsTrackByCategory()
        {
            Assert.AreEqual(0.7f, AudioPremix.TrackVolume(NewCue(category: SoundCue.Category.Music), 0.7f, 0.3f));
            Assert.AreEqual(0.3f, AudioPremix.TrackVolume(NewCue(category: SoundCue.Category.Sfx), 0.7f, 0.3f));
        }

        // ---- ZoneMixer: stack + strictest-wins blend ----

        private static readonly ZoneMix Cave = new ZoneMix(0.5f, 3000f, 0.6f);
        private static readonly ZoneMix Hall = new ZoneMix(0.9f, 6000f, 0.4f);

        [Test]
        public void ZoneMixer_PushBlendsOverTime_PopReturns()
        {
            var mixer = new ZoneMixer();
            var token = new object();
            mixer.Push(token, Cave, blendIn: 1f);
            Assert.IsTrue(mixer.InZone);

            mixer.Tick(0.5f);
            Assert.AreEqual(0.75f, mixer.ListenerState.volumeScale, 1e-3f, "halfway from 1.0 to 0.5");
            Assert.AreEqual(12500f, mixer.ListenerState.cutoff, 1f, "halfway from 22000 to 3000");

            mixer.Tick(0.5f);
            Assert.AreEqual(Cave.cutoff, mixer.ListenerState.cutoff, 1f, "blend completes at the zone target");

            mixer.Pop(token, blendOut: 1f);
            mixer.Tick(1f);
            Assert.AreEqual(ZoneMix.Neutral.cutoff, mixer.ListenerState.cutoff, 1f);
            Assert.AreEqual(0f, mixer.ListenerState.reverbWet, 1e-4f);
            Assert.IsFalse(mixer.InZone);
        }

        [Test]
        public void ZoneMixer_OverlappingZonesCombineStrictest()
        {
            var mixer = new ZoneMixer();
            mixer.Push(new object(), Cave, 0f);     // blend 0 = snap
            mixer.Push(new object(), Hall, 0f);
            ZoneMix state = mixer.ListenerState;
            Assert.AreEqual(0.45f, state.volumeScale, 1e-3f, "scales multiply");
            Assert.AreEqual(3000f, state.cutoff, 1f, "cutoffs take the min");
            Assert.AreEqual(0.6f, state.reverbWet, 1e-3f, "wet takes the max");

            mixer.Pop(new object(), 0f);            // unknown token: no-op
            Assert.AreEqual(3000f, mixer.ListenerState.cutoff, 1f);
        }

        [Test]
        public void ZoneMixer_RePushReplaces_DoesNotStack()
        {
            var mixer = new ZoneMixer();
            var token = new object();
            mixer.Push(token, Cave, 0f);
            mixer.Push(token, Hall, 0f);            // same token re-entered
            Assert.AreEqual(Hall.cutoff, mixer.ListenerState.cutoff, 1f,
                "the second push replaces the first, it does not double-count");
        }

        [Test]
        public void ZoneMixer_ClearSnapsToNeutral()
        {
            var mixer = new ZoneMixer();
            mixer.Push(new object(), Cave, 5f);     // long blend pending
            mixer.Clear();
            Assert.IsFalse(mixer.InZone);
            Assert.AreEqual(ZoneMix.Neutral.cutoff, mixer.ListenerState.cutoff, 1f,
                "a scene cut must not keep fading towards a zone that no longer exists");
        }

        // ---- MusicFader: crossfade state machine ----

        [Test]
        public void MusicFader_CrossfadesSlotsAndStopsTheOutgoingClip()
        {
            var fader = new MusicFader();
            SoundCue a = NewCue();
            SoundCue b = NewCue();

            int slotA = fader.Request(a, fadeSeconds: 1f);
            Assert.AreEqual(0, slotA, "the first track lands on slot 0");

            fader.Tick(1f, out float v0, out float v1, out bool stop0, out bool stop1);
            Assert.AreEqual(1f, v0, 1e-3f);

            int slotB = fader.Request(b, 1f);
            Assert.AreEqual(1, slotB, "the second track takes the other physical source");

            fader.Tick(0.5f, out v0, out v1, out stop0, out stop1);
            Assert.AreEqual(0.5f, v0, 1e-3f, "outgoing fades down");
            Assert.AreEqual(0.5f, v1, 1e-3f, "incoming fades up");

            fader.Tick(0.5f, out v0, out v1, out stop0, out stop1);
            Assert.AreEqual(0f, v0, 1e-3f);
            Assert.IsTrue(stop0, "the finished slot reports stop so its clip is freed");
            Assert.AreEqual(1f, v1, 1e-3f);
        }

        [Test]
        public void MusicFader_SameTrackIsNoOp_SceneReentryKeepsPlaying()
        {
            var fader = new MusicFader();
            SoundCue a = NewCue();
            fader.Request(a, 1f);
            fader.Tick(1f, out _, out _, out _, out _);
            Assert.AreEqual(-1, fader.Request(a, 1f), "re-requesting the stable track changes nothing");
            Assert.AreSame(a, fader.Current);
        }

        [Test]
        public void MusicFader_StopFadesToSilence()
        {
            var fader = new MusicFader();
            SoundCue a = NewCue();
            fader.Request(a, 1f);
            fader.Tick(1f, out _, out _, out _, out _);

            fader.Stop(1f);
            fader.Tick(0.5f, out float v0, out _, out _, out _);
            Assert.AreEqual(0.5f, v0, 1e-3f, "the track fades instead of cutting");

            fader.Tick(0.5f, out v0, out _, out _, out _);
            Assert.AreEqual(0f, v0, 1e-3f);
            Assert.IsNull(fader.Current);
        }

        [Test]
        public void MusicFader_RequestingTheOutgoingTrackMidFadeReversesWithoutClipSwap()
        {
            var fader = new MusicFader();
            SoundCue a = NewCue();
            SoundCue b = NewCue();
            fader.Request(a, 1f);
            fader.Tick(1f, out _, out _, out _, out _);
            fader.Request(b, 1f);           // a is now fading out
            fader.Tick(0.5f, out _, out _, out _, out _);

            Assert.AreEqual(-1, fader.Request(a, 1f),
                "wanting the outgoing track back must not hand out a new clip slot");
            fader.Tick(0.5f, out float v0, out float v1, out _, out _);
            Assert.Greater(v0, 0f, "the outgoing track is coming back");
            Assert.Less(v1, 1f, "the interrupting track is fading away");
            Assert.AreSame(a, fader.Current);
        }

        // ---- AudioManager: bounded growth, steal at the cap, drop only when unstealable ----

        [Test]
        public void AudioManager_GrowsToHardCapThenDropsWithCounter()
        {
            GameObject go = NewManager(out AudioManager manager, poolSize: 2, hardCap: 4);
            SoundCue cue = NewPlayableCue();
            try
            {
                for (int i = 0; i < 4; i++)
                    Assert.IsNotNull(manager.Play(cue), $"play {i} must find a source below the cap");
                Assert.AreEqual(4, cue.activeCount);

                // At the cap, same-lane voices younger than MinStealAge are protected → strategic drop
                Assert.IsNull(manager.Play(cue), "the 5th same-lane post at the cap is dropped");
                Assert.AreEqual(1, GetField<int>(manager, "droppedPosts"));
            }
            finally
            {
                DestroyManager(go, manager);
                UnityEngine.Object.DestroyImmediate(cue);
            }
        }

        [Test]
        public void AudioManager_CriticalStealsGameplayAtCap()
        {
            GameObject go = NewManager(out AudioManager manager, poolSize: 2, hardCap: 4);
            SoundCue gameplay = NewPlayableCue();
            SoundCue critical = NewPlayableCue();
            critical.priority = CuePriority.Critical;
            try
            {
                for (int i = 0; i < 4; i++) Assert.IsNotNull(manager.Play(gameplay));
                Assert.AreEqual(4, gameplay.activeCount);

                // "Full pool, death sound must still be heard": Critical evicts the oldest lower lane
                Assert.IsNotNull(manager.Post(new AudioPost(critical)));
                Assert.AreEqual(3, gameplay.activeCount, "the stolen voice returns its concurrency slot");
                Assert.AreEqual(1, critical.activeCount);
                Assert.AreEqual(1, GetField<int>(manager, "stolenVoices"));
            }
            finally
            {
                DestroyManager(go, manager);
                UnityEngine.Object.DestroyImmediate(gameplay);
                UnityEngine.Object.DestroyImmediate(critical);
            }
        }

        [Test]
        public void AudioManager_NearestFirst_ReplacesFarVoiceWhenCooldownBlocks()
        {
            GameObject go = NewManager(out AudioManager manager, poolSize: 2, hardCap: 4);
            GameObject ear = NewListenerAt(manager, Vector3.zero);
            SoundCue cue = NewNearestFirstCue();
            cue.cooldown = 10f;
            try
            {
                AudioSource far = manager.Play(cue, new Vector3(80f, 0f, 0f));
                AudioSource near = manager.Play(cue, new Vector3(2f, 0f, 0f));

                Assert.IsNotNull(far);
                Assert.IsNotNull(near, "a nearer post supersedes the cooldown-blocked far voice");
                Assert.AreEqual(1, manager.ActiveVoiceCount);
                Assert.AreEqual(1, cue.activeCount, "replacement releases then reclaims one Cue slot");
                Assert.AreEqual(1, GetField<int>(manager, "stolenVoices"));
                Assert.IsNull(far.clip, "the superseded source is stopped and returned to the pool");

                Assert.IsNull(manager.Play(cue, new Vector3(90f, 0f, 0f)),
                    "a farther post cannot displace the nearer voice during cooldown");
                Assert.AreEqual(1, manager.ActiveVoiceCount);
            }
            finally
            {
                DestroyManager(go, manager);
                UnityEngine.Object.DestroyImmediate(ear);
                UnityEngine.Object.DestroyImmediate(cue);
            }
        }

        [Test]
        public void AudioManager_NearestFirst_ReplacesFarthestAtCueLimit()
        {
            GameObject go = NewManager(out AudioManager manager, poolSize: 2, hardCap: 4);
            GameObject ear = NewListenerAt(manager, Vector3.zero);
            SoundCue cue = NewNearestFirstCue();
            cue.maxConcurrent = 2;
            try
            {
                AudioSource farthest = manager.Play(cue, new Vector3(80f, 0f, 0f));
                AudioSource middle = manager.Play(cue, new Vector3(60f, 0f, 0f));
                AudioSource near = manager.Play(cue, new Vector3(5f, 0f, 0f));

                Assert.IsNotNull(farthest);
                Assert.IsNotNull(middle);
                Assert.AreSame(farthest, near,
                    "with no pooled source, the farthest voice's source is recycled for the nearer post");
                Assert.AreEqual(2, manager.ActiveVoiceCount);
                Assert.AreEqual(2, cue.activeCount);
                Assert.AreEqual(1, GetField<int>(manager, "stolenVoices"));

                Assert.IsNull(manager.Play(cue, new Vector3(90f, 0f, 0f)),
                    "a farther post is still dropped at the per-Cue limit");
                Assert.AreEqual(2, manager.ActiveVoiceCount);
            }
            finally
            {
                DestroyManager(go, manager);
                UnityEngine.Object.DestroyImmediate(ear);
                UnityEngine.Object.DestroyImmediate(cue);
            }
        }

        [Test]
        public void AudioManager_NearestFirst_BypassesYoungVoiceProtectionAtHardCap()
        {
            GameObject go = NewManager(out AudioManager manager, poolSize: 1, hardCap: 1);
            GameObject ear = NewListenerAt(manager, Vector3.zero);
            SoundCue cue = NewNearestFirstCue();
            try
            {
                AudioSource far = manager.Play(cue, new Vector3(80f, 0f, 0f));
                AudioSource near = manager.Play(cue, new Vector3(2f, 0f, 0f));

                Assert.AreSame(far, near,
                    "nearest-first may recycle its own far voice even before MinStealAge");
                Assert.AreEqual(1, manager.ActiveVoiceCount);
                Assert.AreEqual(1, cue.activeCount);
                Assert.AreEqual(1, GetField<int>(manager, "stolenVoices"));
                Assert.AreEqual(0, GetField<int>(manager, "droppedPosts"));
            }
            finally
            {
                DestroyManager(go, manager);
                UnityEngine.Object.DestroyImmediate(ear);
                UnityEngine.Object.DestroyImmediate(cue);
            }
        }

        [Test]
        public void AudioManager_NearestFirst_FirstPostAtHardCapKeepsPriorityArbitration()
        {
            GameObject go = NewManager(out AudioManager manager, poolSize: 1, hardCap: 1);
            GameObject ear = NewListenerAt(manager, Vector3.zero);
            SoundCue ambience = NewPlayableCue();
            SoundCue blast = NewNearestFirstCue();
            ambience.priority = CuePriority.Ambience;
            try
            {
                AudioSource ambienceSource = manager.Play(ambience);
                AudioSource blastSource = manager.Play(blast, new Vector3(2f, 0f, 0f));

                Assert.AreSame(ambienceSource, blastSource,
                    "with no existing Blast voice, its first post still steals a lower-priority voice");
                Assert.AreEqual(0, ambience.activeCount);
                Assert.AreEqual(1, blast.activeCount);
                Assert.AreEqual(1, GetField<int>(manager, "stolenVoices"));
            }
            finally
            {
                DestroyManager(go, manager);
                UnityEngine.Object.DestroyImmediate(ear);
                UnityEngine.Object.DestroyImmediate(ambience);
                UnityEngine.Object.DestroyImmediate(blast);
            }
        }

        [Test]
        public void AudioManager_NearestFirst_DoesNotChangeUnsupportedCueBehavior()
        {
            GameObject go = NewManager(out AudioManager manager, poolSize: 3, hardCap: 4);
            GameObject ear = NewListenerAt(manager, Vector3.zero);
            SoundCue nonSpatial = NewPlayableCue();
            SoundCue positionless = NewNearestFirstCue();
            nonSpatial.preferNearestWhenLimited = true;
            nonSpatial.cooldown = 10f;
            positionless.cooldown = 10f;
            try
            {
                Assert.IsNotNull(manager.Play(nonSpatial, new Vector3(80f, 0f, 0f)));
                Assert.IsNull(manager.Play(nonSpatial, new Vector3(2f, 0f, 0f)),
                    "a non-spatial Cue keeps first-come cooldown behavior");

                Assert.IsNotNull(manager.Play(positionless));
                Assert.IsNull(manager.Play(positionless),
                    "a positionless post cannot participate in nearest-first replacement");
                Assert.AreEqual(2, manager.ActiveVoiceCount);
                Assert.AreEqual(0, GetField<int>(manager, "stolenVoices"));
            }
            finally
            {
                DestroyManager(go, manager);
                UnityEngine.Object.DestroyImmediate(ear);
                UnityEngine.Object.DestroyImmediate(nonSpatial);
                UnityEngine.Object.DestroyImmediate(positionless);
            }
        }

        // ---- helpers, same reflection idiom as InkformRuntimeEdgeTests ----

        private static GameObject NewManager(out AudioManager manager, int poolSize, int hardCap)
        {
            GameObject go = new GameObject("AudioManager arbitration test");
            go.SetActive(false);
            manager = go.AddComponent<AudioManager>();
            SetField(manager, "poolSize", poolSize);
            SetField(manager, "hardCap", hardCap);
            SetField(manager, "growthStep", hardCap);   // one growth step covers the whole test cap
            // Activation runs Awake/OnEnable exactly once (adding while inactive defers them);
            // re-invoking them by hand would build the pool twice and mask the growth path
            go.SetActive(true);
            return go;
        }

        private static void DestroyManager(GameObject go, AudioManager manager)
        {
            UnityEngine.Object.DestroyImmediate(go);    // OnDisable/OnDestroy run as part of teardown
        }

        private static SoundCue NewPlayableCue()
        {
            SoundCue cue = ScriptableObject.CreateInstance<SoundCue>();
            AudioClip clip = AudioClip.Create("audio-test", 32, 1, 8000, false);
            cue.clips = new[] { clip };
            cue.cooldown = 0f;
            cue.maxConcurrent = 8;
            return cue;     // the clip is destroyed with the cue's DestroyImmediate
        }

        private static SoundCue NewNearestFirstCue()
        {
            SoundCue cue = NewPlayableCue();
            cue.spatial = true;
            cue.falloffRange = 100f;
            cue.preferNearestWhenLimited = true;
            return cue;
        }

        private static GameObject NewListenerAt(AudioManager manager, Vector3 position)
        {
            GameObject ear = new GameObject("AudioManager test listener");
            ear.transform.position = position;
            // AudioManager only needs the listener Transform for its XY distance calculation. Injecting
            // it avoids creating a second real AudioListener when a test scene already carries one.
            SetField(manager, "listenerCache", ear.transform);
            return ear;
        }

        private static void SetField(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(target, value);

        private static T GetField<T>(object target, string name) =>
            (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(target);

        private static void Invoke(object target, string method) =>
            target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(target, null);
    }
}
#endif
