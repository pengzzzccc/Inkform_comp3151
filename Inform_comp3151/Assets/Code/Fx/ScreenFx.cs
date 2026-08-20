using Inkform.Bus;
using Inkform.Settings;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Inkform.Fx
{
    /// <summary>
    /// Screen-level FX: post vignette punch. Hitstop is owned by GameTimeController.
    /// Both only listen to FxBus commands, never caring which game event triggered them (only death
    /// actually requests the vignette, but this class does not need to know).
    /// Presentation time advances through hitstop but freezes for a user pause.
    /// </summary>
    public class ScreenFx : MonoBehaviour
    {
        [Header("Post punch")]
        [SerializeField] private Volume volume;
        [SerializeField] private float vignettePunch = 0.35f;   // vignette increment at amount = 1

        private Vignette vignette;
        private float baseVignette;
        private float punchAmount, punchDuration, punchRemaining;

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
            FxBus.PunchRequested += OnPunch;
        }

        void OnDisable()
        {
            FxBus.PunchRequested -= OnPunch;

            // Restore post values, so it does not sit at the punch peak
            if (vignette != null) vignette.intensity.value = baseVignette;
        }

        void Update()
        {
            punchRemaining = Mathf.Max(0f, punchRemaining - GameTimeController.PresentationDeltaTime);
            PunchStep();
        }

        private void PunchStep()
        {
            float k = punchDuration > 0f ? punchRemaining / punchDuration : 0f;
            float a = punchAmount * k;

            if (vignette != null) vignette.intensity.value = Mathf.Clamp01(baseVignette + vignettePunch * a);
        }

        private void OnPunch(float amount, float duration)
        {
            if (duration <= 0f) return;

            // FX intensity scales the visual punch; hitstop (a duration) stays untouched
            punchAmount = Mathf.Clamp01(amount * SettingsStore.FxIntensity);
            punchDuration = duration;
            punchRemaining = duration;
        }
    }
}
