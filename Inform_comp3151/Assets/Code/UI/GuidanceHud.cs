using Inkform.Bus;
using UnityEngine;
using UnityEngine.UI;

namespace Inkform.UI
{
    /// <summary>Non-modal tutorial/control-room guidance; latest source on GuidanceBus wins.</summary>
    public sealed class GuidanceHud : MonoBehaviour
    {
        private GameObject root;
        private Text label;

        private void Awake()
        {
            root = GameplayHudFactory.CreatePanel(transform, "Guidance HUD", 91,
                new Vector2(780f, 82f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -38f));
            label = GameplayHudFactory.CreateText(root.transform, 26, TextAnchor.MiddleCenter);
        }

        private void OnEnable()
        {
            GuidanceBus.Changed += Refresh;
            Refresh(GuidanceBus.Current);
        }

        private void OnDisable() => GuidanceBus.Changed -= Refresh;
        public void RefreshVisibility() => Refresh(GuidanceBus.Current);

        private void Refresh(string message)
        {
            if (root == null) return;
            bool gameplay = UIManager.Instance != null && !UIManager.Instance.IsInMainMenu;
            root.SetActive(gameplay && !string.IsNullOrWhiteSpace(message));
            label.text = message ?? string.Empty;
        }
    }

    internal static class GameplayHudFactory
    {
        public static GameObject CreatePanel(Transform owner, string name, int sortingOrder,
            Vector2 size, Vector2 anchor, Vector2 pivot, Vector2 anchoredPosition)
        {
            GameObject canvasObject = new GameObject(name + " Canvas", typeof(RectTransform));
            canvasObject.transform.SetParent(owner, false);
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            GameObject panel = new GameObject(name, typeof(RectTransform));
            panel.transform.SetParent(canvasObject.transform, false);
            RectTransform rect = (RectTransform)panel.transform;
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            Image background = panel.AddComponent<Image>();
            background.color = new Color(0.025f, 0.035f, 0.055f, 0.82f);
            background.raycastTarget = false;
            return panel;
        }

        public static Text CreateText(Transform panel, int fontSize, TextAnchor alignment)
        {
            GameObject textObject = new GameObject("Label", typeof(RectTransform));
            textObject.transform.SetParent(panel, false);
            RectTransform rect = (RectTransform)textObject.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(16f, 8f);
            rect.offsetMax = new Vector2(-16f, -8f);
            Text text = textObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = Color.white;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }
    }
}
