using Inkform.Interactable;
using System;
using UnityEngine;

namespace Inkform.Bus
{
    /// <summary>
    /// Item bus: published by the item itself when eaten, subscribed by PlayerHandler.
    /// The player side only deals with the ICarriable interface — concrete implementations
    /// (CarriablePart) never need to be known. Item prefabs therefore need no serialized
    /// scene references and can be freely Instantiated at runtime.
    /// </summary>
    public static class ItemBus
    {
        /// <summary>The player ate an item. The parameter is the item itself; listeners can distinguish kinds by concrete type.</summary>
        public static event Action<ICarriable> ItemEaten;

        /// <summary>The player spit out the held item. pos = spawn point (mouth), velocity = initial velocity.</summary>
        public static event Action<ICarriable, Vector2, Vector2> ItemReleased;

        /// <summary>The item currently held in the mouth (null = nothing held).
        /// The snapshot must update before Invoke: multiple items may callback in sequence within one
        /// physics step, and the later one must immediately see that the earlier one was eaten.</summary>
        public static ICarriable Held { get; private set; }

        public static void RaiseItemEaten(ICarriable item)
        {
            Held = item;
            ItemEaten?.Invoke(item);
        }

        public static void RaiseItemReleased(ICarriable item, Vector2 pos, Vector2 velocity)
        {
            if (Held == item) Held = null;      // only clear the snapshot when the spit item is the one held
            ItemReleased?.Invoke(item, pos, velocity);
        }

        /// <summary>Clears the snapshot. Used when the player dies and the held item is dropped back into
        /// the world — that path does not go through ItemReleased (it would light a fuse), so the
        /// snapshot must be cleared separately.</summary>
        public static void ClearHeld()
        {
            Held = null;
        }

        // Static fields do not clear on scene reload; with Domain Reload off, dead subscribers from
        // the previous run linger
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            ItemEaten = null;
            ItemReleased = null;
            Held = null;
        }
    }
}
