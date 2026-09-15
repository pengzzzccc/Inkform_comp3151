using System;
using Inkform.Bus;
using UnityEngine.UIElements;

namespace Inkform.UI
{
    /// <summary>
    /// One Celeste TextMenu.Option row: label on the left, current value right-aligned between
    /// a pair of chevrons. The row itself is the focusable control.
    ///
    /// Left/right navigation is intercepted while the row has focus (PreventDefault stops the
    /// focus controller from moving on) and reported as Stepped — the Celeste "arrows change the
    /// value, up/down still move the selection" behaviour. A click or Submit steps forward, same
    /// as the old two-arrow rows where both arrows flipped two-state values.
    ///
    /// Value switches play the Celeste value nudge (UiFx.Nudge) and raise UiBus.Toggled so the
    /// UIManager's toggleOn/toggleOff cues fire — right step = on, left step = off, exactly what
    /// UiToggleFx used to raise.
    /// </summary>
    public class OptionRow : VisualElement
    {
        private readonly Label valueLabel;
        private readonly Label leftArrow;
        private readonly Label rightArrow;

        /// <summary>The value was stepped by the given direction: -1 = left, +1 = right/click.</summary>
        public event Action<int> Stepped;

        /// <summary>The row was clicked / submitted. Distinct from Stepped so click-only rows
        /// (Unstuck, Reset Bindings) have something to wire.</summary>
        public event Action Confirmed;

        public OptionRow(string labelText)
        {
            AddToClassList("option-row");
            AddToClassList("floaty");
            focusable = true;

            var label = new Label(labelText);
            label.AddToClassList("row-label");
            label.AddToClassList("outline");
            label.pickingMode = PickingMode.Ignore;
            Add(label);

            leftArrow = new Label("<");
            leftArrow.AddToClassList("row-arrow");
            leftArrow.AddToClassList("outline");
            leftArrow.pickingMode = PickingMode.Ignore;
            Add(leftArrow);

            valueLabel = new Label();
            valueLabel.AddToClassList("row-value");
            valueLabel.AddToClassList("outline");
            valueLabel.pickingMode = PickingMode.Ignore;
            Add(valueLabel);

            rightArrow = new Label(">");
            rightArrow.AddToClassList("row-arrow");
            rightArrow.AddToClassList("outline");
            rightArrow.pickingMode = PickingMode.Ignore;
            Add(rightArrow);

            RegisterCallback<NavigationMoveEvent>(OnNavigate);
            RegisterCallback<ClickEvent>(OnClick);
            RegisterCallback<PointerEnterEvent>(OnPointerEnter);
            RegisterCallback<FocusInEvent>(OnFocusIn);
        }

        /// <summary>The current value text (already formatted by the owning panel).</summary>
        public string Value
        {
            set => valueLabel.text = value;
        }

        /// <summary>Hides the chevrons for click-only rows (Unstuck, Reset Bindings).</summary>
        public void ShowArrows(bool show)
        {
            DisplayStyle style = show ? DisplayStyle.Flex : DisplayStyle.None;
            leftArrow.style.display = style;
            rightArrow.style.display = style;
        }

        private void OnNavigate(NavigationMoveEvent e)
        {
            // Left/right on an option row change the value and must not move focus. Unity 6 moved
            // the focus change out of the event's default action into a propagation-phase handler
            // (EventBase.PreventDefault is obsolete with exactly this guidance), so stopping the
            // propagation here keeps the focus on this row while we step the value.
            switch (e.direction)
            {
                case NavigationMoveEvent.Direction.Left:
                    e.StopImmediatePropagation();
                    Step(-1);
                    break;
                case NavigationMoveEvent.Direction.Right:
                    e.StopImmediatePropagation();
                    Step(+1);
                    break;
            }
        }

        private void OnClick(ClickEvent e)
        {
            // A click changes the value like a right-step does: it plays the toggle cue (raised
            // by Step), not the general click cue — Celeste's option rows sound the same way.
            UiFx.Bounce(this);
            Confirmed?.Invoke();
            Step(+1);
        }

        private void Step(int dir)
        {
            UiFx.Nudge(valueLabel, dir);
            UiBus.RaiseToggled(dir > 0);
            Stepped?.Invoke(dir);
        }

        private void OnPointerEnter(PointerEnterEvent _) => UiBus.RaiseHovered();

        private void OnFocusIn(FocusInEvent _) => UiBus.RaiseHovered();
    }
}
