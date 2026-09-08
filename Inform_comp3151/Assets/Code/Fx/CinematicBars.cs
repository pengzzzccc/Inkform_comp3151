using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Inkform.Fx
{
    /// <summary>
    /// Letterbox: two black bars sliding in from the top and bottom screen edges — the industry's
    /// universal "this is a cinematic, not gameplay" signal (convention tracing back to Ninja
    /// Gaiden). Code-built overlay canvas like SceneFader — no prefab to regenerate.
    ///
    /// The bars never raycast: they only ever cover screen edges, and whatever sits behind them
    /// stays as interactive as it was. Driven by unscaled time so an already-controllable player
    /// can pause while the exit animation is finishing without leaving the camera held forever.
    ///
    /// Owned by RoomIntro; any future cutscene can drive the same component.
    /// </summary>
    public sealed class CinematicBars : MonoBehaviour
    {
        [Range(0f, 0.5f)]
        [SerializeField] private float barHeightFraction = 0.1f;   // each bar, as a fraction of screen height

        private RectTransform top, bottom;

        /// <summary>Slides both bars in from the screen edges; completes when fully in.</summary>
        public IEnumerator Show(float seconds) => Animate(barHeightFraction, seconds);

        /// <summary>Slides both bars back out; completes when gone.</summary>
        public IEnumerator Hide(float seconds) => Animate(0f, seconds);

        /// <summary>Jumps to the configured letterbox height instantly. This deliberately has no
        /// numeric argument: passing 1 as a visibility value used to make each bar one full screen
        /// tall instead of showing a movie-style letterbox.</summary>
        public void ShowInstant()
        {
            if (top == null) Build();
            SetHeight(Screen.height * Mathf.Clamp(barHeightFraction, 0f, 0.5f));
        }

        private IEnumerator Animate(float targetFraction, float seconds)
        {
            if (top == null) Build();

            float start = top.sizeDelta.y;
            float target = Screen.height * Mathf.Clamp(targetFraction, 0f, 0.5f);
            if (Mathf.Approximately(start, target)) yield break;
            if (seconds <= 0f)
            {
                SetHeight(target);
                yield break;
            }

            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / seconds)
            {
                float height = Mathf.Lerp(start, target, t);
                SetHeight(height);
                yield return null;
            }

            SetHeight(target);
        }

        private void Build()
        {
            if (top != null && bottom != null) return;

            GameObject canvasObject = new GameObject("Cinematic Bars", typeof(RectTransform));
            canvasObject.transform.SetParent(transform, false);

            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 150;   // above the UI sheets (100), below SceneFader (200)

            top = BuildBar(canvasObject.transform, "Top");
            bottom = BuildBar(canvasObject.transform, "Bottom");
        }

        // Each bar hugs one screen edge: top stretches across and grows downward, bottom across and
        // grows upward — animating sizeDelta.y alone slides the pair in and out
        private static RectTransform BuildBar(Transform parent, string name)
        {
            GameObject bar = new GameObject(name, typeof(RectTransform));
            bar.transform.SetParent(parent, false);

            RectTransform rect = (RectTransform)bar.transform;
            bool isTop = name == "Top";
            rect.anchorMin = new Vector2(0f, isTop ? 1f : 0f);
            rect.anchorMax = new Vector2(1f, isTop ? 1f : 0f);
            rect.pivot = new Vector2(0.5f, isTop ? 1f : 0f);
            rect.sizeDelta = Vector2.zero;

            Image image = bar.AddComponent<Image>();
            image.color = Color.black;
            image.raycastTarget = false;
            return rect;
        }

        private void SetHeight(float height)
        {
            top.sizeDelta = new Vector2(0f, height);
            bottom.sizeDelta = new Vector2(0f, height);
        }
    }
}
