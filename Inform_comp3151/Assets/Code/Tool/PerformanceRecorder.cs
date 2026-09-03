using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Inkform.Bus;
using Inkform.Player;
using UnityEngine;

// Process (System.Diagnostics) drags its own Debug in; without this alias every bare Debug. call
// is ambiguous between it and UnityEngine.Debug.
using Debug = UnityEngine.Debug;

namespace Inkform.Tool
{
    /// <summary>
    /// Session performance recorder: starts sampling the moment the game boots and writes one CSV row
    /// per second until shutdown, so every play session leaves a comparable trace on disk — frame
    /// pacing plus where the player was and what state they were in when it got slow.
    ///
    /// Output location follows one rule: an existing project-root Log/ folder wins; otherwise a
    /// PerfLogs folder is created under Docs/ and used. The session header stamps the git branch and
    /// commit (resolved by shelling out once at startup; a build machine without git records
    /// "unknown" rather than failing), because a perf file nobody can pin to a code revision is only
    /// half evidence.
    ///
    /// Attach to GameManager: the host survives scene switches via DontDestroyOnLoad, duplicate copies
    /// arriving with each new scene are destroyed together with their host (PersistentGameRoot's dedup)
    /// — the active-instance guard below is belt-and-braces for Awake ordering edge cases.
    ///
    /// All timing is unscaled so hitstop (timeScale = 0) never freezes or divides by zero; per-frame
    /// costs are aggregated over the sample window before being written, which is what makes rows of
    /// different sessions comparable.
    /// </summary>
    public sealed class PerformanceRecorder : MonoBehaviour
    {
        private const float SampleInterval = 1f;
        private const string HeadlessFallbackFolder = "PerfLogs";

        private static PerformanceRecorder active;

        private StreamWriter writer;
        private bool streamFailed;

        // Window aggregation: unscaled frame time summed over the interval, plus fps extremes within it
        private float windowTime;
        private int windowFrames;
        private float windowMinFps = float.MaxValue;
        private float windowMaxFps;

        // Whole-session totals for the closing summary line
        private double totalSeconds;
        private long totalFrames;
        private float sessionMinFps = float.MaxValue;
        private double totalFrameTimeSum;
        private float startRealtime;
        private string lastSceneName = "";

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

            OpenSession();
        }

        void OnEnable()
        {
            LevelBus.Started += OnSceneStarted;
        }

        void OnDisable()
        {
            LevelBus.Started -= OnSceneStarted;
        }

        void OnDestroy()
        {
            if (active != this) return;
            active = null;

            WriteSummary();
            CloseStream();
        }

        void Update()
        {
            if (writer == null) return;

            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f) return;

            int fps = Mathf.RoundToInt(1f / dt);
            if (fps < windowMinFps) windowMinFps = fps;
            if (fps > windowMaxFps) windowMaxFps = fps;

            windowTime += dt;
            windowFrames++;
            if (windowTime < SampleInterval) return;

            WriteSample();
            windowTime = 0f;
            windowFrames = 0;
            windowMinFps = float.MaxValue;
            windowMaxFps = 0f;
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

                WriteHeader();

                startRealtime = Time.realtimeSinceStartup;
                lastSceneName = SceneManagerSceneName();
                Debug.Log($"PerformanceRecorder: logging to {path}", this);
            }
            catch (Exception e)
            {
                streamFailed = true;
                writer = null;
                enabled = false;   // recording must never cost the game anything
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
            writer.WriteLine($"# screen={Screen.currentResolution.width}x{Screen.currentResolution.height}"
                           + $" targetFps={Application.targetFrameRate} vSync={QualitySettings.vSyncCount}");
            writer.WriteLine("time_s,scene,fps_avg,fps_min,fps_max,frame_ms,gc_mb,x,y,state,face");
            writer.Flush();
        }

        private void WriteSample()
        {
            if (writer == null) return;

            double now = Time.realtimeSinceStartup - startRealtime;
            float avgFps = windowFrames / Mathf.Max(windowTime, 1e-5f);
            float avgMs = 1000f * windowTime / Mathf.Max(windowFrames, 1);
            string scene = SceneManagerSceneName();
            if (scene != lastSceneName) lastSceneName = scene;   // marker rows come from LevelBus.Started

            Transform player = PlayerBus.Player != null ? PlayerBus.Player.transform : null;
            Vector2 pos = player != null ? (Vector2)player.position : Vector2.zero;

            totalSeconds = now;
            totalFrames += windowFrames;
            totalFrameTimeSum += windowTime;
            if (windowMinFps < sessionMinFps) sessionMinFps = windowMinFps;

            // Invariant culture: a locale that renders floats as "3,5" would corrupt every column past it.
            // string.Format with a provider, not string.Create(IFormatProvider, ...): that overload is
            // .NET 6+ and Unity's profile only offers the SpanAction form.
            writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "{0:F1},{1},{2:F0},{3},{4},{5:F2},{6:F1},{7:F2},{8:F2},{9},{10}",
                now, scene, avgFps, windowMinFps, windowMaxFps, avgMs,
                GC.GetTotalMemory(false) / (1024f * 1024f), pos.x, pos.y,
                PlayerBus.State, PlayerBus.Face));
            writer.Flush();
        }

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
                "# summary duration_s={0:F1} frames={1} fps_avg_global={2:F0} fps_min_session={3}",
                totalSeconds, totalFrames, avgGlobal,
                sessionMinFps == float.MaxValue ? 0f : sessionMinFps));
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
