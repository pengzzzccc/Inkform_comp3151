using UnityEngine;
using UnityEngine.UI;

namespace Inkform.UI
{
    /// <summary>
    /// Hover / press feel for a Button. All of the behaviour lives in UiSelectableFx; this only names
    /// which Selectable to read. Added to every button by UIBuilder.AddButton, so a button gains the
    /// effect by existing — there is nothing to wire per panel.
    ///
    /// Colour is deliberately not handled here: UIBuilder gives the Button a real ColorBlock and lets
    /// uGUI's ColorTint run it. See UiSelectableFx for why.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class UiButtonFx : UiSelectableFx
    {
        private Button button;

        protected override Selectable Target
        {
            get
            {
                if (button == null) button = GetComponent<Button>();
                return button;
            }
        }
    }
}
