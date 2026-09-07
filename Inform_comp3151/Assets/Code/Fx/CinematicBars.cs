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
    /// Self-installed by RoomIntro; any future cutscene can drive the same component.
    /// </summary>
    public sealed class CinematicBars : MonoBehaviour
    {
        [SerializeField] private float barHeightFraction = 0.1f;   // each bar, as a fraction of screen height

        private RectTransform top, bottom;

        private void Awake() => Build();

        /// <summary>Slides both bars in from the screen edges; completes when fully in.</summary>
        public IEnumerator Show(float seconds) => Animate(barHeightFraction, seconds);

        /// <summary>Slides both bars back out; completes when gone.</summary>
        public IEnumerator Hide(float seconds) => Animate(0f, seconds);

        /// <summary>Jumps both bars to a fraction of screen height instantly, no animation — the
        /// intro uses it to stand fully letterboxed before the transition's fade-in reveals it.</summary>
        public void Set(float fraction)
        {
            if (top == null) Build();
            float height = Screen.height * Mathf.Clamp01(fraction);
            top.sizeDelta = new Vector2(0f, height);
            bottom.sizeDelta = new Vector2(0f, height);
        }

        private IEnumerator Animate(float targetFraction, float seconds)
        {
            if (top == null) Build();

            seconds = Mathf.Max(0.01f, seconds);
            float start = top.sizeDelta.y;
            float target = Screen.height * targetFraction;
            if (Mathf.Approximately(start, target)) yield break;

            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / seconds)
            {
                float height = Mathf.Lerp(start, target, t);
                top.sizeDelta = new Vector2(0f, height);
                bottom.sizeDelta = new Vector2(0f, height);
                yield return null;
            }

            top.sizeDelta = new Vector2(0f, target);
            bottom.sizeDelta = new Vector2(0f, target);
        }

        private void Build()
        {
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
    }
}
