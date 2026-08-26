using UnityEngine;

namespace Inkform.Item
{
    /// <summary>Stable, saveable identity for a carriable world prefab.</summary>
    [CreateAssetMenu(menuName = "Item/Inventory Item Definition")]
    public sealed class InventoryItemDefinition : ScriptableObject
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private Sprite icon;
        [SerializeField] private GameObject worldPrefab;
        [SerializeField] private bool dashFuel;

        public string Id => id;
        public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;
        public Sprite Icon => icon;
        public GameObject WorldPrefab => worldPrefab;
        public bool IsDashFuel => dashFuel;
    }
}
