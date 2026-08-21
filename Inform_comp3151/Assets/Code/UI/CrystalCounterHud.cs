using Inkform.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace Inkform.UI
{
    /// <summary>Persistent gameplay-only counter for the Crystal Mine currency.</summary>
    public sealed class CrystalCounterHud : MonoBehaviour
    {
        private GameObject root;
        private Text label;

        private void Awake()
        {
            root = GameplayHudFactory.CreatePanel(transform, "Crystal Counter HUD", 91,
                new Vector2(210f, 62f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-34f, -34f));
            label = GameplayHudFactory.CreateText(root.transform, 25, TextAnchor.MiddleCenter);
        }

        private void OnEnable()
        {
            RunProgressStore.Changed += Refresh;
            Refresh();
        }

        private void OnDisable() => RunProgressStore.Changed -= Refresh;
        public void RefreshVisibility() => Refresh();

        private void Refresh()
        {
            if (root == null) return;
            bool gameplay = UIManager.Instance != null && !UIManager.Instance.IsInMainMenu;
            root.SetActive(gameplay);
            label.text = $"Crystals  {RunProgressStore.CrystalCount}";
        }
    }
}
