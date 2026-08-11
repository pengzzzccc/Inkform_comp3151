using System.Collections.Generic;
using UnityEngine;

namespace Inkform.Interactable
{
    /// <summary>
    /// Interactable node (the core of the component-based interaction framework): every object that
    /// interacts with the player (hazards / plain surfaces / bombs...) carries this component and
    /// composes behavior components (parts) as needed.
    ///
    /// Division of labor:
    /// ① The node does two things — centrally receives physics callbacks (OnTrigger*/OnCollision*) and
    ///    dispatches them in declaration order to parts (short-circuiting on "handled"); and exposes
    ///    part lookup (TryGetPart) as the single entry for players and systems, which never know any
    ///    concrete behavior;
    /// ② parts attach to this object or its children (collected via GetComponentsInChildren, active
    ///    on attach, no manual Inspector wiring); concrete behavior (touch-death / explosion /
    ///    carriable / grabbable...) is implemented by each part — "the player needs only interface
    ///    storage; concrete logic lives in the concrete object".
    ///
    /// Trigger and physical collision dispatch unified: static objects (spikes/lasers, trigger
    /// colliders, no rigidbody) go through OnTrigger*; physical objects (bombs/mines, non-trigger +
    /// Rigidbody2D) go through OnCollision* — parts never need to distinguish the source; both funnel
    /// into HandleContact(phase, other). Collision callbacks require a Rigidbody2D on this object.
    ///
    /// Layer suggestion: hazard-class objects belong on Hazard(13) — the project sets
    /// Physics2D.QueriesHitTriggers = 1, so on Terrain/Breakable the player's four-way OverlapCircle
    /// would treat spikes as standable ground.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class Interactable : MonoBehaviour
    {
        private readonly List<IInteractablePart> parts = new List<IInteractablePart>();

        /// <summary>Part lookup: unified entry for the player/system side (e.g. ItemCarrier looks up the
        /// carriable part, RopeGun the grabbable part). Returns false when absent — the caller degrades
        /// to "no such behavior".</summary>
        public bool TryGetPart<T>(out T part) where T : class, IInteractablePart
        {
            foreach (IInteractablePart p in parts)
            {
                if (p is T t)
                {
                    part = t;
                    return true;
                }
            }
            part = null;
            return false;
        }

        void Awake()
        {
            // Parts must be collected before any physics callback: callbacks can only arrive after Awake
            parts.Clear();
            foreach (IInteractablePart p in GetComponentsInChildren<IInteractablePart>(true))
            {
                parts.Add(p);
                p.Attach(this);     // mount callback: this node's Awake already ran, references can be cached now
            }
        }

        // ---- Unified physics callback entry: dispatch to all parts, short-circuit on "handled" ----
        // Triggers and collisions share the same dispatch: static objects (no rigidbody) only receive
        // OnTrigger*, physical objects (with Rigidbody2D) receive OnCollision* — parts need no source distinction

        void OnTriggerEnter2D(Collider2D other) => Dispatch(ContactPhase.Enter, other);
        void OnTriggerStay2D(Collider2D other) => Dispatch(ContactPhase.Stay, other);
        void OnTriggerExit2D(Collider2D other) => Dispatch(ContactPhase.Exit, other);

        void OnCollisionEnter2D(Collision2D collision) => Dispatch(ContactPhase.Enter, collision.collider);
        void OnCollisionStay2D(Collision2D collision) => Dispatch(ContactPhase.Stay, collision.collider);
        void OnCollisionExit2D(Collision2D collision) => Dispatch(ContactPhase.Exit, collision.collider);

        private void Dispatch(ContactPhase phase, Collider2D other)
        {
            foreach (IInteractablePart p in parts)
            {
                if (p.HandleContact(phase, other)) return;
            }
        }

        // Draws the node's bounds + an overview of attached parts in the Scene view, so a level designer
        // can see the composition at a glance
        void OnDrawGizmosSelected()
        {
            Collider2D c = GetComponent<Collider2D>();
            if (c == null) return;

            Gizmos.color = new Color(0.9f, 0.3f, 0.9f, 0.5f);
            Gizmos.DrawWireCube(c.bounds.center, c.bounds.size);

            // A yellow dot per behavior component: which behavior layers this object carries, at a glance
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.9f);
            foreach (IInteractablePart p in GetComponentsInChildren<IInteractablePart>(true))
            {
                Transform t = (p as MonoBehaviour)?.transform;
                if (t == null) continue;
                Gizmos.DrawWireSphere(t.position, 0.12f);
            }
        }
    }
}
