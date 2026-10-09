#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using System.Reflection;
using Inkform.Audio;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

namespace Inkform.Tests
{
    /// <summary>
    /// Audio system tests. The playback gate (CueLimiter), the volume and rolloff maths
    /// (AudioLevels / AudioSpatial) and the music crossfade (MusicFader) are pure classes tested
    /// directly; voice takeover goes through a real AudioService, and the mixer asset plus its
    /// GameManager wiring are checked from disk.
    /// </summary>
    public sealed class InkformAudioTests
    {
        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
        }

        // ---- CueLimiter: per-Cue cooldown and concurrency ----

        [Test]
        public void CueLimiter_CooldownBlocksRetriggerUntilElapsed()
        {
            var limiter = new CueLimiter();
            SoundCue cue = NewCue(cooldown: 0.1f, maxConcurrent: 8);
            try
            {
                Assert.IsTrue(limiter.CanStart(cue, 10f), "an unseen Cue may always start");
                limiter.Started(cue, 10f);
                Assert.IsFalse(limiter.CanStart(cue, 10.05f), "inside the cooldown");
                Assert.IsTrue(limiter.CanStart(cue, 10.1f), "cooldown elapsed");
            }
            finally { Object.DestroyImmediate(cue); }
        }

        [Test]
        public void CueLimiter_ConcurrencyCapHoldsUntilAVoiceIsReleased()
        {
            var limiter = new CueLimiter();
            SoundCue cue = NewCue(cooldown: 0f, maxConcurrent: 2);
            try
            {
                limiter.Started(cue, 1f);
                limiter.Started(cue, 1f);
                Assert.AreEqual(2, limiter.ActiveCount(cue));
                Assert.IsFalse(limiter.CanStart(cue, 2f), "at the cap");

                limiter.Released(cue);
                Assert.IsTrue(limiter.CanStart(cue, 2f));

                limiter.Released(cue);
                limiter.Released(cue);
                Assert.AreEqual(0, limiter.ActiveCount(cue), "the count never goes negative");
            }
            finally { Object.DestroyImmediate(cue); }
        }

        [Test]
        public void CueLimiter_CriticalBypassesCooldownAndCap()
        {
            var limiter = new CueLimiter();
            SoundCue cue = NewCue(cooldown: 10f, maxConcurrent: 1);
            cue.priority = CuePriority.Critical;
            try
            {
                limiter.Started(cue, 0f);
                Assert.IsTrue(limiter.CanStart(cue, 0f), "death feedback must never be gated");
            }
            finally { Object.DestroyImmediate(cue); }
        }

        [Test]
        public void CueLimiter_PickVictim_LowerLaneFirstThenOldest_NeverAMoreImportantLane()
        {
            var busy = new List<CueLimiter.VoiceFact>
            {
                new CueLimiter.VoiceFact { priority = CuePriority.Gameplay, startedAt = 1f },
                new CueLimiter.VoiceFact { priority = CuePriority.Ambience, startedAt = 5f },
                new CueLimiter.VoiceFact { priority = CuePriority.Ambience, startedAt = 3f },
                new CueLimiter.VoiceFact { priority = CuePriority.Critical, startedAt = 0f },
            };
            Assert.AreEqual(2, CueLimiter.PickVictim(busy, CuePriority.Gameplay),
                "the least important lane goes first, oldest within it");

            var important = new List<CueLimiter.VoiceFact>
            {
                new CueLimiter.VoiceFact { priority = CuePriority.Critical, startedAt = 0f },
                new CueLimiter.VoiceFact { priority = CuePriority.Gameplay, startedAt = 0f },
            };
            Assert.AreEqual(-1, CueLimiter.PickVictim(important, CuePriority.Ambience),
                "an ambience post never takes over anything more important");
            Assert.AreEqual(1, CueLimiter.PickVictim(important, CuePriority.Gameplay),
                "the same lane is fair game");
        }

        // ---- AudioLevels / AudioSpatial: slider dB and the 2D-plane rolloff ----

        [Test]
        public void AudioLevels_LinearToDb_FollowsLogCurveWithFloor()
        {
            Assert.AreEqual(0f, AudioLevels.LinearToDb(1f), 1e-4f);
            Assert.AreEqual(-6.0206f, AudioLevels.LinearToDb(0.5f), 1e-3f);
            Assert.AreEqual(AudioLevels.SilentDb, AudioLevels.LinearToDb(0f));
            Assert.AreEqual(AudioLevels.SilentDb, AudioLevels.LinearToDb(0.00001f));
        }

