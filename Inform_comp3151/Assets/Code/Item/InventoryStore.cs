using System;
using System.Collections.Generic;
using Inkform.Save;
using UnityEngine;

namespace Inkform.Item
{
    /// <summary>Four-slot, data-backed inventory that survives scenes and is mirrored into SaveStore.</summary>
    public static class InventoryStore
    {
        public const int Capacity = 4;

        public static event Action Changed;

        private static readonly List<InventoryItemDefinition> items = new List<InventoryItemDefinition>(Capacity);
        private static Dictionary<string, InventoryItemDefinition> catalogue;
        private static int selectedIndex;
        private static bool suppressPersistence;

        public static IReadOnlyList<InventoryItemDefinition> Items => items;
        public static int Count => items.Count;
        public static int SelectedIndex => items.Count == 0 ? -1 : selectedIndex;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Changed = null;
            items.Clear();
            catalogue = null;
            selectedIndex = 0;
            suppressPersistence = false;
        }

        public static bool TryAdd(InventoryItemDefinition definition)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.Id) || items.Count >= Capacity)
                return false;

            items.Add(definition);
            if (items.Count == 1) selectedIndex = 0;
            Notify();
            return true;
        }

        public static bool Select(int index)
        {
            if (index < 0 || index >= items.Count || selectedIndex == index) return false;
            selectedIndex = index;
            Notify();
            return true;
        }

        public static bool TryPeekSelected(out InventoryItemDefinition definition)
        {
            if (items.Count == 0)
            {
                definition = null;
                return false;
            }
            selectedIndex = Mathf.Clamp(selectedIndex, 0, items.Count - 1);
            definition = items[selectedIndex];
            return definition != null;
        }

        public static bool RemoveSelected()
        {
            if (items.Count == 0) return false;
            selectedIndex = Mathf.Clamp(selectedIndex, 0, items.Count - 1);
            items.RemoveAt(selectedIndex);
            if (items.Count == 0) selectedIndex = 0;
            else selectedIndex = Mathf.Min(selectedIndex, items.Count - 1);
            Notify();
            return true;
        }

        public static void Clear() => ClearInternal(true);

        internal static void ClearWithoutSaving() => ClearInternal(false);

        private static void ClearInternal(bool persist)
        {
            bool changed = items.Count > 0 || selectedIndex != 0;
            items.Clear();
            selectedIndex = 0;
            if (changed) Notify(persist);
        }

        public static void Restore(string[] ids, int selected)
        {
            suppressPersistence = true;
            try
            {
                items.Clear();
                EnsureCatalogue();

                if (ids != null)
                {
                    foreach (string id in ids)
                    {
                        if (items.Count >= Capacity) break;
                        if (string.IsNullOrWhiteSpace(id)) continue;
                        if (catalogue.TryGetValue(id, out InventoryItemDefinition definition)) items.Add(definition);
                        else Debug.LogWarning($"InventoryStore: saved item id '{id}' has no definition; skipping it");
                    }
                }

                selectedIndex = items.Count == 0 ? 0 : Mathf.Clamp(selected, 0, items.Count - 1);
            }
            finally
            {
                suppressPersistence = false;
            }
            Changed?.Invoke();
        }

        public static string[] SnapshotIds()
        {
            var result = new string[items.Count];
            for (int i = 0; i < items.Count; i++) result[i] = items[i] != null ? items[i].Id : string.Empty;
            return result;
        }

        private static void EnsureCatalogue()
        {
            if (catalogue != null) return;
            catalogue = new Dictionary<string, InventoryItemDefinition>(StringComparer.Ordinal);
            foreach (InventoryItemDefinition definition in Resources.LoadAll<InventoryItemDefinition>("Inventory"))
            {
                if (definition == null || string.IsNullOrWhiteSpace(definition.Id)) continue;
                if (!catalogue.TryAdd(definition.Id, definition))
                    Debug.LogWarning($"InventoryStore: duplicate inventory id '{definition.Id}'");
            }
        }

        private static void Notify(bool persist = true)
        {
            Changed?.Invoke();
            if (persist && !suppressPersistence)
                SaveStore.RecordInventory(SnapshotIds(), SelectedIndex);
        }
    }
}
