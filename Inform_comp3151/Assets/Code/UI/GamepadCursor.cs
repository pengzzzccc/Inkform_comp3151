using System.Collections.Generic;
using Inkform.Settings;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Inkform.UI
{
    /// <summary>One rendered menu cursor for mouse and gamepad, sharing one Canvas coordinate space.</summary>
    public sealed class GamepadCursor : MonoBehaviour
    {
        private static readonly Vector2 ReferenceResolution = new Vector2(1920f, 1080f);
        private const float ScrollSpeed = 1.2f;

        [SerializeField] private Sprite cursorSprite;
        [SerializeField] private float cursorSpeed = 1000f;
        [SerializeField] private float cursorSize = 40f;

        public Sprite CursorSprite => cursorSprite;

        private GameObject root;
        private RectTransform rootRect;
        private RectTransform cursorRect;
        private Vector2 localPos;
        private SettingsStore.InputDevice activeDevice;

        private PointerEventData pointerData;
        private readonly List<RaycastResult> raycastResults = new List<RaycastResult>();
        private GameObject hovered;
        private GameObject pressedTarget;
        private bool visible;

        private static Sprite discSprite;

        private void Awake() => CreateCursor();

        private void CreateCursor()
        {
            root = new GameObject("Unified Menu Cursor", typeof(RectTransform));
            root.transform.SetParent(transform, false);
            rootRect = (RectTransform)root.transform;

            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000;

            CanvasScaler scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = ReferenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            GameObject imageObject = new GameObject("Cursor", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            imageObject.transform.SetParent(root.transform, false);
            Image image = imageObject.GetComponent<Image>();
            image.sprite = cursorSprite != null ? cursorSprite : DiscSprite;
            image.raycastTarget = false;

            cursorRect = (RectTransform)imageObject.transform;
            cursorRect.anchorMin = cursorRect.anchorMax = new Vector2(0.5f, 0.5f);
            cursorRect.pivot = new Vector2(0.5f, 0.5f);
            cursorRect.sizeDelta = Vector2.one * cursorSize;
            root.SetActive(false);
        }

        private void Update()
        {
            bool menuOpen = UIManager.Instance != null && UIManager.Instance.AnyPanelOpen;
            SetVisible(menuOpen);
            if (!visible) return;

            DetectActiveDevice();
            if (activeDevice == SettingsStore.InputDevice.Gamepad && Gamepad.current != null)
                UpdateGamepad(Gamepad.current);
            else
                UpdateMouse();
        }

        private void DetectActiveDevice()
        {
            Mouse mouse = Mouse.current;
            Gamepad gamepad = Gamepad.current;
            bool mouseActed = mouse != null
                && (mouse.delta.ReadValue().sqrMagnitude > 0.01f
                    || mouse.leftButton.wasPressedThisFrame
                    || mouse.scroll.ReadValue().sqrMagnitude > 0.01f);
            bool padActed = gamepad != null
                && (gamepad.leftStick.ReadValue().sqrMagnitude > 0.01f
                    || gamepad.buttonSouth.wasPressedThisFrame);

            SettingsStore.InputDevice requested = mouseActed
                ? SettingsStore.InputDevice.KeyboardMouse
                : padActed ? SettingsStore.InputDevice.Gamepad : SettingsStore.Device;

            if (requested == activeDevice) return;
            if (activeDevice == SettingsStore.InputDevice.Gamepad)
            {
                CancelPress();
                ClearHover();
            }
            activeDevice = requested;
            if (SettingsStore.Device != requested) SettingsStore.SetDevice(requested);
        }

        private void UpdateMouse()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null) return;

            Vector2 screen = mouse.position.ReadValue();
            screen.x = Mathf.Clamp(screen.x, 0f, Screen.width);
            screen.y = Mathf.Clamp(screen.y, 0f, Screen.height);
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rootRect, screen, null, out Vector2 local))
            {
                localPos = ClampLocal(local);
                cursorRect.anchoredPosition = localPos;
            }

            // Mouse hover/down/up/click remains exclusively owned by InputSystemUIInputModule.
        }

        private void UpdateGamepad(Gamepad gamepad)
        {
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null)
                EventSystem.current.SetSelectedGameObject(null);

            Vector2 stick = gamepad.leftStick.ReadValue();
            if (stick.sqrMagnitude > 0.0001f)
            {
                localPos += stick * cursorSpeed * SettingsStore.StickSensitivity * Time.unscaledDeltaTime;
                localPos = ClampLocal(localPos);
                cursorRect.anchoredPosition = localPos;
            }

            UpdateHover();

            Vector2 scroll = gamepad.rightStick.ReadValue();
            if (Mathf.Abs(scroll.y) > 0.2f && hovered != null)
            {
                ScrollRect scrollRect = hovered.GetComponentInParent<ScrollRect>();
                if (scrollRect != null && scrollRect.vertical)
                    scrollRect.verticalNormalizedPosition = Mathf.Clamp01(
                        scrollRect.verticalNormalizedPosition
                        + scroll.y * ScrollSpeed * SettingsStore.StickSensitivity * Time.unscaledDeltaTime);
            }

            if (gamepad.buttonSouth.wasPressedThisFrame) Press();
            if (gamepad.buttonSouth.wasReleasedThisFrame) Release();
        }

        private Vector2 ClampLocal(Vector2 value)
        {
            Rect bounds = rootRect.rect;
            float halfCursor = cursorSize * 0.5f;
            value.x = Mathf.Clamp(value.x, bounds.xMin + halfCursor, bounds.xMax - halfCursor);
            value.y = Mathf.Clamp(value.y, bounds.yMin + halfCursor, bounds.yMax - halfCursor);
            return value;
        }

        private void SetVisible(bool show)
        {
            Cursor.visible = false;
            if (visible == show) return;
            visible = show;
            root.SetActive(show);

            if (show)
            {
                activeDevice = SettingsStore.Device;
                if (activeDevice == SettingsStore.InputDevice.KeyboardMouse && Mouse.current != null)
                    UpdateMouse();
                else
                {
                    localPos = Vector2.zero;
                    cursorRect.anchoredPosition = localPos;
                }
            }
            else
            {
                CancelPress();
                ClearHover();
            }
        }

        private void EnsurePointerData()
        {
            if (pointerData == null && EventSystem.current != null)
                pointerData = new PointerEventData(EventSystem.current);
        }

        private Vector2 CursorScreenPoint() => RectTransformUtility.WorldToScreenPoint(null, cursorRect.position);

        private void UpdateHover()
        {
            if (EventSystem.current == null) return;
            EnsurePointerData();
            if (pointerData == null) return;

            pointerData.position = CursorScreenPoint();
            raycastResults.Clear();
            EventSystem.current.RaycastAll(pointerData, raycastResults);
            GameObject target = raycastResults.Count > 0 ? raycastResults[0].gameObject : null;
            if (target == hovered) return;

            if (hovered != null)
                ExecuteEvents.ExecuteHierarchy(hovered, pointerData, ExecuteEvents.pointerExitHandler);
            hovered = target;
            if (hovered != null)
                ExecuteEvents.ExecuteHierarchy(hovered, pointerData, ExecuteEvents.pointerEnterHandler);
        }

        private void Press()
        {
            if (EventSystem.current == null || hovered == null) return;
            EnsurePointerData();
            if (pointerData == null) return;
            pointerData.position = CursorScreenPoint();
            pressedTarget = ExecuteEvents.ExecuteHierarchy(hovered, pointerData, ExecuteEvents.pointerDownHandler);
        }

        public void Release()
        {
            if (pressedTarget == null) return;
            EnsurePointerData();
            if (pointerData == null) { pressedTarget = null; return; }

            pointerData.position = CursorScreenPoint();
            ExecuteEvents.Execute(pressedTarget, pointerData, ExecuteEvents.pointerUpHandler);
            GameObject clickTarget = hovered != null
                ? ExecuteEvents.GetEventHandler<IPointerClickHandler>(hovered)
                : null;
            if (clickTarget == pressedTarget)
                ExecuteEvents.Execute(pressedTarget, pointerData, ExecuteEvents.pointerClickHandler);
            pressedTarget = null;
        }

        private void CancelPress()
        {
            if (pressedTarget == null) return;
            EnsurePointerData();
            if (pointerData != null)
                ExecuteEvents.Execute(pressedTarget, pointerData, ExecuteEvents.pointerUpHandler);
            pressedTarget = null;
        }

        private void ClearHover()
        {
            if (hovered == null) return;
            EnsurePointerData();
            if (pointerData != null)
                ExecuteEvents.ExecuteHierarchy(hovered, pointerData, ExecuteEvents.pointerExitHandler);
            hovered = null;
        }

        private static Sprite DiscSprite
        {
            get
            {
                if (discSprite != null) return discSprite;
                const int size = 32;
                Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                float radius = size * 0.5f - 1f;
                Vector2 center = Vector2.one * size * 0.5f;
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                    texture.SetPixel(x, y, distance <= radius ? Color.white : Color.clear);
                }
                texture.Apply();
                discSprite = Sprite.Create(texture, new Rect(0, 0, size, size), Vector2.one * 0.5f, 100f);
                return discSprite;
            }
        }
    }
}
