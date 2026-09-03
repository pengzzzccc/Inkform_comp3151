using Inkform.Bus;
using Inkform.Interactable;
using Inkform.Item;
using UnityEngine;

namespace Inkform.Player
{
    /// <summary>Player-side inventory gameplay facade. World parts store through this component;
    /// player actions consume or release through it. InventoryStore remains the data/save backing.</summary>
    public class PlayerInventory : MonoBehaviour
    {
        [SerializeField] private float spitOffset = 0.9f;
        [SerializeField] private float spitSpeed = 24f;

        public int Count => InventoryStore.Count;
        public int Capacity => InventoryStore.Capacity;
        public bool IsEmpty => InventoryStore.Count == 0;

        public bool TryStore(InventoryItemDefinition definition) => InventoryStore.TryAdd(definition);

        public bool TryCollectCapacityUpgrade(string pickupId, int capacityIncrease = 1) =>
            InventoryStore.TryCollectCapacityPickup(pickupId, capacityIncrease);

        public bool TryConsumeDashFuel()
        {
            for (int i = 0; i < InventoryStore.Items.Count; i++)
            {
                InventoryItemDefinition item = InventoryStore.Items[i];
                if (item != null && item.IsDashFuel)
                    return InventoryStore.RemoveAt(i);
            }

            return false;
        }

        public bool TryReleaseFirst(Vector2 dir)
        {
            if (!InventoryStore.TryPeekFirst(out InventoryItemDefinition definition)) return false;
            if (definition.WorldPrefab == null)
            {
                Debug.LogWarning($"Inventory item '{definition.Id}' has no world prefab; retaining it in the backpack", definition);
                return false;
            }

            Vector2 mouth = (Vector2)transform.position + dir * spitOffset;
            GameObject instance = Instantiate(definition.WorldPrefab, mouth, Quaternion.identity);
            Inkform.Interactable.Interactable node =
                instance.GetComponentInChildren<Inkform.Interactable.Interactable>(true);
            if (node == null || !node.TryGetPart(out ICarriable carriable))
            {
                Debug.LogWarning($"Inventory prefab '{definition.WorldPrefab.name}' has no Interactable ICarriable part; retaining the item", instance);
                Destroy(instance);
                return false;
            }

            Vector2 velocity = dir * spitSpeed;
            carriable.Release(mouth, velocity);
            InventoryStore.RemoveFirst();
            ItemBus.RaiseItemReleased(definition, mouth, velocity);
            return true;
        }
    }
}
