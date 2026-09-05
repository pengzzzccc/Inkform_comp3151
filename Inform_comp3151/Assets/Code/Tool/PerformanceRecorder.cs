using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Inkform.Audio;
using Inkform.Bus;
using Inkform.Fx;
using Inkform.Level;
using Inkform.Life;
using Inkform.Player;
using Inkform.Settings;
using UnityEngine;
using Unity.Profiling;

// Process (System.Diagnostics) drags its own Debug in; without this alias every bare Debug. call
// is ambiguous between it and UnityEngine.Debug.
using Debug = UnityEngine.Debug;

namespace Inkform.Tool
{
    /// <summary>
    /// Session performance recorder: samples once per SettingsStore.PerfInterval (Graphics tab's
    /// "Sample Rate", 0.1–5 s) whenever SettingsStore.PerfRecording is on, writing one CSV row per
    /// sample until shutdown — frame pacing plus where the player was and what state they were in
    /// when it got slow. Toggling the setting mid-run closes the current file (with its summary)
    /// and opens a fresh one, so each recorded stretch is a self-contained session.
    ///
    /// Output location follows one rule: an existing project-root Log/ folder wins; otherwise a
    /// PerfLogs folder is created under Docs/ and used. The session header stamps the git branch and
    /// commit (resolved by shelling out once at startup; a build machine without git records
    /// "unknown" rather than failing), because a perf file nobody can pin to a code revision is only
    /// half evidence.
    ///
    /// Attach to GameManager: the host survives scene switches (PersistentGameRoot keeps it alive),
    /// duplicate copies arriving with each new scene are destroyed together with their host
    /// (PersistentGameRoot's dedup) — the active-instance guard below is belt-and-braces for Awake
    /// ordering edge cases.
    ///
    /// All timing is unscaled so hitstop (timeScale = 0) never freezes or divides by zero; per-frame
    /// costs are aggregated over the sample window before being written, which is what makes rows of
    /// different sessions comparable.
    /// </summary>
    public sealed class PerformanceRecorder : MonoBehaviour
    {
        private const float HitchThresholdSeconds = 1f / 30f;   // a frame that misses 30 fps is a hitch
        private const float PausedGapSeconds = 1f;              // larger = sleep/minimize gap, not a frame
        private const string HeadlessFallbackFolder = "PerfLogs";

        // Sampling cadence lives in SettingsStore (Graphics tab: Performance Log + Sample Rate);
        // this cached copy is re-synced whenever the settings change
        private float sampleInterval = 1f;
        private bool recording;     // a session file is open and being written

        private static PerformanceRecorder active;

        private StreamWriter writer;
        private bool streamFailed;

        // Window aggregation: unscaled frame time summed over the interval, plus fps extremes within it
        private float windowTime;
        private int windowFrames;
        private float windowMinFps = float.MaxValue;
        private float windowMaxFps;

        // Frame pacing extremes: the worst single frame of the window and how many frames blew past
        // the 30 fps line — avg_ms hides a 45 ms spike behind a 9 ms average, these two don't
        private float windowMaxFrameMs;
        private int windowHitchCount;
        private long hitchTotal;

        // GC sampling baseline: the window's managed allocation delta and Gen0 collection count.
        // The delta reads negative across a collection — that's what gc_collects explains.
        private long lastGcBytes;
        private long lastGcCollects;

        // Scene-system lookups for the context columns, re-resolved via Unity's fake-null after the
        // scene (and their owners) change — same lazy pattern as RespawnDirector.deathCache
        private LevelMemento mementoCache;
        private CamHandler camCache;

        // Render statistics need the profiler: absent from release-player builds, so those log -1
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private ProfilerRecorder drawCallsRecorder;
        private ProfilerRecorder setPassRecorder;
        private ProfilerRecorder trianglesRecorder;
#endif

        // Whole-session totals for the closing summary line
        private double totalSeconds;
        private long totalFrames;
        private float sessionMinFps = float.MaxValue;
        private double totalFrameTimeSum;
        private float startRealtime;
        private string lastSceneName = "";

        // The screen/targetFps line is written at the first sample rather than at Awake: settings
        // apply around the same BeforeSceneLoad tick, and the first sample reports what the session
        // actually ran at, not what was briefly in effect during boot
        private bool environmentLineWritten;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => active = null;

