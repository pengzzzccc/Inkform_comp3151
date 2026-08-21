using Inkform.Item;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Inkform.UI
{
    /// <summary>Persistent four-slot radial selector and compact gameplay inventory readout.</summary>
    public sealed class InventoryHud : MonoBehaviour
    {
        public static InventoryHud Instance { get; private set; }

        private readonly Image[] slotIcons = new Image[InventoryStore.Capacity];
        private readonly Image[] slotBackgrounds = new Image[InventoryStore.Capacity];

        private GameObject hudRoot;
        private Image currentIcon;
        private Text countText;
        private GameObject wheelRoot;
        private int pendingIndex;

        public bool IsWheelOpen => wheelRoot != null && wheelRoot.activeSelf;

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
            CancelWheel();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Refresh();

        public void RefreshVisibility() => Refresh();

        public bool OpenWheel()
        {
            if (InventoryStore.Count == 0 || wheelRoot == null) return false;
            pendingIndex = Mathf.Max(0, InventoryStore.SelectedIndex);
            wheelRoot.SetActive(true);
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.None;
            RefreshWheel();
            return true;
        }

        public void SetWheelDirection(Vector2 direction)
        {
            if (!IsWheelOpen || direction.sqrMagnitude < 0.04f) return;

            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            if (angle >= 45f && angle < 135f) pendingIndex = 0;       // up
            else if (angle >= -45f && angle < 45f) pendingIndex = 1; // right
            else if (angle >= -135f && angle < -45f) pendingIndex = 2; // down
            else pendingIndex = 3;                                    // left
            RefreshWheel();
        }

        public void CommitWheel()
        {
            if (!IsWheelOpen) return;
            InventoryStore.Select(pendingIndex);
            wheelRoot.SetActive(false);
            RestoreGameplayCursorLock();
            Refresh();
        }

        public void CancelWheel()
        {
            if (wheelRoot != null) wheelRoot.SetActive(false);
            RestoreGameplayCursorLock();
        }

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

            hudRoot = CreateRect("Current Item", canvasObject.transform, new Vector2(0.5f, 0.5f));
            RectTransform hudRect = (RectTransform)hudRoot.transform;
            hudRect.anchorMin = hudRect.anchorMax = new Vector2(1f, 0f);
            hudRect.pivot = new Vector2(1f, 0f);
            hudRect.anchoredPosition = new Vector2(-36f, 36f);
            hudRect.sizeDelta = new Vector2(132f, 86f);
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

            wheelRoot = CreateRect("Inventory Wheel", canvasObject.transform, new Vector2(560f, 560f));
            RectTransform wheelRect = (RectTransform)wheelRoot.transform;
            wheelRect.anchorMin = wheelRect.anchorMax = new Vector2(0.5f, 0.5f);
            wheelRect.anchoredPosition = Vector2.zero;

            Image wheelBackdrop = wheelRoot.AddComponent<Image>();
            wheelBackdrop.color = new Color(0f, 0f, 0f, 0.35f);
            wheelBackdrop.raycastTarget = false;

            Vector2[] positions =
            {
                new Vector2(0f, 180f), new Vector2(180f, 0f),
                new Vector2(0f, -180f), new Vector2(-180f, 0f)
            };
            for (int i = 0; i < InventoryStore.Capacity; i++)
            {
                GameObject slot = CreateRect($"Slot {i + 1}", wheelRoot.transform, new Vector2(116f, 116f));
                ((RectTransform)slot.transform).anchoredPosition = positions[i];
                slotBackgrounds[i] = slot.AddComponent<Image>();
                slotBackgrounds[i].raycastTarget = false;

                GameObject slotIcon = CreateRect("Icon", slot.transform, new Vector2(82f, 82f));
                slotIcons[i] = slotIcon.AddComponent<Image>();
                slotIcons[i].preserveAspect = true;
                slotIcons[i].raycastTarget = false;
            }

            wheelRoot.SetActive(false);
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
            bool hasItem = InventoryStore.TryPeekSelected(out InventoryItemDefinition selected);
            hudRoot.SetActive(gameplay && hasItem);
            if (!gameplay) CancelWheel();

            if (hasItem)
            {
                currentIcon.sprite = selected.Icon;
                currentIcon.enabled = selected.Icon != null;
                countText.text = $"{InventoryStore.Count}/{InventoryStore.Capacity}";
            }

            if (IsWheelOpen) RefreshWheel();
        }

        private void RefreshWheel()
        {
            for (int i = 0; i < InventoryStore.Capacity; i++)
            {
                bool occupied = i < InventoryStore.Items.Count && InventoryStore.Items[i] != null;
                slotIcons[i].sprite = occupied ? InventoryStore.Items[i].Icon : null;
                slotIcons[i].enabled = occupied && slotIcons[i].sprite != null;
                bool selected = i == pendingIndex;
                slotBackgrounds[i].color = selected
                    ? new Color(0.95f, 0.7f, 0.18f, occupied ? 0.95f : 0.45f)
                    : new Color(0.12f, 0.14f, 0.18f, occupied ? 0.9f : 0.45f);
            }
        }

        private static void RestoreGameplayCursorLock()
        {
            Cursor.visible = false;
            UIManager ui = UIManager.Instance;
            if (ui == null || (!ui.AnyPanelOpen && !ui.IsInMainMenu))
                Cursor.lockState = CursorLockMode.Locked;
        }
    }
}
