using Inkform.Bus;
using Inkform.Tool;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Inkform.Fx
{
    /// <summary>
    /// Screen-level FX: hitstop and the post vignette punch.
    /// Both only listen to FxBus commands, never caring which game event triggered them (only death
    /// actually requests the vignette, but this class does not need to know).
    /// Timing always uses UnscaledTimer — Time.time is frozen during hitstop; a plain Timer would never expire.
    /// </summary>
    public class ScreenFx : MonoBehaviour
    {
        [Header("Hit stop")]
        [SerializeField] private float maxHitStop = 0.25f;      // safety cap, prevents freezing the game too long

        [Header("Post punch")]
        [SerializeField] private Volume volume;
        [SerializeField] private float vignettePunch = 0.35f;   // vignette increment at amount = 1

        private UnscaledTimer stopTimer;

        private Vignette vignette;
        private float baseVignette;
        private float punchAmount, punchDuration;
        private UnscaledTimer punchTimer;

        void Awake()
        {
            if (volume != null)
            {
                // Use profile rather than sharedProfile: the profile getter automatically copies a
                // private instance, so runtime changes never pollute the project's Volume Profile asset;
                // sharedProfile is the shared asset itself — writing it would persist to the .asset and
                // affect every Volume using it
                if (volume.profile.TryGet(out vignette)) baseVignette = vignette.intensity.value;
            }
        }

        void OnEnable()
        {
            FxBus.HitStopRequested += OnHitStop;
            FxBus.PunchRequested += OnPunch;
        }

        void OnDisable()
        {
            FxBus.HitStopRequested -= OnHitStop;
            FxBus.PunchRequested -= OnPunch;

            // Safety: if this component is disabled/destroyed while hitstopped, time must be released,
            // or the entire game freezes permanently
            if (Time.timeScale == 0f) Time.timeScale = 1f;

            // Restore post values, so it does not sit at the punch peak
            if (vignette != null) vignette.intensity.value = baseVignette;
        }

        void Update()
        {
            // restore when hitstop expires
            if (Time.timeScale == 0f && !stopTimer.IsRunning) Time.timeScale = 1f;

            PunchStep();
        }

        private void PunchStep()
        {
            float k = (punchDuration > 0f && punchTimer.IsRunning) ? punchTimer.Remaining / punchDuration : 0f;
            float a = punchAmount * k;

            if (vignette != null) vignette.intensity.value = Mathf.Clamp01(baseVignette + vignettePunch * a);
        }

        private void OnHitStop(float duration)
        {
            if (duration <= 0f) return;
            duration = Mathf.Min(duration, maxHitStop);

            // Overlapping requests take the longer one; do not interrupt each other
            if (duration <= stopTimer.Remaining) return;

            stopTimer.Set(duration);
            Time.timeScale = 0f;
        }

        private void OnPunch(float amount, float duration)
        {
            if (duration <= 0f) return;

            punchAmount = Mathf.Clamp01(amount);
            punchDuration = duration;
            punchTimer.Set(duration);
        }
    }
}
