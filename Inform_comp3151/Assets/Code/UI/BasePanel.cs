using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Inkform.UI
{
    /// <summary>
    /// UI panel base class. A panel is one full-screen (or overlay) sheet under a Canvas,
    /// e.g. the main menu, the pause menu, the settings sheet. Every concrete panel derives
    /// from this class and is registered on the UIManager.
    ///
    /// The panel itself knows nothing about the game or the UIManager's pause state machine:
    /// Open/Close only toggle visibility (CanvasGroup alpha + interactability + raycast block).
    /// The fade is immediate by default; subclass by overriding SetVisible for animated fades.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public abstract class BasePanel : MonoBehaviour
    {
        private CanvasGroup group;

        public bool IsOpen { get; private set; }

        protected virtual void Awake()
        {
            group = GetComponent<CanvasGroup>();

            // Force the visuals to agree with IsOpen == false. Without this the panel's real state
            // comes from the prefab (UIBuilder saves them active, alpha 1, blocksRaycasts on, behind a
            // full-screen opaque Image) while IsOpen already reads false — and because Close() early-
            // returns on !IsOpen, nothing could ever bring the two back in sync: every panel stayed
            // visible and swallowed every click, forever.
            // Safe inside Awake: deactivating here does not interrupt the running Awake call stack, so
            // subclasses finish their own Awake (button lookup + listener wiring) normally.
            ApplyState(false);
        }

        /// <summary>Shows the panel and makes it interactable.</summary>
        public void Open()
        {
            if (IsOpen) return;

            IsOpen = true;
            ApplyState(true);

            OnOpen();
        }

        /// <summary>Hides the panel and stops it from receiving pointer events.</summary>
        public void Close()
        {
            if (!IsOpen) return;

            IsOpen = false;

            OnClose();                  // runs while still active, so the hook can touch children
            ApplyState(false);
        }

        // The single visible/hidden switch: every state toggle lives here, never scattered across call
        // sites (same shape as BreakablePart.SetBroken). Deliberately unconditional — it is also the
        // one-shot initializer that lands the prefab's state onto IsOpen's default.
        private void ApplyState(bool open)
        {
            if (open) gameObject.SetActive(true);

            group.alpha = open ? 1f : 0f;
            group.interactable = open;
            group.blocksRaycasts = open;

            if (!open) gameObject.SetActive(false);
        }

        /// <summary>Hook after the panel becomes visible.</summary>
        protected virtual void OnOpen() { }

        /// <summary>Hook before the panel becomes hidden.</summary>
        protected virtual void OnClose() { }

        // ---- Child lookup, shared by every panel ----

        /// <summary>
        /// Finds a Button among the panel's **direct** children by exact name; missing returns null.
        /// Direct children only is deliberate: UIBuilder parents every button straight onto the panel
        /// root, so a name that fails to resolve means the name is wrong, not that it is nested deeper.
        /// </summary>
        protected Button FindButton(string name)
        {
            Transform t = transform.Find(name);
            return t != null ? t.GetComponent<Button>() : null;
        }

        /// <summary>Finds a direct child GameObject by exact name; missing returns null.</summary>
        protected GameObject FindChild(string name)
        {
            Transform t = transform.Find(name);
            return t != null ? t.gameObject : null;
        }

        /// <summary>
        /// Wires a click handler, warning instead of throwing when the button is missing. Panels build
        /// their wiring in Awake, so an unguarded null here would abort the rest of Awake and silently
        /// leave every *later* button dead too — one renamed child would take down the whole sheet.
        /// </summary>
        protected void Bind(Button button, string name, UnityAction onClick)
        {
            if (button == null)
            {
                Debug.LogWarning($"{GetType().Name}: no Button named '{name}' among {this.name}'s direct children — that control does nothing", this);
                return;
            }

            button.onClick.AddListener(onClick);
        }
    }
}
