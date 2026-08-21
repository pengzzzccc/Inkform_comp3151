using UnityEngine;
using Inkform.Item;

namespace Inkform.Interactable
{
    /// <summary>
    /// Carriable item interface: the player systems (ItemCarrier / ItemBus / RopeGun) **only deal with
    /// this interface** and never know any concrete implementation — "the player needs only interface
    /// storage; concrete logic lives in the concrete object".
    ///
    /// Implementations:
    /// ① standalone classes implement it directly (the former Bomb monolith's method signatures
    ///    already matched, just declared);
    /// ② framework objects attach it to an Interactable, implemented by CarriablePart — note the
    ///    implementation must sit on the Interactable's **root object** (RopeGun probes with
    ///    GetComponent; a child object would not be found).
    ///
    /// Release is a direct command on the freshly instantiated world object. ItemBus only announces
    /// the completed fact to feedback systems.
    /// </summary>
    public interface ICarriable
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