        void Awake()
        {
            if (active != null && active != this)
            {
                enabled = false;     // a copy arriving with its scene while the owner lives on
                return;
            }
            active = this;
            sampleInterval = SettingsStore.PerfInterval;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // "Draw Calls Count" reads 0 on Unity 6000.4 (the counter name no longer resolves);
            // "Batches Count" is the same story post-batching and does resolve. Valid-guarded reads
            // mean an unavailable counter logs -1 ("not measurable") instead of a lying zero.
            drawCallsRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            setPassRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            trianglesRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
#endif
            if (SettingsStore.PerfRecording) StartRecording();
        }

        void OnEnable()
        {
            SettingsStore.Changed += OnSettingsChanged;
        }

        void OnDisable()
        {
            SettingsStore.Changed -= OnSettingsChanged;
        }

        void OnDestroy()
        {
            if (active != this) return;
            active = null;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            drawCallsRecorder.Dispose();
            setPassRecorder.Dispose();
            trianglesRecorder.Dispose();
#endif
            if (recording)
            {
                LevelBus.Started -= OnSceneStarted;
                WriteSummary();
                CloseStream();
                recording = false;
            }
        }

        // ---- Settings-driven recording state ----

        private void OnSettingsChanged()
        {
            sampleInterval = SettingsStore.PerfInterval;
            ResetWindow();      // a mid-window interval change would make that one row lopsided

            if (SettingsStore.PerfRecording && !recording) StartRecording();
            else if (!SettingsStore.PerfRecording && recording) StopRecording();
        }

        /// <summary>Opens a session file and starts sampling. No-op while already recording; a
        /// failed open (unwritable directory) leaves recording off and the recorder idle-but-alive,
        /// so a later settings change can try again.</summary>
        private void StartRecording()
        {
            if (recording) return;

            OpenSession();
            if (writer == null) return;     // OpenSession warned; recording stays off

            recording = true;
            lastGcBytes = GC.GetTotalMemory(false);
            lastGcCollects = GC.CollectionCount(0);
            lastSceneName = SceneManagerSceneName();
            LevelBus.Started += OnSceneStarted;
        }

        /// <summary>Closes the session: a summary line, the scene-marker unsubscribe, the stream.</summary>
        private void StopRecording()
        {
            if (!recording) return;
            recording = false;

            LevelBus.Started -= OnSceneStarted;
            WriteSummary();
            CloseStream();
            ResetWindow();
        }

        private void ResetWindow()
        {
            windowTime = 0f;
            windowFrames = 0;
            windowMinFps = float.MaxValue;
            windowMaxFps = 0f;
            windowMaxFrameMs = 0f;
            windowHitchCount = 0;
        }

        void Update()
        {
            if (writer == null) return;

            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f) return;

            // A sleep or minimized-window gap reports as one giant unscaled delta; counting it would
            // poison every average (a 30 s "frame" has been logged as max_frame_ms before). Drop the
            // partial window and wait for real frames.
            if (dt > PausedGapSeconds)
            {
                ResetWindow();
                return;
            }

            int fps = Mathf.RoundToInt(1f / dt);
            if (fps < windowMinFps) windowMinFps = fps;
            if (fps > windowMaxFps) windowMaxFps = fps;

            float frameMs = dt * 1000f;
            if (frameMs > windowMaxFrameMs) windowMaxFrameMs = frameMs;
            if (dt > HitchThresholdSeconds) { windowHitchCount++; hitchTotal++; }

            windowTime += dt;
            windowFrames++;
            if (windowTime < sampleInterval) return;

            WriteSample();
            ResetWindow();
        }

        // ---- session lifecycle ----

