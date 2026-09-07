using Inkform.Item;
using UnityEngine;
using UnityEngine.UI;

namespace Inkform.UI
{
    /// <summary>Persistent compact readout for the next FIFO item and current count/capacity.</summary>
    public sealed class InventoryHud : MonoBehaviour
    {
        public static InventoryHud Instance { get; private set; }

        [SerializeField] private GameObject hudRoot;
        [SerializeField] private Image currentIcon;
        [SerializeField] private Text countText;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
        }

        private void OnEnable()
        {
            InventoryStore.Changed += Refresh;
            Refresh();
        }

        private void OnDisable()
        {
            InventoryStore.Changed -= Refresh;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void RefreshVisibility() => Refresh();

        private void Refresh()
        {
            if (hudRoot == null || currentIcon == null || countText == null) return;

            bool hasItem = InventoryStore.TryPeekFirst(out InventoryItemDefinition first);
            currentIcon.sprite = hasItem ? first.Icon : null;
            currentIcon.enabled = hasItem && first.Icon != null;
            countText.text = $"{InventoryStore.Count}/{InventoryStore.Capacity}";
        }
    }
}
