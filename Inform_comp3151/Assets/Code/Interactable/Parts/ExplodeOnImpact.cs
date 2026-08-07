using UnityEngine;

namespace Inkform.Interactable.Parts
{
    /// <summary>
    /// Impact-detonation trigger: once velocity reaches the threshold, contact with ANY object
    /// detonates. Use with ExplodePart — the framework version of speed detonation
    /// (Bomb.CheckSpeedExplode): no target-tag restriction, hitting a wall/ground/player all count,
    /// and it ignores any immunity window.
    /// While swallowed the rigidbody simulation and collider are off → no contact callbacks, so it
    /// cannot trigger — no defensive check needed.
    /// </summary>
    public class ExplodeOnImpact : MonoBehaviour, IInteractablePart
    {
        [Header("Impact")]
        [Tooltip("Once velocity reaches this threshold, contact with any object detonates; <= 0 disables")]
        [SerializeField] private float threshold = 0f;

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
            if (body.linearVelocity.magnitude < threshold) return false;

            explode.Explode();
            return true;    // handled: short-circuit later parts
        }
    }
}