        private void OpenSession()
        {
            try
            {
                string dir = ResolveOutputDir();

                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir,
                    $"perf_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

                writer = new StreamWriter(path, append: false, Encoding.UTF8);
                streamFailed = false;   // an earlier failed open must not make CloseStream skip this one

                WriteHeader();

                startRealtime = Time.realtimeSinceStartup;
                lastSceneName = SceneManagerSceneName();
                lastGcBytes = GC.GetTotalMemory(false);
                lastGcCollects = GC.CollectionCount(0);
                Debug.Log($"PerformanceRecorder: logging to {path}", this);
            }
            catch (Exception e)
            {
                streamFailed = true;
                writer = null;
                // Deliberately NOT disabling the component: the settings toggle must stay able to
                // retry (a directory that was unwritable at boot can come back). Recording stays
                // off; Update idles on its writer == null guard.
                Debug.LogWarning($"PerformanceRecorder: could not open log file, recording disabled ({e.Message})", this);
            }
        }

        // An existing project-root Log/ folder wins outright; with none, Docs gets a PerfLogs subfolder
        // created and becomes home. "Project root" is Assets' parent, where Unity itself puts Logs/.
        private static string ResolveOutputDir()
        {
            string root = Path.GetDirectoryName(Application.dataPath);
            string logDir = Path.Combine(root, "Log");
            if (Directory.Exists(logDir)) return logDir;

            return Path.Combine(root, "Docs", HeadlessFallbackFolder);
        }

        private void WriteHeader()
        {
            writer.WriteLine("# Inkform performance log");
            writer.WriteLine($"# startedAt={DateTime.Now:yyyy-MM-dd'T'HH:mm:ss}");
            writer.WriteLine($"# branch={RunGit("rev-parse --abbrev-ref HEAD")}");
            writer.WriteLine($"# commit={RunGit("rev-parse HEAD")}");
            writer.WriteLine($"# unity={Application.unityVersion} platform={Application.platform}");
            writer.WriteLine($"# device={SystemInfo.deviceModel} os={SystemInfo.operatingSystem}");
            writer.WriteLine($"# gpu={SystemInfo.graphicsDeviceName} sysMemMB={SystemInfo.systemMemorySize}"
                           + $" quality={QualitySettings.names[QualitySettings.GetQualityLevel()]}");
            writer.WriteLine("time_s,scene,fps_avg,fps_min,fps_max,frame_ms,gc_mb,x,y,state,face,"
                           + "game_state,hitches,max_frame_ms,gc_alloc_kb,gc_collects,scene_load_ms,"
                           + "voices,deaths,rope,mementos,cam_mode,draw_calls,set_pass,tris");
            writer.Flush();
        }

