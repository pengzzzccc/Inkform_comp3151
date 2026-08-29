using UnityEngine;
using Inkform.Item;

namespace Inkform.Interactable
{
    /// <summary>
    /// Carriable item part capability: player systems deal only with this interface and resolve it
    /// through Interactable.TryGetPart — never by probing concrete components directly.
    ///
    /// The implementation may sit on the Interactable root or a child: the node collects every
    /// IInteractablePart in its hierarchy and is the single capability lookup entry.
    ///
    /// Release is a direct command on the freshly instantiated world object. ItemBus only announces
    /// the completed fact to feedback systems.
    /// </summary>
    public interface ICarriable : IInteractablePart
    {
        InventoryItemDefinition Definition { get; }

        /// <summary>Pull-target position (MonoBehaviour built-in).</summary>
        Transform transform { get; }

        /// <summary>Swallow after the rope gun reels in. Returns true on success (false = mouth full /
        /// not eatable — the rope gun releases).</summary>
        bool TrySwallowByRope(Transform player);

        /// <summary>Places a freshly instantiated inventory item back into the world.</summary>
        void Release(Vector2 pos, Vector2 velocity);

        /// <summary>Being pulled by the rope gun (explosives use it to prevent accidental contact detonation).</summary>
        void MarkRopeGrappled();

        /// <summary>Pulling ended.</summary>
        void ClearRopeGrappled();

    }
}
