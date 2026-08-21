using Inkform.Tool;
using UnityEngine;

namespace Inkform.Interactable.Parts
{
    /// <summary>
    /// Touch-detonation trigger: explodes on contact with the target (player by default). Use with
    /// ExplodePart — the trigger condition and the explosion itself are separated; swapping the trigger
    /// mode (timed / chain) swaps parts without touching the core.
    /// Both trigger colliders and physical colliders can trigger it: the node unifies both into
    /// HandleContact.
    /// </summary>
    public class ExplodeOnContact : MonoBehaviour, IInteractablePart
    {
        [Header("Trigger")]
        [Tooltip("Who detonates it: CompareTag never errors on a wrong string, it just never matches — use the Tags constants")]
        [SerializeField] private string targetTag = Tags.Player;

        private Interactable root;
        private ExplodePart explode;

        public void Attach(Interactable root) => this.root = root;

        // Dependency resolution in Start: Interactable.Awake collects and Attach-es parts on the fly,
        // so TryGetPart here might not reach the core yet; Start runs after all Awakes, guaranteeing
        // the explosion core is already in the list
        void Start()
        {
            if (!root.TryGetPart(out explode))
                Debug.LogWarning($"{root.name} has ExplodeOnContact but no ExplodePart; touch will not detonate", root);
        }

        public bool HandleContact(ContactPhase phase, Collider2D other)
        {
            if (phase == ContactPhase.Exit) return false;
            if (explode == null) return false;
            if (!other.CompareTag(targetTag)) return false;

            explode.Explode();
            return true;    // handled: short-circuit later parts
        }
    }
}
