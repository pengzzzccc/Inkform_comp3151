using Inkform.Bus;
using Inkform.Interactable;
using Inkform.Item;
using UnityEngine;

namespace Inkform.Player
{
    /// <summary>Releases the selected data-backed inventory item from the player's mouth.</summary>
    public class ItemCarrier : MonoBehaviour
    {
        [SerializeField] private float spitOffset = 0.9f;
        [SerializeField] private float spitSpeed = 24f;

        public bool IsEmpty => InventoryStore.Count == 0;

        public bool TryRelease(Vector2 dir)
        {
            if (!InventoryStore.TryPeekSelected(out InventoryItemDefinition definition)) return false;
            if (definition.WorldPrefab == null)
            {
                Debug.LogWarning($"Inventory item '{definition.Id}' has no world prefab; retaining it in the backpack", definition);
                return false;
            }

            Vector2 mouth = (Vector2)transform.position + dir * spitOffset;
            GameObject instance = Instantiate(definition.WorldPrefab, mouth, Quaternion.identity);
            ICarriable carriable = instance.GetComponent<ICarriable>();
            if (carriable == null)
            {
                Debug.LogWarning($"Inventory prefab '{definition.WorldPrefab.name}' has no ICarriable; retaining the item", instance);
                Destroy(instance);
                return false;
            }

            Vector2 velocity = dir * spitSpeed;
            carriable.Release(mouth, velocity);
            InventoryStore.RemoveSelected();
            ItemBus.RaiseItemReleased(definition, mouth, velocity);
            return true;
        }
    }
}
