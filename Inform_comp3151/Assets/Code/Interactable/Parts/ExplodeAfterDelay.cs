using Inkform.Tool;
using UnityEngine;

namespace Inkform.Interactable.Parts
{
    /// <summary>
    /// Timed explosion trigger: explodes automatically delay seconds after spawn. Use with ExplodePart.
    /// Uses scaled time (Time.time): during hitstop game time freezes and the bomb freezes along —
    /// consistent with the project-wide "gameplay timers use Timer" convention.
    /// </summary>
    public class ExplodeAfterDelay : MonoBehaviour, IInteractablePart
    {
        [Header("Fuse")]
        [Tooltip("Seconds after spawn before exploding")]
        [SerializeField] private float delay = 1.5f;

        private Interactable root;
        private ExplodePart explode;
        private Timer timer;
        private bool started;

        public void Attach(Interactable root) => this.root = root;

        void Start()
        {
            if (!root.TryGetPart(out explode))
                Debug.LogWarning($"{root.name} has ExplodeAfterDelay but no ExplodePart; it will never explode", root);
            timer.Set(delay);
            started = true;
        }

        // Pure timer: does not consume contact
        public bool HandleContact(ContactPhase phase, Collider2D other) => false;

        void Update()
        {
            if (!started || timer.IsRunning) return;
            started = false;
            explode?.Explode();
        }
    }
}
