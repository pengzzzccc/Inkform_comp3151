using Inkform.Bus;
using UnityEngine;
using UnityEngine.UI;

namespace Inkform.UI
{
    /// <summary>Non-modal gameplay prompt for the nearest E/Y interaction.</summary>
    public sealed class InteractionPromptHud : MonoBehaviour
    {
        private GameObject root;
        private Text label;

        private void Awake()
        {
            root = GameplayHudFactory.CreatePanel(transform, "Interaction Prompt HUD", 92,
                new Vector2(620f, 90f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 54f));
            label = GameplayHudFactory.CreateText(root.transform, 25, TextAnchor.MiddleCenter);
        }

        private void OnEnable()
        {
            InteractionBus.Changed += Refresh;
            Refresh(InteractionBus.Current);
        }

        private void OnDisable() => InteractionBus.Changed -= Refresh;
        public void RefreshVisibility() => Refresh(InteractionBus.Current);

        private void Refresh(InteractionPrompt prompt)
        {
            if (root == null) return;
            bool gameplay = UIManager.Instance != null && !UIManager.Instance.IsInMainMenu;
            root.SetActive(gameplay && prompt.IsVisible);
            if (!root.activeSelf) return;

            label.color = prompt.Available ? Color.white : new Color(1f, 0.62f, 0.42f);
            label.text = prompt.Available
                ? $"[E / Y]  {prompt.Action}"
                : $"[E / Y]  {prompt.Action}\n{prompt.UnavailableReason}";
        }
    }
}
