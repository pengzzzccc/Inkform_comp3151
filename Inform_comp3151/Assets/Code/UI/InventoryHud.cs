using Inkform.Item;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Inkform.UI
{
    /// <summary>Persistent compact readout for the next FIFO item and current count/capacity.</summary>
    public sealed class InventoryHud : MonoBehaviour
    {
        public static InventoryHud Instance { get; private set; }

        private GameObject hudRoot;
        private Image currentIcon;
        private Text countText;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
            BuildUi();
        }

        private void OnEnable()
        {
            InventoryStore.Changed += Refresh;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Refresh();
        }

        private void OnDisable()
        {
            InventoryStore.Changed -= Refresh;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Refresh();

        public void RefreshVisibility() => Refresh();

        private void BuildUi()
        {
            GameObject canvasObject = new GameObject("Inventory HUD", typeof(RectTransform));
            canvasObject.transform.SetParent(transform, false);

            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 90;

            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            hudRoot = CreateRect("Current Item", canvasObject.transform, new Vector2(132f, 86f));
            RectTransform hudRect = (RectTransform)hudRoot.transform;
            hudRect.anchorMin = hudRect.anchorMax = new Vector2(1f, 0f);
            hudRect.pivot = new Vector2(1f, 0f);
            hudRect.anchoredPosition = new Vector2(-36f, 36f);
            Image hudBackground = hudRoot.AddComponent<Image>();
            hudBackground.color = new Color(0.03f, 0.04f, 0.06f, 0.8f);
            hudBackground.raycastTarget = false;

            GameObject iconObject = CreateRect("Icon", hudRoot.transform, new Vector2(54f, 54f));
            RectTransform iconRect = (RectTransform)iconObject.transform;
            iconRect.anchorMin = iconRect.anchorMax = new Vector2(0f, 0.5f);
            iconRect.pivot = new Vector2(0f, 0.5f);
            iconRect.anchoredPosition = new Vector2(14f, 0f);
            currentIcon = iconObject.AddComponent<Image>();
            currentIcon.preserveAspect = true;
            currentIcon.raycastTarget = false;

            GameObject textObject = CreateRect("Count", hudRoot.transform, new Vector2(52f, 42f));
            RectTransform textRect = (RectTransform)textObject.transform;
            textRect.anchorMin = textRect.anchorMax = new Vector2(1f, 0.5f);
            textRect.pivot = new Vector2(1f, 0.5f);
            textRect.anchoredPosition = new Vector2(-12f, 0f);
            countText = textObject.AddComponent<Text>();
            countText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            countText.fontSize = 24;
            countText.alignment = TextAnchor.MiddleCenter;
            countText.color = Color.white;
            countText.raycastTarget = false;
        }

        private static GameObject CreateRect(string name, Transform parent, Vector2 size)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            return go;
        }

        private void Refresh()
        {
            if (hudRoot == null) return;

            bool gameplay = UIManager.Instance != null && !UIManager.Instance.IsInMainMenu;
            hudRoot.SetActive(gameplay);

            bool hasItem = InventoryStore.TryPeekFirst(out InventoryItemDefinition first);
            currentIcon.sprite = hasItem ? first.Icon : null;
            currentIcon.enabled = hasItem && first.Icon != null;
            countText.text = $"{InventoryStore.Count}/{InventoryStore.Capacity}";
        }
    }
}
