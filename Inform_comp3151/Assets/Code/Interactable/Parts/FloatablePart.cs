using Inkform.Bus;
using Inkform.Tool;
using UnityEngine;

namespace Inkform.Interactable.Parts
{
    /// <summary>
    /// Floatable behavior: gravity is zero and the object is knocked around by hits — explosions
    /// (HazardBus.Exploded) and player contact both fling it along the 8-way "hit source → self"
    /// direction. A resistance-driven deceleration (linear interpolation of velocity toward zero)
    /// guarantees it always comes to rest — this part is a braking device, not a boundary.
    ///
    /// maxRadius is a designer aid only: OnDrawGizmosSelected draws the circle so the effective
    /// knockback range is visible in the Scene view. Nothing clamps at it; resistance is what
    /// actually stops the object.
    /// </summary>
    public class FloatablePart : MonoBehaviour, IInteractablePart
    {
        [Header("Floatable")]
        [Tooltip("Effective knockback range — designer aid only, drawn as a gizmo circle; nothing clamps at it")]
        [SerializeField] private float maxRadius = 3f;
        [Tooltip("Deceleration strength: linear interpolation of velocity toward zero, per second")]
        [SerializeField] private float resistance = 2f;
        [Tooltip("Speed below which the object counts as stopped; avoids eternal micro-drift")]
        [SerializeField] private float stopSpeed = 0.05f;
        [Tooltip("Knockback force applied when caught in an explosion")]
        [SerializeField] private float knockForce = 8f;
        [Tooltip("Push force applied on player contact")]
        [SerializeField] private float pushForce = 4f;

        private Interactable root;
        private Rigidbody2D body;

        public void Attach(Interactable root)
        {
            this.root = root;
            body = root.GetComponent<Rigidbody2D>();
            if (body == null)
                Debug.LogWarning($"{root.name} has FloatablePart but no Rigidbody2D; it cannot float or be knocked around", root);
            else
                body.gravityScale = 0f;     // floating: no gravity
        }

        // Player push: flung along the 8-way "player → self" direction. Does not consume the contact,
        // so explosion triggers (ExplodeOnContact and the like) still receive it
        public bool HandleContact(ContactPhase phase, Collider2D other)
        {
            if (phase == ContactPhase.Exit) return false;
            if (body == null) return false;
            if (!other.CompareTag(Tags.Player)) return false;

            SetVelocity(Dir8.Snap((Vector2)transform.position - (Vector2)other.transform.position), pushForce);
            return false;
        }

        void OnEnable() { HazardBus.Exploded += OnExploded; }
        void OnDisable() { HazardBus.Exploded -= OnExploded; }

        // Called by HazardBus on explosion: flung along the 8-way "blast center → self" direction —
        // the same hit-direction source as the player's own knockback (PlayerHandler.OnExploded)
        private void OnExploded(GameObject victim, Vector2 center, float force)
        {
            if (root == null || body == null) return;
            if (victim != root.gameObject) return;

            SetVelocity(Dir8.Snap((Vector2)transform.position - center), knockForce);
        }

        private void SetVelocity(Vector2 direction, float force)
        {
            body.linearVelocity = direction * force;
        }

        // Deceleration via linear interpolation: velocity lerps toward zero with a per-second
        // resistance factor, so the object stops instead of drifting forever. Skipped while hidden
        // (swallowed / restorable-gone): simulated is off then anyway
        void FixedUpdate()
        {
            if (body == null || !body.simulated) return;

            Vector2 v = body.linearVelocity;
            if (v.sqrMagnitude <= stopSpeed * stopSpeed)
            {
                body.linearVelocity = Vector2.zero;     // dead zone: fully stopped, no eternal crawl
                return;
            }

            body.linearVelocity = Vector2.Lerp(v, Vector2.zero, resistance * Time.fixedDeltaTime);
        }

        // In the editor Attach never ran; draw the aid circle from the current transform.position
        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.7f);
            Gizmos.DrawWireSphere(transform.position, maxRadius);
        }
    }
}
