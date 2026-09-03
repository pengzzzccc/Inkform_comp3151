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
    /// The object also snaps back home: the first physics frame captures a snapshot of its
    /// transform position (re-captured whenever simulation restarts, e.g. after a RestorablePart
    /// hide/restore). Every FixedUpdate checks the offset from that anchor; once displaced it
    /// slowly drifts back via MovePosition until it settles inside homeRadius, where velocity is
    /// zeroed. The return path respects collisions — a wall can hold it away from home.
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
        [Tooltip("Speed at which the object drifts back toward its home snapshot, per second")]
        [SerializeField] private float returnSpeed = 2f;
        [Tooltip("Distance from the home snapshot below which the object counts as returned; avoids eternal micro-drift")]
        [SerializeField] private float homeRadius = 0.05f;
        [Tooltip("Knockback force applied when caught in an explosion")]
        [SerializeField] private float knockForce = 8f;
        [Tooltip("Push force applied on player contact")]
        [SerializeField] private float pushForce = 4f;

        private Interactable root;
        private Rigidbody2D body;
        private Vector2 homePos;        // home snapshot: transform position captured at first physics frame
        private bool wasSimulated;      // detects simulation restart (hide/restore) to re-capture the snapshot

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
        // so detonation parts (ExplodePart) still receive it
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

        // Snap-back home: capture the transform position at the first physics frame (and re-capture
        // whenever simulation restarts, e.g. a RestorablePart hide/restore) — the drift target. Every
        // step checks the offset; once displaced the object slowly returns via MovePosition, with the
        // same resistance deceleration, until it settles inside homeRadius where velocity is zeroed.
        // Skipped while hidden (swallowed / restorable-gone): simulated is off then anyway
        void FixedUpdate()
        {
            if (body == null) return;

            if (!body.simulated)
            {
                wasSimulated = false;   // forget the snapshot while hidden; re-capture on the next wake
                return;
            }

            if (!wasSimulated)
            {
                wasSimulated = true;
                homePos = transform.position;
            }

            Vector2 toHome = homePos - (Vector2)transform.position;
            if (toHome.sqrMagnitude <= homeRadius * homeRadius)
            {
                body.linearVelocity = Vector2.zero;     // back home: fully stopped, no eternal crawl
                return;
            }

            body.linearVelocity = Vector2.Lerp(body.linearVelocity, Vector2.zero, resistance * Time.fixedDeltaTime);

            Vector2 step = toHome.normalized * (returnSpeed * Time.fixedDeltaTime);
            body.MovePosition((Vector2)transform.position + (toHome.sqrMagnitude <= step.sqrMagnitude ? toHome : step));
        }

        // In the editor Attach never ran; draw the aid circle from the current transform.position
        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.7f);
            Gizmos.DrawWireSphere(transform.position, maxRadius);
        }
    }
}
