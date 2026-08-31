using Inkform.Bus;
using Inkform.Item;
using Inkform.Player;
using Inkform.Tool;
using System.Collections.Generic;
using UnityEngine;

namespace Inkform.Interactable.Parts
{
    /// <summary>
    /// Item-lifecycle hook: fired by CarriablePart.Release the moment the item is spat back into
    /// the world. Parts whose behavior changes when thrown (ExplodePart arms world-contact
    /// detonation) implement this; CarriablePart never learns what the parts do with it.
    /// </summary>
    public interface IOnSpit
    {
        void OnSpit();
    }

    /// <summary>
    /// Carriable part: an object that can be swallowed after the rope gun hits it, carried in the
    /// backpack and spit out. The common behavior of the former Bomb's item lifecycle, extracted
    /// (minus the bomb-specific fuse) — "stored then put back
    /// into the world" is the same lifecycle for any carriable; detonating and the like are the
    /// concrete object's own business.
    ///
    /// The player side only knows the ICarriable interface: PlayerInventory / ItemBus / RopeGun do not
    /// know this class. Requires: Rigidbody2D on the Interactable root object (simulated is disabled
    /// while hidden). The part itself may sit on the root or a child and is resolved through the node.
    /// </summary>
    public class CarriablePart : MonoBehaviour, ICarriable
    {
        [Header("Carriable")]
        [Tooltip("Whether it can be swallowed (after the rope gun reels it in)")]
        [SerializeField] private bool eatAble = true;
        [Tooltip("Brief immunity to player contact after being spit out, so it is not detonated right at the muzzle; <= 0 disables")]
        [SerializeField] private float spitArmTime = 0.3f;
        [Tooltip("Stable inventory identity. Without one the item remains in the world and cannot be swallowed.")]
        [SerializeField] private InventoryItemDefinition definition;

        private Interactable root;
        private Rigidbody2D body;
        private Collider2D hitBox;
        private readonly List<Renderer> renderers = new List<Renderer>();
        private bool held;
        private Timer spitTimer;    // spit-immunity timer: short-circuits only player contact

        // The size this object is meant to be, read once before anything can have touched it. Re-asserted
        // every time the item goes back into the world — see the SetParent notes below for what used to
        // happen to it.
        private Vector3 homeScale;

        public void Attach(Interactable root)
        {
            this.root = root;
            body = root.GetComponent<Rigidbody2D>();
            if (body == null)
                Debug.LogWarning($"{root.name} has CarriablePart but no Rigidbody2D; it cannot be swallowed", root);

            hitBox = root.GetComponent<Collider2D>();
            homeScale = root.transform.localScale;
            renderers.Clear();
            renderers.AddRange(root.GetComponentsInChildren<Renderer>(true));
        }

        public InventoryItemDefinition Definition => definition;

        // While being pulled (ropeGrappled), contact with the player swallows instead of letting
        // explosion-type parts detonate (same priority as the former Bomb). A failed swallow (mouth
        // full, etc.) does NOT short-circuit — it falls through to later parts; in the former Bomb a
        // full mouth also detonates on contact while being pulled
        public bool HandleContact(ContactPhase phase, Collider2D other)
        {
            if (phase == ContactPhase.Exit) return false;

            // Spit immunity: swallow player contact so it is not detonated right after being spit out
            // (same as the former Bomb's ArmTimer). Only blocks the player — impact detonation vs ground/walls
            // keeps working
            if (spitTimer.IsRunning && other.CompareTag(Tags.Player)) return true;

            if (!ropeGrappled) return false;
            if (!other.CompareTag(Tags.Player)) return false;
            return TrySwallowByRope(other.transform);
        }

        // ---- ICarriable ----

        public bool TrySwallowByRope(Transform player)
        {
            if (held) return false;
            if (!eatAble) return false;
            if (body == null || hitBox == null) return false;
            if (definition == null)
            {
                Debug.LogWarning($"{name} has no InventoryItemDefinition and cannot be stored", root);
                return false;
            }
            PlayerInventory inventory = player.GetComponentInParent<PlayerInventory>();
            if (inventory == null) return false;
            if (!inventory.TryStore(definition)) return false;

            Swallow();
            return true;
        }

        private void Swallow()
        {
            held = true;
            ropeGrappled = false;
            body.simulated = false;
            hitBox.enabled = false;
            SetVisible(false);

            // Being swallowed cuts all ropes: a hanging carriable becomes a free object once spit out
            // (same semantics as the former Bomb)
            if (root.TryGetPart(out HangingChain hanging)) hanging.CutAllChains();

            ItemBus.RaiseItemStored(definition);

            // Inventory stores data, never a scene object. Destroying the origin also ensures a
            // checkpoint memento cannot restore a duplicate while the item remains in the backpack.
            Destroy(root.gameObject);
        }

        // Spit out: back into the world with an initial velocity.
        // No fuse is lit — the fuse is the bomb's own behavior; "what happens after being spit out"
        // is decided by the concrete object itself
        public void Release(Vector2 pos, Vector2 velocity)
        {
            held = false;
            ReturnToWorld();
            hitBox.enabled = true;
            body.simulated = true;

            // The project sets m_AutoSyncTransforms = 0: transform and rigidbody positions do not sync,
            // write both
            transform.position = pos;
            body.position = pos;
            body.linearVelocity = velocity;
            body.angularVelocity = 0f;

            // Spit protection: right after being spit out the velocity is high and the player is often
            // still touching — this window blocks any explosion trigger from player contact
            if (spitArmTime > 0f) spitTimer.Set(spitArmTime);

            // Tell the parts the item is now "thrown": an armed explosive detonates on world contact
            // from here on (the player-contact immunity window above is what keeps it from going off
            // at the muzzle instead)
            foreach (IOnSpit onSpit in root.GetComponentsInChildren<IOnSpit>(true)) onSpit.OnSpit();
        }

        // Pull-mark: after the rope gun hits, the player is reeled in; during that contact with the
        // player must swallow rather than detonate (same as the former Bomb)
        private bool ropeGrappled;

        public void MarkRopeGrappled() => ropeGrappled = true;
        public void ClearRopeGrappled() => ropeGrappled = false;

        /// <summary>
        /// Leaves the mouth and goes back into the world: unparent, become visible, and be the size it
        /// is supposed to be. Shared by the spit and the death-drop so the two cannot drift apart.
        ///
        /// **worldPositionStays: false is the whole point.** Swallow parents with false (keep the local
        /// transform); the plain SetParent(null) that used to be here is the `true` overload, which makes
        /// Unity rewrite localScale to preserve *world* scale. The Player root is scaled 1.2, so every
        /// swallow/spit cycle multiplied the item by 1.2 and it visibly grew — AllinoneBomb went
        /// 0.8 -> 0.96 -> 1.152. Matching the two calls is the fix; the explicit scale write after it is
        /// the belt-and-braces invariant, and also repairs an item that grew before this fix.
        /// </summary>
        private void ReturnToWorld()
        {
            transform.SetParent(null, false);
            transform.localScale = homeScale;
            SetVisible(true);
        }

        // No dedup cache here any more. It used to keep a private `_visible` flag and skip the write
        // when it "already" matched — but RestorablePart.SetGone drives these same renderers without
        // going through this method, so after a HideForRestore the cache read visible while the
        // renderers were off, and the next SetVisible(true) would no-op and leave the item permanently
        // invisible. Two owners of one field can only stay in sync if neither caches. The three call
        // sites are swallow / spit / drop, none of them per-frame, so there is nothing to save.
        private void SetVisible(bool visible)
        {
            foreach (Renderer r in renderers) r.enabled = visible;
        }
    }
}
