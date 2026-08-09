using UnityEngine;

namespace Inkform.Interactable.Parts
{
    /// <summary>
    /// Patrol translation: moves back and forth between "initial position + pointA" and "initial
    /// position + pointB". Both endpoints are local offsets based on the world position captured at
    /// Attach — the object can be placed anywhere and the patrol starts from there; moving the whole
    /// group needs no endpoint re-tuning. Combine with Spinner for a "rotating gear that strafes".
    /// Pure driver: does not consume contact.
    /// </summary>
    public class PatrolMover : MonoBehaviour, IInteractablePart
    {
        [Header("Patrol")]
        [Tooltip("Left endpoint (local offset relative to the initial position)")]
        [SerializeField] private Vector2 pointA = new Vector2(-2.5f, 0f);
        [Tooltip("Right endpoint (local offset relative to the initial position)")]
        [SerializeField] private Vector2 pointB = new Vector2(2.5f, 0f);
        [Tooltip("Movement speed, units/second")]
        [SerializeField] private float speed = 2.2f;

        private Interactable root;
        private Rigidbody2D body;       // when a rigidbody exists, sync its position so players standing on top get carried
        private Vector2 startPos;   // world position at Attach, patrol baseline
        private Vector2 target;     // current target endpoint
        private bool goingToB = true;

        public void Attach(Interactable root)
        {
            this.root = root;
            body = root.GetComponent<Rigidbody2D>();
            startPos = root.transform.position;
            target = startPos + pointB;
        }

        public bool HandleContact(ContactPhase phase, Collider2D other) => false;

        void Update()
        {
            Vector2 from = root.transform.position;
            Vector2 to = target - from;
            float dist = to.magnitude;
            float step = speed * Time.deltaTime;
            if (step <= 0f) return;

            if (dist <= step)
            {
                // Arrived at the endpoint: land exactly on it (avoids per-frame cumulative error),
                // turn around
                SetPosition(target);
                goingToB = !goingToB;
                target = startPos + (goingToB ? pointB : pointA);
            }
            else
            {
                SetPosition(from + to / dist * step);
            }
        }

        // The project sets m_AutoSyncTransforms = 0: transform and rigidbody positions do not sync,
        // write both — without the rigidbody sync the physics collider stays behind and players
        // standing on top get left behind (no push, no follow to speak of)
        private void SetPosition(Vector2 next)
        {
            root.transform.position = next;
            if (body != null) body.position = next;
        }

        // In the editor Attach never ran; draw the hint from the current transform.position
        void OnDrawGizmosSelected()
        {
            Vector3 basePos = transform.position;
            Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.9f);
            Gizmos.DrawLine(basePos + (Vector3)pointA, basePos + (Vector3)pointB);
            Gizmos.DrawWireSphere(basePos + (Vector3)pointA, 0.15f);
            Gizmos.DrawWireSphere(basePos + (Vector3)pointB, 0.15f);
        }
    }
}
