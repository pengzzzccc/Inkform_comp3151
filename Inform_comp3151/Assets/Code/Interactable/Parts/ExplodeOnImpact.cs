using UnityEngine;

namespace Inkform.Interactable.Parts
{
    /// <summary>
    /// Impact-detonation trigger: contact detonates once velocity reaches the threshold AND the thing
    /// hit is on an impactMask layer. Use with ExplodePart — the framework version of speed detonation
    /// (the former Bomb's CheckSpeedExplode), with the mask added because speed alone is not a usable criterion:
    /// the fastest thing in the game is a spat item (ItemCarrier.spitSpeed), so a bomb leaving the
    /// mouth clears any sane threshold and would detonate on the first floor it touches. The mask is
    /// what keeps terrain out of it; it is not a tag filter, and it does not consult any immunity window.
    /// While swallowed the rigidbody simulation and collider are off → no contact callbacks, so it
    /// cannot trigger — no defensive check needed.
    /// </summary>
    public class ExplodeOnImpact : MonoBehaviour, IInteractablePart
    {
        [Header("Impact")]
        [Tooltip("Once velocity reaches this threshold, contact with an impactMask object detonates; <= 0 disables")]
        [SerializeField] private float threshold = 0f;
        [Tooltip("Which layers count as an impact. Speed alone is not enough — contact with anything " +
                 "outside this mask never detonates, however fast. Terrain is deliberately out by " +
                 "default: a spat bomb leaves the mouth at spitSpeed and would otherwise blow up on " +
                 "the first floor it touches")]
        [SerializeField] private LayerMask impactMask = (1 << 0) | (1 << 11) | (1 << 13);   // Default | Breakable | Hazard

        private Interactable root;
        private ExplodePart explode;
        private Rigidbody2D body;

        public void Attach(Interactable root)
        {
            this.root = root;
            body = root.GetComponent<Rigidbody2D>();
            if (body == null)
                Debug.LogWarning($"{root.name} has ExplodeOnImpact but no Rigidbody2D; speed checks will not work", root);
        }

        // Dependency resolution in Start: Interactable.Awake collects and Attach-es parts on the fly,
        // so TryGetPart here might not reach the core yet; Start runs after all Awakes, guaranteeing
        // the explosion core is already in the list
        void Start()
        {
            if (!root.TryGetPart(out explode))
                Debug.LogWarning($"{root.name} has ExplodeOnImpact but no ExplodePart; impacts will not detonate", root);
        }

        public bool HandleContact(ContactPhase phase, Collider2D other)
        {
            if (phase == ContactPhase.Exit) return false;
            if (threshold <= 0f || explode == null) return false;
            // Layer gate ahead of the speed test: speed is not sufficient on its own, and this is the
            // cheaper check of the two (magnitude costs a square root)
            if ((impactMask.value & (1 << other.gameObject.layer)) == 0 ) return false;
            if (body.linearVelocity.magnitude < threshold) return false;

            explode.Explode();
            return true;    // handled: short-circuit later parts
        }
    }
}
