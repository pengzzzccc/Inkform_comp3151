using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Inkform.Level
{
    /// <summary>
    /// Full-screen fade SceneDirector hides scene switches behind: black up before the load, black
    /// down after the new scene has settled (which also covers RespawnDirector's one-frame-later
    /// player placement). Code-built overlay canvas like SaveIndicator — no prefab to regenerate —
    /// and driven entirely with unscaled time, because Save &amp; Quit fades while the pause menu
    /// still has timeScale at 0.
    ///
    /// Self-installed by SceneDirector on the persistent GameManager host.
    /// </summary>
    public sealed class SceneFader : MonoBehaviour
    {
        private Canvas canvas;
        private CanvasGroup group;

        private void Awake() => BuildUi();

        /// <summary>Fades to black over the given seconds; completes when fully black.</summary>
        public IEnumerator FadeOut(float seconds) => Fade(1f, seconds);

        /// <summary>Fades back to clear over the given seconds; completes when fully transparent.</summary>
        public IEnumerator FadeIn(float seconds) => Fade(0f, seconds);

        private IEnumerator Fade(float targetAlpha, float seconds)
        {
            if (group == null) yield break;

            seconds = Mathf.Max(0.01f, seconds);
            float start = group.alpha;
            if (Mathf.Approximately(start, targetAlpha)) yield break;   // e.g. a failed load right after a fade-out

            canvas.enabled = true;

            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / seconds)
            {
                group.alpha = Mathf.Lerp(start, targetAlpha, t);
                yield return null;
            }

            group.alpha = targetAlpha;
            if (targetAlpha <= 0f) canvas.enabled = false;   // invisible must also mean "cannot eat raycasts"
        }

        private void BuildUi()
        {
            GameObject canvasObject = new GameObject("Scene Fader", typeof(RectTransform));
            canvasObject.transform.SetParent(transform, false);

            canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;   // above the UI sheets (100) — a transition covers everything

            GameObject blocker = new GameObject("Blocker", typeof(RectTransform));
            blocker.transform.SetParent(canvasObject.transform, false);
            RectTransform rect = (RectTransform)blocker.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            Image image = blocker.AddComponent<Image>();
            image.color = Color.black;
            image.raycastTarget = true;      // a visible fade also blocks clicks on the world behind it

            group = canvasObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = true;
            canvas.enabled = false;
        }
    }
}
