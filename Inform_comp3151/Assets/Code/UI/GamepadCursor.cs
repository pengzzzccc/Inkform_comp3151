using System.Collections.Generic;
using Inkform.Settings;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Inkform.UI
{
    /// <summary>
    /// Gamepad virtual cursor: Apex-style UI control for the menu layer. The left stick drives an
    /// on-screen cursor (the same aim_cursor art the rope gun reticle uses), and the south face button
    /// (<Gamepad>/buttonSouth, A) presses and releases like a mouse button — see Press / Release, which
    /// is what lets a pad hold a control down. Movement speed scales with SettingsStore.StickSensitivity.
    /// The right stick scrolls the ScrollRect under the cursor (the settings tabs).
    ///
    /// This replaces the stock focus-highlight navigation for gamepads: the shared input asset's UI map
    /// no longer binds any Gamepad/Joystick controls (see InputSystem_Actions), so the EventSystem never
    /// hears a stick or button from the pad. This component reads Gamepad.current directly and drives the
    /// same pointer pipeline a mouse does — hover and click go through ExecuteEvents, so
    /// UiSelectableFx's scale/audio feedback and Button.onClick all fire unchanged.
    ///
    /// The cursor is a runtime-created overlay Canvas (sorting order 200, above the menu Canvas at 100)
    /// holding one non-raycast Image, parented to the persistent GameManager. It shows only while a
    /// Gamepad is connected and at least one panel is open (UIManager.AnyPanelOpen). No GraphicRaycaster
    /// is added to this canvas: the cursor must never hit-test itself, the menu canvas does the raycast.
    /// </summary>
    public class GamepadCursor : MonoBehaviour
    {
        // Same 1920x1080 reference the menu CanvasScaler uses (UIManager.CreateCanvas); kept in step by
        // hand, same as FpsDisplay. Clamping to these bounds keeps the cursor on screen regardless of the
        // scaler's match split, which is what a hardcoded Screen.width/height could not do.
        private const float ReferenceWidth = 1920f;
        private const float ReferenceHeight = 1080f;

        [SerializeField] private Sprite cursorSprite;       // aim_cursor; wired by UIBuilder. Null -> generated disc fallback.
        [SerializeField] private float cursorSpeed = 1000f;  // reference-space units/sec at StickSensitivity = 1
        [SerializeField] private float cursorSize = 40f;     // reference-space size of the cursor image

        /// <summary>Right-stick scroll speed in normalized ScrollRect units per second at sensitivity 1.</summary>
        private const float ScrollSpeed = 1.2f;

        /// <summary>The sprite this cursor renders (aim_cursor); UIManager reuses it for the OS pointer.</summary>
        public Sprite CursorSprite => cursorSprite;

        private GameObject root;
        private RectTransform cursorRect;
        private Vector2 localPos;

        private PointerEventData pointerData;
        private readonly List<RaycastResult> raycastResults = new List<RaycastResult>();
        private GameObject hovered;
        private GameObject pressedTarget;   // took the pointerDown; the release goes back to this one
        private bool visible;

        private static Sprite discSprite;

        void Awake()
        {
            CreateCursor();
        }

        private void CreateCursor()
        {
            root = new GameObject("Gamepad Cursor");
            root.transform.SetParent(transform);

            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000;   // always above every other Canvas (menu 100, FpsDisplay 50)

            CanvasScaler scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            GameObject img = new GameObject("Cursor", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            img.transform.SetParent(root.transform, false);

            Image cursorImage = img.GetComponent<Image>();
            cursorImage.sprite = cursorSprite != null ? cursorSprite : DiscSprite;
            cursorImage.raycastTarget = false;

            cursorRect = (RectTransform)img.transform;
            cursorRect.anchorMin = cursorRect.anchorMax = new Vector2(0.5f, 0.5f);
            cursorRect.sizeDelta = new Vector2(cursorSize, cursorSize);

            root.SetActive(false);
        }

        void Update()
        {
            Gamepad gamepad = Gamepad.current;
            bool shouldShow = gamepad != null && UIManager.Instance != null && UIManager.Instance.AnyPanelOpen;

            SetVisible(shouldShow);
            if (!visible || gamepad == null) return;

            // The stock focus highlight (EventSystem.currentSelectedGameObject) is a mouse/keyboard
            // concern now; a gamepad cursor must not leave a stale highlight underneath it. Clearing
            // every frame while a pad is attached means keyboard WASD navigation stays inert for pad
            // owners — intended, they navigate with the cursor — and the mouse is untouched.
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null)
                EventSystem.current.SetSelectedGameObject(null);

            Vector2 stick = gamepad.leftStick.ReadValue();

            // A pad driving the cursor is the strongest possible "gamepad user" signal: flip the
            // input family now so it is already correct when gameplay starts (menu -> level is seamless)
            if ((stick.sqrMagnitude > 0.0001f || gamepad.buttonSouth.wasPressedThisFrame)
                && SettingsStore.Device != SettingsStore.InputDevice.Gamepad)
                SettingsStore.SetDevice(SettingsStore.InputDevice.Gamepad);

            if (stick.sqrMagnitude > 0.0001f)
            {
                localPos += stick * cursorSpeed * SettingsStore.StickSensitivity * Time.unscaledDeltaTime;

                float halfW = ReferenceWidth * 0.5f;
                float halfH = ReferenceHeight * 0.5f;
                localPos.x = Mathf.Clamp(localPos.x, -halfW, halfW);
                localPos.y = Mathf.Clamp(localPos.y, -halfH, halfH);
                cursorRect.anchoredPosition = localPos;
            }

            UpdateHover();

            // Right stick scrolls the ScrollRect under the cursor (the settings tabs are ScrollRects);
            // a 0.2 deadzone keeps drift from scrolling. Only runs while a panel is open (see above).
            Vector2 rstick = gamepad.rightStick.ReadValue();
            if (Mathf.Abs(rstick.y) > 0.2f && hovered != null)
            {
                ScrollRect scroll = hovered.GetComponentInParent<ScrollRect>();
                if (scroll != null && scroll.vertical)
                    scroll.verticalNormalizedPosition = Mathf.Clamp01(
                        scroll.verticalNormalizedPosition
                        + rstick.y * ScrollSpeed * SettingsStore.StickSensitivity * Time.unscaledDeltaTime);
            }

            // Two separate ifs, not else-if: a tap short enough to press and release inside one frame
            // must still produce both halves, or the control would stay stuck down.
            if (gamepad.buttonSouth.wasPressedThisFrame) Press();
            if (gamepad.buttonSouth.wasReleasedThisFrame) Release();
        }

        private void SetVisible(bool show)
        {
            if (visible == show) return;
            visible = show;
            root.SetActive(show);

            if (show)
            {
                localPos = Vector2.zero;
                cursorRect.anchoredPosition = Vector2.zero;
                hovered = null;
                pressedTarget = null;   // whatever was held when the cursor last vanished is long gone
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

        private void UpdateHover()
        {
            if (EventSystem.current == null) return;
            EnsurePointerData();

            pointerData.position = RectTransformUtility.WorldToScreenPoint(null, cursorRect.position);
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

        // A real press/release pair rather than down/up/click fired in a single frame, which is what
        // this used to do. One frame is indistinguishable from a tap, so no control could ever observe
        // a *hold* from a pad — and the save menu's slots are exactly that: tap to continue, hold to
        // offer an overwrite (see UiHoldButton). It also means the press scale in UiSelectableFx
        // finally plays for pad users, who previously saw the button snap back the same frame.
        private void Press()
        {
            if (EventSystem.current == null || hovered == null) return;
            EnsurePointerData();
            if (pointerData == null) return;

            pointerData.position = RectTransformUtility.WorldToScreenPoint(null, cursorRect.position);
            pressedTarget = ExecuteEvents.ExecuteHierarchy(hovered, pointerData, ExecuteEvents.pointerDownHandler);
        }

        private void Release()
        {
            if (pressedTarget == null) return;
            EnsurePointerData();
            if (pointerData == null) { pressedTarget = null; return; }

            pointerData.position = RectTransformUtility.WorldToScreenPoint(null, cursorRect.position);
            ExecuteEvents.Execute(pressedTarget, pointerData, ExecuteEvents.pointerUpHandler);

            // Same rule the mouse follows: a release only counts as a click when it lands back on the
            // control the press started on, so sliding the cursor off a button cancels it. pointerClick
            // is what reaches Button.onClick and UiSelectableFx's click sound; toggles flip on their
            // own IPointerClickHandler here too.
            GameObject clickTarget = hovered != null
                ? ExecuteEvents.GetEventHandler<IPointerClickHandler>(hovered)
                : null;
            if (clickTarget == pressedTarget)
                ExecuteEvents.Execute(pressedTarget, pointerData, ExecuteEvents.pointerClickHandler);

            pressedTarget = null;
        }

        /// <summary>Ends a press without producing a click — for when the cursor is taken away
        /// mid-hold (panel closed, pad unplugged). Without it the control keeps its pressed visual
        /// state and UiHoldButton never learns the press ended.</summary>
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

        /// <summary>White disc fallback when no cursor sprite is wired (mirrors RopeGun.DiscSprite).</summary>
        private static Sprite DiscSprite
        {
            get
            {
                if (discSprite != null) return discSprite;

                const int size = 32;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                float r = size * 0.5f - 1f;
                Vector2 c = new Vector2(size * 0.5f, size * 0.5f);
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c);
                        tex.SetPixel(x, y, d <= r ? Color.white : Color.clear);
                    }
                }
                tex.Apply();
                discSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
                return discSprite;
            }
        }
    }
}
