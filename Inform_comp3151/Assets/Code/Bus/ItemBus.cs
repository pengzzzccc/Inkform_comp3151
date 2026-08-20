using System;
using Inkform.Item;
using UnityEngine;

namespace Inkform.Bus
{
    /// <summary>Announcements for inventory storage and world release.</summary>
    public static class ItemBus
    {
        public static event Action<InventoryItemDefinition> ItemStored;
        public static event Action<InventoryItemDefinition, Vector2, Vector2> ItemReleased;

        public static void RaiseItemStored(InventoryItemDefinition item) => ItemStored?.Invoke(item);

        public static void RaiseItemReleased(InventoryItemDefinition item, Vector2 pos, Vector2 velocity) =>
            ItemReleased?.Invoke(item, pos, velocity);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            ItemStored = null;
            ItemReleased = null;
        }
    }
}