        private void WriteSample()
        {
            if (writer == null) return;

            if (!environmentLineWritten)
            {
                environmentLineWritten = true;
                writer.WriteLine($"# screen={Screen.currentResolution.width}x{Screen.currentResolution.height}"
                               + $" targetFps={Application.targetFrameRate} vSync={QualitySettings.vSyncCount}");
            }

            double now = Time.realtimeSinceStartup - startRealtime;
            float avgFps = windowFrames / Mathf.Max(windowTime, 1e-5f);
            float avgMs = 1000f * windowTime / Mathf.Max(windowFrames, 1);
            string scene = SceneManagerSceneName();
            if (scene != lastSceneName) lastSceneName = scene;   // marker rows come from LevelBus.Started

            Transform player = PlayerBus.Player != null ? PlayerBus.Player.transform : null;
            Vector2 pos = player != null ? (Vector2)player.position : Vector2.zero;

            // Managed heap size plus this window's allocation delta and Gen0 collection count. The
            // delta reads negative across a collection — the collections column explains why.
            long gcBytes = GC.GetTotalMemory(false);
            long gcAllocKb = (gcBytes - lastGcBytes) / 1024;
            long collects = GC.CollectionCount(0);
            int gcCollects = (int)(collects - lastGcCollects);
            lastGcBytes = gcBytes;
            lastGcCollects = collects;

            RopeGun rope = Rope;
            LevelMemento memento = Memento;
            CamHandler cam = Cam;

            totalSeconds = now;
            totalFrames += windowFrames;
            totalFrameTimeSum += windowTime;
            if (windowMinFps < sessionMinFps) sessionMinFps = windowMinFps;

            // Invariant culture: a locale that renders floats as "3,5" would corrupt every column past it.
            // string.Format with a provider, not string.Create(IFormatProvider, ...): that overload is
            // .NET 6+ and Unity's profile only offers the SpanAction form.
            writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "{0:F1},{1},{2:F0},{3},{4},{5:F2},{6:F1},{7:F2},{8:F2},{9},{10},"
                + "{11},{12},{13:F2},{14},{15},{16:F1},{17},{18},{19},{20},{21},{22},{23},{24}",
                now, scene, avgFps, windowMinFps, windowMaxFps, avgMs,
                gcBytes / (1024f * 1024f), pos.x, pos.y, PlayerBus.State, PlayerBus.Face,
                GameStateStore.Current, windowHitchCount, windowMaxFrameMs, gcAllocKb, gcCollects,
                SceneDirector.LastLoadMs,
                AudioManager.Instance != null ? AudioManager.Instance.ActiveVoiceCount : 0,
                LifeBus.DeathCount,
                rope != null ? rope.Phase.ToString() : "",
                memento != null ? memento.RestorableCount : 0,
                cam != null ? cam.Mode.ToString() : "",
                DrawCalls, SetPassCalls, Triangles));
            writer.Flush();
        }

        // ---- context lookups ----

        // The rope lives on the player's GameObject, which changes every scene — the fake-null check
        // re-resolves on the first sample after a switch
        private RopeGun ropeCache;
        private RopeGun Rope
        {
            get
            {
                if (ropeCache == null && PlayerBus.Player != null)
                    ropeCache = PlayerBus.Player.GetComponent<RopeGun>();
                return ropeCache;
            }
        }

        private LevelMemento Memento =>
            mementoCache != null ? mementoCache : mementoCache = FindAnyObjectByType<LevelMemento>();

        private CamHandler Cam =>
            camCache != null ? camCache : camCache = FindAnyObjectByType<CamHandler>();

        // Render counters come from the profiler; release builds don't have them and log -1
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private int DrawCalls => drawCallsRecorder.Valid ? (int)drawCallsRecorder.LastValue : -1;
        private int SetPassCalls => setPassRecorder.Valid ? (int)setPassRecorder.LastValue : -1;
        private int Triangles => trianglesRecorder.Valid ? (int)trianglesRecorder.LastValue : -1;
#else
        private int DrawCalls => -1;
        private int SetPassCalls => -1;
        private int Triangles => -1;
#endif

        // Scene changes are written as comment lines so a CSV reader treats them as noise but a human
        // can see exactly where the hard part of the run started.
        private void OnSceneStarted(string sceneName)
        {
            if (writer == null || sceneName == null || sceneName == lastSceneName) return;
            lastSceneName = sceneName;

            double now = Time.realtimeSinceStartup - startRealtime;
            writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "# scene={0} t={1:F1}s", sceneName, now));
            writer.Flush();
        }

        private void WriteSummary()
        {
            if (writer == null) return;

            float avgGlobal = totalFrames > 0 ? (float)(totalFrames / Math.Max(totalFrameTimeSum, 1e-6)) : 0f;
            writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "# summary duration_s={0:F1} frames={1} fps_avg_global={2:F0} fps_min_session={3} hitch_total={4}",
                totalSeconds, totalFrames, avgGlobal,
                sessionMinFps == float.MaxValue ? 0f : sessionMinFps,
                hitchTotal));
            writer.Flush();
        }

        private void CloseStream()
        {
            if (streamFailed) return;
            try { writer?.Dispose(); }
            catch (Exception e) { Debug.LogWarning($"PerformanceRecorder: could not close log ({e.Message})"); }
            writer = null;
        }

        private static string SceneManagerSceneName() => UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

        // One-shot shell-out per session. Working directory sits inside the repo (the project folder),
        // so git resolves upward to the repository root without us carrying its absolute path around.
        // Any failure — no git installed, detached tooling, timeout — degrades to "unknown": the log
        // loses attribution, not the play session.
        private static string RunGit(string arguments)
        {
            try
            {
                var psi = new ProcessStartInfo("git", arguments)
                {
                    WorkingDirectory = Path.GetDirectoryName(Application.dataPath),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };

                using Process process = Process.Start(psi);
                if (process == null) return "unknown";

                string output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit(1500);
                return process.HasExited && output.Length > 0 ? output : "unknown";
            }
            catch (Exception)
            {
                return "unknown";
            }
        }
    }
}
