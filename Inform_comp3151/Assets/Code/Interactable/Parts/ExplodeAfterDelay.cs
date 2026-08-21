using Inkform.Life;
using Inkform.Tool;
using UnityEngine;

namespace Inkform.Interactable.Parts
{
    /// <summary>
    /// Timed explosion trigger: explodes automatically delay seconds after spawn. Use with ExplodePart.
    /// Uses scaled time (Time.time): during hitstop game time freezes and the bomb freezes along —
    /// consistent with the project-wide "gameplay timers use Timer" convention.
    /// </summary>
    public class ExplodeAfterDelay : MonoBehaviour, IInteractablePart, IRestorablePart
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

        // ---- IRestorablePart ----

        /// <summary>
        /// The fuse is one-shot: after firing, `started` stays false forever. Without this a restored
        /// bomb would come back whole and then never tick.
        ///
        /// Remaining time is captured rather than restarted from `delay`, because the snapshot's
        /// contract is "the state at the checkpoint" — a fuse that was nearly out then should be nearly
        /// out again. Timer stores an absolute end time, so it cannot be copied across a restore
        /// directly; Remaining / Set is the conversion.
        /// </summary>
        public IMemento Capture() => new FuseMemento(this, started, timer.Remaining);

        private class FuseMemento : IMemento
        {
            private readonly ExplodeAfterDelay part;
            private readonly bool started;
            private readonly float remaining;

            public FuseMemento(ExplodeAfterDelay part, bool started, float remaining)
            {
                this.part = part;
                this.started = started;
                this.remaining = remaining;
            }

            public void Restore()
            {
                if (part == null) return;

                part.started = started;
                if (remaining > 0f) part.timer.Set(remaining);
                else part.timer.Clear();
            }
        }
    }
}
