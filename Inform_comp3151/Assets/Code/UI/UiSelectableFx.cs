using Inkform.Bus;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Inkform.UI
{
    /// <summary>
    /// Hover / press / selection feel shared by every interactive control: a scale nudge plus the
    /// UiBus signals that drive the menu sounds. UiButtonFx and UiToggleFx add their own colour
    /// handling on top.
    ///
    /// This component drives *scale only* for buttons — colour stays with uGUI's own ColorTint
    /// transition, whose ColorBlock UIBuilder now fills in properly. Two writers on one Image.color
    /// would fight, and going Transition.None instead would silently kill the settings sheet's tab
    /// indicator, which is expressed as tab.interactable = false (SettingsPanel.SetTab).
    ///
    /// Everything here is unscaled: UIManager.SetPaused drops Time.timeScale to 0, so a paused menu
    /// animating on scaled time would simply freeze. Same reason RumbleManager gives for its own
    /// coroutine.
    /// </summary>
    [DisallowMultipleComponent]
    public abstract class UiSelectableFx : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler,
        ISelectHandler, IDeselectHandler,
        IPointerClickHandler, ISubmitHandler
    {
        protected const float HoverScale = 1.06f;
        protected const float PressScale = 0.97f;

        // Units of scale per second. 0.5 takes the 0.06 hover step in ~0.12s — quick enough to feel
        // attached to the pointer, slow enough to read as motion.
        private const float ScaleSpeed = 0.5f;

        /// <summary>The Selectable this component decorates. Supplied by the subclass so the
        /// interactable check reads the real control rather than a cached guess.</summary>
        protected abstract Selectable Target { get; }

        private bool pointerInside;
        private bool pressed;
        private bool selected;

        protected virtual void OnDisable()
        {
            // Panels hide by SetActive(false) (BasePanel.ApplyState), which means a control can be
            // switched off mid-hover and never receive its OnPointerExit. Without this reset the
            // enlarged scale would be baked in and still be there on the next open.
            pointerInside = false;
            pressed = false;
            selected = false;
            transform.localScale = Vector3.one;
        }

        protected virtual void Update()
        {
            float target = 1f;
            if (Interactable)
                target = pressed ? PressScale : (pointerInside || selected) ? HoverScale : 1f;

            float current = transform.localScale.x;
            if (!Mathf.Approximately(current, target))
            {
                float next = Mathf.MoveTowards(current, target, ScaleSpeed * Time.unscaledDeltaTime);
                transform.localScale = new Vector3(next, next, 1f);
            }
        }

        protected bool Interactable => Target != null && Target.IsInteractable();

        /// <summary>Pointer is over this control, or navigation has it selected. Subclasses that own
        /// their own colour (UiToggleFx) read this to decide whether to show the lifted shade.</summary>
        protected bool IsHighlighted => pointerInside || selected;

        // ---- Pointer ----

        public void OnPointerEnter(PointerEventData eventData)
        {
            pointerInside = true;
            if (Interactable) UiBus.RaiseHovered();
        }

        public void OnPointerExit(PointerEventData eventData) => pointerInside = false;

        public void OnPointerDown(PointerEventData eventData) => pressed = true;

        public void OnPointerUp(PointerEventData eventData) => pressed = false;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (Interactable) OnActivated();
        }

        // ---- Navigation ----

        public void OnSelect(BaseEventData eventData)
        {
            selected = true;

            // Clicking with the mouse also selects, so an unconditional hover sound here would double
            // up with the one OnPointerEnter already played. The pointer being inside is exactly the
            // case where that happens; gamepad and keyboard navigation never is.
            if (Interactable && !pointerInside) UiBus.RaiseHovered();
        }

        public void OnDeselect(BaseEventData eventData) => selected = false;

        public void OnSubmit(BaseEventData eventData)
        {
            if (Interactable) OnActivated();
        }

        // Both Selectable and this component implement these interfaces; the EventSystem executes
        // every matching handler on the object, so the control's own behaviour still runs. Clicks
        // keep going through the Button's onClick listener (BasePanel.Bind) — this is presentation.
        /// <summary>The control was activated. Buttons announce a click; toggles stay quiet here and
        /// speak from onValueChanged instead, so a silent SetIsOnWithoutNotify pull cannot sound.</summary>
        protected virtual void OnActivated() => UiBus.RaiseClicked();
    }
}
