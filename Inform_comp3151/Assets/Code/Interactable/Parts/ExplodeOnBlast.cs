using Inkform.Bus;
using Inkform.Tool;
using UnityEngine;

namespace Inkform.Interactable.Parts
{
    /// <summary>
    /// Chain-explosion trigger: after being caught in another explosion, detonates on a delayed fuse.
    /// Use with ExplodePart. Claims itself via the victim of HazardBus.Exploded — the publisher never
    /// needs to know this object, so a row of explosives blows in sequence instead of all in one frame.
    /// </summary>
    public class ExplodeOnBlast : MonoBehaviour, IInteractablePart
    {
        [Header("Chain")]
        [Tooltip("Delay before detonating after being caught in an explosion (0 = same-frame chain)")]
        [SerializeField] private float chainDelay = 0.1f;

        private Interactable root;
        private ExplodePart explode;
        private Timer timer;
        private bool pending;       // caught in a blast, fuse running

        public void Attach(Interactable root) => this.root = root;

        void OnEnable() { HazardBus.Exploded += OnChainExploded; }
        void OnDisable() { HazardBus.Exploded -= OnChainExploded; }

        void Start()
        {
            if (!root.TryGetPart(out explode))
                Debug.LogWarning($"{root.name} has ExplodeOnBlast but no ExplodePart; chaining will not work", root);
        }

        // Pure listener: does not consume contact, so ExplodeOnContact and the like still receive it
        public bool HandleContact(ContactPhase phase, Collider2D other) => false;

        // Called by HazardBus when another explosion affects this object: the victim claims itself
        private void OnChainExploded(GameObject victim, Vector2 center, float force)
        {
            if (victim != root.gameObject) return;
            pending = true;
            timer.Set(chainDelay);
        }

        void Update()
        {
            if (!pending || timer.IsRunning) return;
            pending = false;
            explode?.Explode();
        }
    }
}
