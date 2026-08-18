using UnityEngine;

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
    /// Release (spit out) is not here: it goes through the ItemBus.ItemReleased event with
    /// self-claiming — that path also has audio listeners, and events fit "broadcast to everyone"
    /// better than a method call.
    /// </summary>
    public interface ICarriable
    {
        /// <summary>Pull-target position (MonoBehaviour built-in).</summary>
        Transform transform { get; }

        /// <summary>Swallow after the rope gun reels in. Returns true on success (false = mouth full /
        /// not eatable — the rope gun releases).</summary>
        bool TrySwallowByRope(Transform player);

        /// <summary>Being pulled by the rope gun (explosives use it to prevent accidental contact detonation).</summary>
        void MarkRopeGrappled();

        /// <summary>Pulling ended.</summary>
        void ClearRopeGrappled();

        /// <summary>Dropped back into the world when the player dies (no fuse, no initial velocity, dropped in place).</summary>
        void DropAt(Vector2 pos);
    }
}
