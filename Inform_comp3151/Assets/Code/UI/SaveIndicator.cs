using Inkform.Save;
using UnityEngine;
using UnityEngine.UI;

namespace Inkform.UI
{
    /// <summary>
    /// "Saved" toast: the corner flash that tells the player an autosave just landed on disk (a room
    /// entered, a checkpoint stamped, an item picked up, Save &amp; Quit). SaveStore writes silently
    /// otherwise, and in a game whose whole loop is checkpoints, "did it save?" is worth one glance.
    ///
    /// Subscribes to SaveStore.Saved — not Changed, which also fires for in-memory updates like
    /// BeginNewRun/Delete that write nothing to confirm. Self-installed by UIManager (same pattern as
    /// GamepadCursor / InventoryHud) and builds its own small overlay canvas, so no UIBuilder prefab
    /// needs regenerating for it to exist.
    ///
    /// Timed with unscaled time: Save &amp; Quit happens while the pause menu has timeScale at 0, and
    /// its final save deserves the same flash as any other.
    /// </summary>
    public sealed class SaveIndicator : MonoBehaviour
    {
        private const float HoldSeconds = 1f;
        private const float FadeSeconds = 0.4f;

        private Text label;
        private float remaining;

        private void Awake() => BuildUi();

        private void OnEnable()
        {
            SaveStore.Saved += Show;
        }

        private void OnDisable()
        {
            SaveStore.Saved -= Show;
        }

        private void Show()
        {
            if (label == null) return;
            remaining = HoldSeconds + FadeSeconds;
            label.enabled = true;
            SetAlpha(1f);
        }

        private void Update()
        {
            if (remaining <= 0f) return;

            remaining = Mathf.Max(0f, remaining - Time.unscaledDeltaTime);
            SetAlpha(Mathf.Clamp01(remaining / FadeSeconds));
            if (remaining <= 0f) label.enabled = false;
        }

        private void SetAlpha(float alpha)
        {
            Color color = label.color;
            color.a = alpha;
            label.color = color;
        }

        private void BuildUi()
        {
            GameObject canvasObject = new GameObject("Save Indicator", typeof(RectTransform));
            canvasObject.transform.SetParent(transform, false);

            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 95;   // above the inventory HUD (90), under the menu sheets (100)

            // Same scaler policy as UIManager's canvas and the inventory HUD, so the offsets below
            // mean the same distance at every resolution
            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            GameObject textObject = new GameObject("Label", typeof(RectTransform));
            textObject.transform.SetParent(canvasObject.transform, false);
            RectTransform rect = (RectTransform)textObject.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-44f, 138f);   // clear above the inventory HUD's box
            rect.sizeDelta = new Vector2(200f, 32f);

            label = textObject.AddComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 24;
            label.fontStyle = FontStyle.Italic;
            label.alignment = TextAnchor.MiddleRight;
            label.color = new Color(1f, 1f, 1f, 0.9f);
            label.raycastTarget = false;
            label.text = "Saved";
            label.enabled = false;
        }
    }
}
