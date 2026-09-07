using Inkform.Settings;
using UnityEngine;
using UnityEngine.UI;

namespace Inkform.UI
{
    /// <summary>
    /// Frame rate counter overlay: a small top-right label on its own Canvas (sorting order 50,
    /// below the menu canvas at 100, so menus cover it). Purely presentational — reads SettingsStore
    /// directly and subscribes to Changed for the on/off toggle, exactly like RopeGun's sensitivity.
    ///
    /// Attach to GameManager (the DontDestroyOnLoad host, wired by UIBuilder): the counter then
    /// survives scene switches and keeps running in every scene. Hidden by default (ShowFps = false);
    /// the Graphics tab flips it live.
    ///
    /// The font is a serialized reference on this component (UIBuilder hands over the project font);
    /// being a runtime-created Text it cannot read the font from a built prefab label. Unwired, it
    /// falls back to the built-in font.
    ///
    /// This canvas carries a CanvasScaler matching UIManager's, and it needs one despite the label
    /// being a single corner-anchored string. The anchor keeps it in the corner at any resolution, but
    /// the font size is in pixels: unscaled, 24px is a comfortable readout at 1080p and an unreadable
    /// speck at 4K, while the menus beside it scale correctly. The earlier "a counter needs no
    /// scaling" reasoning only held while the counter was the same size as the UI it sat next to.
    /// </summary>
    public class FpsDisplay : MonoBehaviour
    {
        private const float UpdateInterval = 0.5f;      // refresh the label text at most this often
        private const float Smoothing = 0.1f;           // exponential smoothing on the instant fps

        [SerializeField] private Font font;

        private GameObject root;
        private Text label;
        private float smoothedFps;
        private float elapsed;

        void Awake()
        {
            root = new GameObject("Fps Counter");
            root.transform.SetParent(transform);

            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;

            // Same four values as UIManager.CreateCanvas. Kept in step by hand rather than shared:
            // this canvas is deliberately separate (it must draw under the menu layer and outlive any
            // panel), and one counter is not worth a shared factory.
            CanvasScaler scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            GameObject textGo = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            textGo.transform.SetParent(root.transform, false);

            RectTransform rt = textGo.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-16f, -16f);
            rt.sizeDelta = new Vector2(160f, 40f);

            label = textGo.GetComponent<Text>();
            label.font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (label.font == null) label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            label.fontSize = 24;
            label.alignment = TextAnchor.MiddleRight;
            label.color = new Color(1f, 1f, 1f, 0.8f);

            root.SetActive(SettingsStore.ShowFps);
        }

        void OnEnable() => SettingsStore.Changed += OnSettingsChanged;
        void OnDisable() => SettingsStore.Changed -= OnSettingsChanged;

        private void OnSettingsChanged() => root.SetActive(SettingsStore.ShowFps);

        void Update()
        {
            if (!root.activeSelf) return;

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
