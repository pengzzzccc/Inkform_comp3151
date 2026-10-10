using System.Collections;
using Inkform.Tool;
using UnityEngine;
using UnityEngine.UI;

namespace Inkform.Level
{
    /// <summary>
    /// The curtain SceneDirector hides scene switches behind: the screen is cut into horizontal
    /// bands that sweep across it in a staircase. Covering, each band slides in from the right,
    /// the bottom one first and each band above a beat later; revealing, they carry on out to the
    /// left in the same bottom-first order — one continuous right-to-left wipe across the load.
    /// Covered after FadeOut also covers RespawnDirector's one-frame-later player placement.
    ///
    /// Code-built overlay canvas — no prefab to regenerate — and driven entirely with unscaled
    /// time, because Save &amp; Quit transitions while the pause menu still has timeScale at 0.
    ///
    /// Self-installed by SceneDirector on the persistent GameManager host, so the serialized
    /// defaults below are the live tuning.
    /// </summary>
    public sealed class SceneFader : MonoBehaviour
    {
        [SerializeField, Min(1)] private int bandCount = 6;
        [Tooltip("Share of the transition spent staggering band starts (0 = all bands move together)")]
        [SerializeField, Range(0f, 0.9f)] private float staggerRatio = 0.45f;
        [SerializeField] private Color bandColor = Color.black;

        // Band positions are normalized: +1 = parked off the right edge, 0 = covering, -1 = off the left
        private const float OffRight = 1f;
        private const float Cover = 0f;
        private const float OffLeft = -1f;

        private Canvas canvas;
        private RectTransform canvasRect;
        private RectTransform[] bands;
        private bool covered;

        private void Awake() => BuildUi();

        /// <summary>Sweeps the bands in from the right; completes when the screen is fully covered.</summary>
        public IEnumerator FadeOut(float seconds)
        {
            if (bands == null || covered) yield break;

            SetAll(OffRight);
            canvas.enabled = true;
            yield return Sweep(OffRight, Cover, seconds);
            covered = true;
        }

        /// <summary>Sweeps the bands on out to the left; completes when the screen is fully clear.</summary>
        public IEnumerator FadeIn(float seconds)
        {
            if (bands == null || !covered) yield break;   // e.g. a failed load before anything covered

            yield return Sweep(Cover, OffLeft, seconds);
            covered = false;
            canvas.enabled = false;   // invisible must also mean "cannot eat raycasts"
            SetAll(OffRight);         // parked for the next cover
        }

        // Staircase: band i (0 = bottom) starts i·stagger after the first, every band takes the
        // same slide time, and the whole sweep lasts exactly `seconds`
        private IEnumerator Sweep(float from, float to, float seconds)
        {
            seconds = Mathf.Max(0.01f, seconds);
            float stagger = bandCount > 1 ? seconds * staggerRatio / (bandCount - 1) : 0f;
            float slide = Mathf.Max(0.01f, seconds - stagger * (bandCount - 1));

            for (float elapsed = 0f; elapsed < seconds; elapsed += PresentationTime.UnscaledStep)
            {
                for (int i = 0; i < bands.Length; i++)
                {
                    float t = Mathf.Clamp01((elapsed - i * stagger) / slide);
                    SetBand(i, Mathf.Lerp(from, to, EaseInOutCubic(t)));
                }
                yield return null;
            }

            SetAll(to);
        }

        private static float EaseInOutCubic(float t) =>
            t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) * 0.5f;

        // Width is read live, so a resolution change mid-game still parks bands fully off screen
        private void SetBand(int index, float position) =>
            bands[index].anchoredPosition = new Vector2(position * canvasRect.rect.width, 0f);

        private void SetAll(float position)
        {
            for (int i = 0; i < bands.Length; i++) SetBand(i, position);
        }

        private void BuildUi()
        {
            GameObject canvasObject = new GameObject("Scene Fader", typeof(RectTransform));
            canvasObject.transform.SetParent(transform, false);

            canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;   // above the UI sheets (100) — a transition covers everything
            canvasRect = (RectTransform)canvasObject.transform;

            bands = new RectTransform[bandCount];
            for (int i = 0; i < bandCount; i++)
            {
                GameObject band = new GameObject($"Band {i}", typeof(RectTransform));
                band.transform.SetParent(canvasObject.transform, false);

                RectTransform rect = (RectTransform)band.transform;
                rect.anchorMin = new Vector2(0f, (float)i / bandCount);
                rect.anchorMax = new Vector2(1f, (float)(i + 1) / bandCount);
                // Half a pixel of overlap each side: fractional band edges must never show a seam
                rect.offsetMin = new Vector2(0f, -0.5f);
                rect.offsetMax = new Vector2(0f, 0.5f);

                Image image = band.AddComponent<Image>();
                image.color = bandColor;
                image.raycastTarget = true;   // a visible curtain also blocks clicks on the world behind it

                bands[i] = rect;
            }

            canvas.enabled = false;
            SetAll(OffRight);
        }
    }
}