        [Test]
        public void AudioSpatial_LinearRolloffSpansExactlyTheFalloffRangeOnThePlane()
        {
            const float range = 14f;
            float min = AudioSpatial.MinDistance;
            float max = AudioSpatial.MaxDistanceFor(range);
            float depth = AudioSpatial.PlaneDepth;

            // Unity's linear rolloff: gain = 1 - (d - min) / (max - min), clamped
            float Gain(float planeOffset)
            {
                float d = Mathf.Sqrt(planeOffset * planeOffset + depth * depth);
                return Mathf.Clamp01(1f - (d - min) / (max - min));
            }

            Assert.AreEqual(1f, Gain(0f), 1e-4f, "right under the camera: full volume");
            Assert.AreEqual(0f, Gain(range), 1e-4f, "silent exactly at the Cue's falloff range");
            Assert.Greater(Gain(range * 0.5f), 0f);
            Assert.Less(Gain(range * 0.5f), 1f);
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

        // ---- AudioService: voice takeover when every voice is busy ----

        [Test]
        public void AudioService_FullVoicesTakeOverSameOrLowerLane_AndDropOtherwise()
        {
            GameObject go = NewService(voiceCount: 2, out AudioService service);
            SoundCue gameplay = NewPlayableCue();
            SoundCue critical = NewPlayableCue();
            critical.priority = CuePriority.Critical;
            SoundCue ambience = NewPlayableCue();
            ambience.priority = CuePriority.Ambience;
            try
            {
                AudioService.Play(gameplay);
                AudioService.Play(gameplay);
                Assert.AreEqual(2, service.ActiveVoiceCount);

                AudioService.Play(gameplay);
                Assert.AreEqual(2, service.ActiveCountOf(gameplay), "a same-lane post takes over a busy voice");

                AudioService.Play(critical);
                Assert.AreEqual(1, service.ActiveCountOf(critical));
                Assert.AreEqual(1, service.ActiveCountOf(gameplay), "critical took one gameplay voice");

                AudioService.Play(ambience);
                Assert.AreEqual(0, service.ActiveCountOf(ambience), "nothing less important is busy: dropped");
                Assert.AreEqual(2, service.ActiveVoiceCount);
            }
            finally
            {
                DestroyService(go);
                DestroyCue(gameplay);
                DestroyCue(critical);
                DestroyCue(ambience);
            }
        }

        [Test]
        public void AudioService_StaticEntryPointsAreSilentWithoutAServiceOrCue()
        {
            ResetServiceStatics();
            SoundCue cue = NewPlayableCue();
            try
            {
                Assert.DoesNotThrow(() => AudioService.Play(cue));
                Assert.DoesNotThrow(() => AudioService.Play(null));
                Assert.DoesNotThrow(() => AudioService.PlayMusic(cue));
                Assert.DoesNotThrow(() => AudioService.StopMusic());
                Assert.IsNull(AudioService.OutputFor(cue));
            }
            finally { DestroyCue(cue); }
        }

        // ---- Mixer asset and GameManager wiring ----

        [Test]
        public void Mixer_HasMusicAndSfxUnderMasterWithExposedVolumes()
        {
            AudioMixer mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>("Assets/Audio/Inkform.mixer");
            Assert.IsNotNull(mixer, "Assets/Audio/Inkform.mixer did not import as an AudioMixer");

            Assert.AreEqual(1, mixer.FindMatchingGroups("Master/Music").Length);
            Assert.AreEqual(1, mixer.FindMatchingGroups("Master/Sfx").Length);
            foreach (string param in new[] { "MasterVolume", "MusicVolume", "SfxVolume" })
                Assert.IsTrue(mixer.GetFloat(param, out _), $"exposed parameter {param} is missing");
        }

        [Test]
        public void GameManager_AudioServiceRoutesIntoTheMixerGroups()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Control/GameManager.prefab");
            Assert.IsNotNull(prefab);
            AudioService service = prefab.GetComponent<AudioService>();
            Assert.IsNotNull(service, "GameManager lost its AudioService");

            Assert.IsNotNull(GetField<AudioMixer>(service, "mixer"));
            Assert.AreEqual("Music", GetField<AudioMixerGroup>(service, "musicGroup")?.name);
            Assert.AreEqual("Sfx", GetField<AudioMixerGroup>(service, "sfxGroup")?.name);
        }

        // ---- helpers, same reflection idiom as InkformRuntimeEdgeTests ----

        private static SoundCue NewCue(float cooldown = 0.05f, int maxConcurrent = 3)
        {
            SoundCue cue = ScriptableObject.CreateInstance<SoundCue>();
            cue.cooldown = cooldown;
            cue.maxConcurrent = maxConcurrent;
            return cue;
        }

        private static SoundCue NewPlayableCue()
        {
            SoundCue cue = NewCue(cooldown: 0f, maxConcurrent: 8);
            cue.clips = new[] { AudioClip.Create("audio-test", 32, 1, 8000, false) };
            return cue;
        }

        private static void DestroyCue(SoundCue cue)
        {
            if (cue.clips != null)
                foreach (AudioClip clip in cue.clips) Object.DestroyImmediate(clip);
            Object.DestroyImmediate(cue);
        }

        private static GameObject NewService(int voiceCount, out AudioService service)
        {
            GameObject go = new GameObject("AudioService test");
            go.SetActive(false);
            service = go.AddComponent<AudioService>();
            SetField(service, "voiceCount", voiceCount);
            ResetServiceStatics();
            go.SetActive(true);
            // Edit mode may skip Awake for plain MonoBehaviours; run it once if activation did not
            if (AudioService.Instance != service) Invoke(service, "Awake");
            return go;
        }

        private static void DestroyService(GameObject go)
        {
            Object.DestroyImmediate(go);
            ResetServiceStatics();      // OnDestroy may not run in edit mode either
        }

        private static void ResetServiceStatics() =>
            typeof(AudioService).GetMethod("ResetStatics", BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, null);

        private static void Invoke(object target, string name) =>
            target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.Invoke(target, null);

        private static void SetField(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(target, value);

        private static T GetField<T>(object target, string name) where T : class =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(target) as T;
    }
}
#endif
