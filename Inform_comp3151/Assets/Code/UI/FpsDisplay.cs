using Inkform.Settings;
using UnityEngine;
using UnityEngine.UI;

namespace Inkform.UI
{
    /// <summary>
    /// Frame rate counter overlay inside HudRoot. Purely presentational — reads SettingsStore directly
    /// and subscribes to Changed for the on/off toggle, exactly like RopeGun's sensitivity.
    ///
    /// The persistent HUD survives scene switches. Hidden by default (ShowFps = false); the Graphics
    /// tab flips its serialized prefab label live.
    /// </summary>
    public class FpsDisplay : MonoBehaviour
    {
        private const float UpdateInterval = 0.5f;      // refresh the label text at most this often
        private const float Smoothing = 0.1f;           // exponential smoothing on the instant fps

        [SerializeField] private GameObject root;
        [SerializeField] private Text label;
        private float smoothedFps;
        private float elapsed;

        void Awake()
        {
            if (root != null) root.SetActive(SettingsStore.ShowFps);
        }

        void OnEnable() => SettingsStore.Changed += OnSettingsChanged;
        void OnDisable() => SettingsStore.Changed -= OnSettingsChanged;

        private void OnSettingsChanged()
        {
            if (root != null) root.SetActive(SettingsStore.ShowFps);
        }

        void Update()
        {
            if (root == null || label == null || !root.activeSelf) return;

            // Unscaled time: hitstop (timeScale = 0) must not zero the reading or freeze the label
            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f) return;

            // Instant fps is too jittery to read; smooth it and print at most twice a second
            smoothedFps += (1f / dt - smoothedFps) * Smoothing;

            elapsed += dt;
            if (elapsed >= UpdateInterval)
            {
                elapsed = 0f;
                label.text = $"{Mathf.RoundToInt(smoothedFps)} FPS";
            }
        }
    }
}
