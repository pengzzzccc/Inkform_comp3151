using System;
using Inkform.Settings;
using UnityEngine;
using UnityEngine.UIElements;

namespace Inkform.UI
{
    /// <summary>
    /// The VIDEO settings page: resolution cycling, fullscreen, vsync, fps cap, FX intensity,
    /// the perf recorder tools. Cycling rows match their stored value by value (not index) —
    /// the machine's list differs per player.
    /// </summary>
    public class SettingsVideoPage : SettingsSubPage
    {
        public override string Title => "VIDEO";

        private OptionRow resRow, fullRow, vsyncRow, fpsRow, showFpsRow, perfRow, perfRateRow;
        private Slider fxSlider;

        public SettingsVideoPage(SettingsPanel owner) : base(owner, "UI/SettingsVideo")
        {
            resRow = AddOptionRow("Resolution", FormatRes(SettingsStore.ResolutionWidth, SettingsStore.ResolutionHeight));
            resRow.Stepped += StepResolution;

            fullRow = AddOnOffRow("Fullscreen", () => SettingsStore.Fullscreen, SettingsStore.SetFullscreen);

            vsyncRow = AddOnOffRow("VSync", () => SettingsStore.VSync, SettingsStore.SetVSync);

            fpsRow = AddOptionRow("FPS Cap", FpsText(SettingsStore.FpsCap));
            fpsRow.Stepped += StepFps;

            AddSliderRow("FX Intensity", 0f, 1f, out fxSlider);
            fxSlider.RegisterValueChangedCallback(e => SettingsStore.SetFxIntensity(e.newValue));

            showFpsRow = AddOnOffRow("Show FPS", () => SettingsStore.ShowFps, SettingsStore.SetShowFps);

            perfRow = AddOnOffRow("Perf Recording", () => SettingsStore.PerfRecording, SettingsStore.SetPerfRecording);

            perfRateRow = AddOptionRow("Perf Rate", RateText(SettingsStore.PerfInterval));
            perfRateRow.Stepped += StepPerfRate;
        }

        public override void Refresh()
        {
            resRow.Value = FormatRes(SettingsStore.ResolutionWidth, SettingsStore.ResolutionHeight);
            fullRow.Value = OnOffText(SettingsStore.Fullscreen);
            vsyncRow.Value = OnOffText(SettingsStore.VSync);
            fpsRow.Value = FpsText(SettingsStore.FpsCap);
            SetSlider(fxSlider, SettingsStore.FxIntensity, null, null);
            showFpsRow.Value = OnOffText(SettingsStore.ShowFps);
            perfRow.Value = OnOffText(SettingsStore.PerfRecording);
            perfRateRow.Value = RateText(SettingsStore.PerfInterval);
        }

        /// <summary>Cycles the machine's supported resolutions; the stored size is matched by
        /// value (not index — the list differs between machines, an index would be meaningless).</summary>
        private void StepResolution(int dir)
        {
            var list = SettingsStore.AvailableResolutions;
            int i = IndexOfResolution(list, SettingsStore.ResolutionWidth, SettingsStore.ResolutionHeight);
            i = (i + dir + list.Count) % list.Count;

            var r = list[i];
            SettingsStore.SetResolution(r.width, r.height);
            resRow.Value = FormatRes(r.width, r.height);
        }

        /// <summary>Cycles the FpsOptions list; the stored cap is matched by value for the same reason.</summary>
        private void StepFps(int dir)
        {
            int[] opts = SettingsStore.FpsOptions;
            int i = Array.IndexOf(opts, SettingsStore.FpsCap);
            if (i < 0) i = 1;   // stored value is not one of the options (e.g. hand-edited): step from 60

            i = (i + dir + opts.Length) % opts.Length;
            SettingsStore.SetFpsCap(opts[i]);
            fpsRow.Value = FpsText(opts[i]);
        }

        /// <summary>Cycles the PerfIntervals list (seconds); matched by value like StepFps.</summary>
        private void StepPerfRate(int dir)
        {
            float[] opts = SettingsStore.PerfIntervals;
            int i = Array.IndexOf(opts, SettingsStore.PerfInterval);
            if (i < 0) i = 2;   // stored value is not one of the options: step from 1 s

            i = (i + dir + opts.Length) % opts.Length;
            SettingsStore.SetPerfInterval(opts[i]);
            perfRateRow.Value = RateText(opts[i]);
        }

        private static int IndexOfResolution(System.Collections.Generic.IReadOnlyList<Resolution> list, int width, int height)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].width == width && list[i].height == height) return i;
            }
            return 0;
        }
    }
}
