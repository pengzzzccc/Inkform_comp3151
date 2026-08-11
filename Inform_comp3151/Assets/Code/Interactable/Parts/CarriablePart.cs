using Inkform.Bus;
using Inkform.Tool;
using System.Collections.Generic;
using UnityEngine;

namespace Inkform.Interactable.Parts
{
    /// <summary>
    /// Carriable part: an object that can be swallowed after the rope gun hits it, carried in the
    /// mouth, spit out, and dropped on death. The common behavior of Bomb.Swallow / DropAt /
    /// OnItemReleased, extracted (minus the bomb-specific fuse) — "swallowed then put back into the
    /// world" is the same lifecycle for any carriable; detonating and the like are the concrete
    /// object's own business.
    ///
    /// The player side only knows the ICarriable interface: ItemCarrier / ItemBus / RopeGun do not
    /// know this class. Requires: Rigidbody2D on the Interactable root object (simulated is disabled
    /// while hidden). Note: on a child object RopeGun's GetComponent probe would not find it — must
    /// sit on the root object.
    /// </summary>
    public class CarriablePart : MonoBehaviour, IInteractablePart, ICarriable
    {
        [Header("Carriable")]
        [Tooltip("Whether it can be swallowed (after the rope gun reels it in)")]
        [SerializeField] private bool eatAble = true;
        [Tooltip("Brief immunity to player contact after being spit out, so it is not detonated right at the muzzle; <= 0 disables")]
        [SerializeField] private float spitArmTime = 0.3f;

        private Interactable root;
        private Rigidbody2D body;
        private Collider2D hitBox;
        private readonly List<Renderer> renderers = new List<Renderer>();
        private bool held;
        private Timer spitTimer;    // spit-immunity timer: short-circuits only player contact

        public void Attach(Interactable root)
        {
            this.root = root;
            body = root.GetComponent<Rigidbody2D>();
            if (body == null)
                Debug.LogWarning($"{root.name} has CarriablePart but no Rigidbody2D; it cannot be swallowed", root);

            hitBox = root.GetComponent<Collider2D>();
            renderers.Clear();
            renderers.AddRange(root.GetComponentsInChildren<Renderer>(true));
        }

        void OnEnable() { ItemBus.ItemReleased += OnItemReleased; }
        void OnDisable() { ItemBus.ItemReleased -= OnItemReleased; }

        // While being pulled (ropeGrappled), contact with the player swallows instead of letting
        // explosion-type parts detonate (same priority as Bomb). A failed swallow (mouth full, etc.)
        // does NOT short-circuit — it falls through to later parts; in the original Bomb a full mouth
        // also detonates on contact while being pulled
        public bool HandleContact(ContactPhase phase, Collider2D other)
        {
            if (phase == ContactPhase.Exit) return false;

            // Spit immunity: swallow player contact so it is not detonated right after being spit out
            // (same as Bomb's ArmTimer). Only blocks the player — impact detonation vs ground/walls
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
            if (ItemBus.Held != null) return false;     // something is already in the mouth

            Swallow(player);
            return true;
        }

        // Swallow: does not destroy, just disables physics + hides, parents to the player, waiting to
        // be spit out. Note: must NOT SetActive(false) — OnDisable unsubscribes the bus and the
        // "released" event would never arrive
        private void Swallow(Transform player)
        {
            held = true;
            ropeGrappled = false;
            body.simulated = false;
            hitBox.enabled = false;
            SetVisible(false);
            transform.SetParent(player, false);
            transform.localPosition = Vector3.zero;

            // Being swallowed cuts all ropes: a hanging carriable becomes a free object once spit out
            // (same semantics as Bomb)
            if (root.TryGetPart(out HangingChain hanging)) hanging.CutAllChains();

            ItemBus.RaiseItemEaten(this);
        }

        /// <summary>Dropped back into the world on the player's death: put down in place, no fuse, no initial velocity.</summary>
        public void DropAt(Vector2 pos)
        {
            held = false;
            transform.SetParent(null);
            SetVisible(true);
            hitBox.enabled = true;
            body.simulated = true;

            // The project sets m_AutoSyncTransforms = 0: transform and rigidbody positions do not sync,
            // write both
            transform.position = pos;
            body.position = pos;
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
        }

        // Spit out: back into the world with an initial velocity.
        // No fuse is lit — the fuse is the bomb's own behavior; "what happens after being spit out"
        // is decided by the concrete object itself
        private void OnItemReleased(ICarriable item, Vector2 pos, Vector2 velocity)
        {
            if (item != (ICarriable)this) return;       // not the one being spit

            held = false;
            transform.SetParent(null);
            SetVisible(true);
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
        }

        // Pull-mark: after the rope gun hits, the player is reeled in; during that contact with the
        // player must swallow rather than detonate (same as Bomb)
        private bool ropeGrappled;

        public void MarkRopeGrappled() => ropeGrappled = true;
        public void ClearRopeGrappled() => ropeGrappled = false;

        // Self-deduplicating: same show/hide logic as Bomb's SetVisible (per-frame driving never rewrites)
        private bool _visible = true;

        private void SetVisible(bool visible)
        {
            if (_visible == visible) return;
            _visible = visible;
            foreach (Renderer r in renderers) r.enabled = visible;
        }
    }
}
