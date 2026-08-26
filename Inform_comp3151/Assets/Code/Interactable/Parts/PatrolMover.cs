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
        [Tooltip("Forward movement speed, units/second")]
        [InspectorName("Forward Speed")]
        [SerializeField, Min(0f)] private float speed = 2.2f;
        [Tooltip("Return movement speed, units/second. Zero uses Forward Speed.")]
        [SerializeField, Min(0f)] private float returnSpeed;

        [Header("Endpoint Stops")]
        [Tooltip("Seconds to remain at point A before moving toward point B")]
        [SerializeField, Min(0f)] private float stopTimeAtA;
        [Tooltip("Seconds to remain at point B before returning toward point A")]
        [SerializeField, Min(0f)] private float stopTimeAtB;

        [Header("Movement Curves")]
        [Tooltip("Normalized time to normalized distance while moving toward point B")]
        [SerializeField] private AnimationCurve forwardCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        [Tooltip("Normalized time to normalized distance while returning toward point A")]
        [SerializeField] private AnimationCurve returnCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        private Interactable root;
        private Rigidbody2D body;       // when a rigidbody exists, sync its position so players standing on top get carried
        private Vector2 startPos;   // world position at Attach, patrol baseline
        private Vector2 segmentStart;
        private Vector2 target;     // current target endpoint
        private bool goingToB = true;
        private MotionPhase motionPhase;
        private float segmentElapsed;
        private float waitRemaining;

        private const float DistanceEpsilon = 0.0001f;
        private const int MaxTransitionsPerTick = 64;

        private enum MotionPhase
        {
            Moving,
            Waiting,
            Stationary
        }

        public void Attach(Interactable root)
        {
            this.root = root;
            body = root.GetComponent<Rigidbody2D>();
            startPos = root.transform.position;
            segmentStart = startPos;
            goingToB = true;
            target = startPos + pointB;
            segmentElapsed = 0f;
            waitRemaining = 0f;
            motionPhase = MotionPhase.Moving;
        }

        public bool HandleContact(ContactPhase phase, Collider2D other) => false;

        void Update()
        {
            Advance(Time.deltaTime);
        }

        // Kept separate from Update so movement remains deterministic and can be covered by EditMode tests.
        private void Advance(float deltaTime)
        {
            if (root == null || deltaTime <= 0f || motionPhase == MotionPhase.Stationary) return;

            float remaining = deltaTime;
            for (int transitions = 0; transitions < MaxTransitionsPerTick && remaining > 0f; transitions++)
            {
                if (motionPhase == MotionPhase.Waiting)
                {
                    float waitStep = Mathf.Min(remaining, waitRemaining);
                    waitRemaining -= waitStep;
                    remaining -= waitStep;

                    if (waitRemaining > 0f) return;
                    BeginNextSegment();
                    continue;
                }

                float movementSpeed = CurrentSpeed();
                if (movementSpeed <= 0f) return;

                float segmentDistance = Vector2.Distance(segmentStart, target);
                if (segmentDistance <= DistanceEpsilon)
                {
                    SetPosition(target);
                    FinishSegment();
                    continue;
                }

                float duration = segmentDistance / movementSpeed;
                float timeToEndpoint = Mathf.Max(0f, duration - segmentElapsed);
                float movementStep = Mathf.Min(remaining, timeToEndpoint);
                segmentElapsed += movementStep;
                remaining -= movementStep;

                if (segmentElapsed >= duration)
                {
                    SetPosition(target);
                    FinishSegment();
                    continue;
                }

                float normalizedTime = segmentElapsed / duration;
                AnimationCurve curve = goingToB ? forwardCurve : returnCurve;
                float normalizedDistance = curve == null || curve.length == 0
                    ? normalizedTime
                    : curve.Evaluate(normalizedTime);
                SetPosition(Vector2.Lerp(segmentStart, target, Mathf.Clamp01(normalizedDistance)));
                return;
            }
        }

        private float CurrentSpeed()
        {
            if (goingToB) return Mathf.Max(0f, speed);
            return returnSpeed > 0f ? returnSpeed : Mathf.Max(0f, speed);
        }

        private void FinishSegment()
        {
            // A zero-length patrol has no meaningful direction change. The initial leg may still
            // move from the placed position to the shared endpoint before becoming stationary.
            if ((pointA - pointB).sqrMagnitude <= DistanceEpsilon * DistanceEpsilon)
            {
                motionPhase = MotionPhase.Stationary;
                return;
            }

            waitRemaining = Mathf.Max(0f, goingToB ? stopTimeAtB : stopTimeAtA);
            motionPhase = MotionPhase.Waiting;
        }

        private void BeginNextSegment()
        {
            goingToB = !goingToB;
            segmentStart = target;
            target = startPos + (goingToB ? pointB : pointA);
            segmentElapsed = 0f;
            motionPhase = MotionPhase.Moving;

            if (Vector2.Distance(segmentStart, target) <= DistanceEpsilon)
            {
                SetPosition(target);
                motionPhase = MotionPhase.Stationary;
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
