using Inkform.Audio;
using UnityEngine;

namespace Inkform.Item
{
    /// <summary>
    /// Crusher head driver: a vertical two-point ping-pong with shaped legs. Same machinery as
    /// PatrolMover — an AnimationCurve maps normalized time to normalized distance so each leg can
    /// ease in/out, and a Moving/Waiting phase machine holds the head at each endpoint for its stop
    /// time — but specialized to the vertical route and self-contained: it moves Target directly
    /// (default: its own transform), no Interactable root required. The component is meant to sit
    /// on the fixed holder node while Target points at the moving head.
    ///
    /// Cycle: hover at the top → slam down → linger at the bottom → rise back, forever. The placed
    /// position acts as the top for the opening leg (same as PatrolMover's first-leg semantics);
    /// every later rise returns exactly to start + Top Offset. Each leg's start plays its SoundCue
    /// (fall cue on the slam, rise cue on the return) through the shared AudioManager pool.
    /// </summary>
    public class CrusherMover : MonoBehaviour
    {
        [Header("Target")]
        [Tooltip("The node that actually moves. Empty = this transform. Point it at the crusher head while keeping the component on the fixed holder")]
        [SerializeField] private Transform target;

        [Header("Route (local offsets relative to the start position captured at spawn)")]
        [Tooltip("Head's rest offset — it hovers here before each slam")]
        [SerializeField] private Vector2 topOffset = Vector2.zero;
        [Tooltip("Head's lowest offset — the bottom of the slam")]
        [SerializeField] private Vector2 bottomOffset = new Vector2(0f, -2.5f);

        [Header("Speed")]
        [Tooltip("Rise speed, units/second. Zero holds the head at the bottom")]
        [SerializeField, Min(0f)] private float riseSpeed = 1.5f;
        [Tooltip("Fall speed, units/second. Zero holds the head at the top")]
        [SerializeField, Min(0f)] private float fallSpeed = 4f;

        [Header("Movement Curves")]
        [Tooltip("Normalized time to normalized distance while rising")]
        [SerializeField] private AnimationCurve riseCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        [Tooltip("Normalized time to normalized distance while falling — ease it in for a gravity slam")]
        [SerializeField] private AnimationCurve fallCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [Header("Endpoint Stops")]
        [Tooltip("Seconds to hover at the top before the slam")]
        [SerializeField, Min(0f)] private float stopTimeAtTop = 1f;
        [Tooltip("Seconds to linger at the bottom before rising back")]
        [SerializeField, Min(0f)] private float stopTimeAtBottom = 0.3f;

        [Header("Audio")]
        [Tooltip("Played when a rise leg starts")]
        [SerializeField] private SoundCue riseCue;
        [Tooltip("Played when a fall leg starts")]
        [SerializeField] private SoundCue fallCue;

        private Rigidbody2D body;       // when the target has a rigidbody, sync its position too
        private Vector2 startPos;       // world position of the target captured at spawn, route baseline
        private Vector2 topPos;
        private Vector2 bottomPos;
        private MotionPhase phase;
        private float segmentElapsed;   // seconds already spent on the current leg
        private float waitRemaining;
        private bool falling;           // direction of the leg about to run / currently running

        private const float DistanceEpsilon = 0.0001f;
        private const int MaxTransitionsPerTick = 64;

        private enum MotionPhase
        {
            Moving,
            Waiting
        }

        void Start()
        {
            if (target == null) target = transform;
            body = target.GetComponent<Rigidbody2D>();

            startPos = target.position;
            topPos = startPos + topOffset;
            bottomPos = startPos + bottomOffset;

            // A fully collapsed route has nowhere to go; the head simply stays put
            if ((bottomPos - topPos).sqrMagnitude <= DistanceEpsilon * DistanceEpsilon) return;

            // Spawn hovering at the head's placed position: hold for the top stop, then slam
            falling = true;
            segmentElapsed = 0f;
            waitRemaining = stopTimeAtTop;
            phase = MotionPhase.Waiting;
        }

        void Update()
        {
            Advance(Time.deltaTime);
        }

        // Kept separate from Update like PatrolMover's: deterministic, EditMode-testable, and the
        // time budget loop lets a leg, its endpoint stop and the next leg chain within one frame
        private void Advance(float deltaTime)
        {
            if (target == null || deltaTime <= 0f) return;

            float remaining = deltaTime;
            for (int transitions = 0; transitions < MaxTransitionsPerTick && remaining > 0f; transitions++)
            {
                if (phase == MotionPhase.Waiting)
                {
                    float waitStep = Mathf.Min(remaining, waitRemaining);
                    waitRemaining -= waitStep;
                    remaining -= waitStep;

                    if (waitRemaining > 0f) return;
                    BeginLeg();
                    continue;
                }

                Vector2 from = falling ? topPos : bottomPos;
                Vector2 destination = falling ? bottomPos : topPos;
                float legSpeed = falling ? fallSpeed : riseSpeed;
                if (legSpeed <= 0f) return;     // zero speed: hold until retuned

                float segmentDistance = Vector2.Distance(from, destination);
                if (segmentDistance <= DistanceEpsilon)
                {
                    SetPosition(destination);
                    FinishLeg();
                    continue;
                }

                float duration = segmentDistance / legSpeed;
                float timeToEndpoint = Mathf.Max(0f, duration - segmentElapsed);
                float movementStep = Mathf.Min(remaining, timeToEndpoint);
                segmentElapsed += movementStep;
                remaining -= movementStep;

                if (segmentElapsed >= duration)
                {
                    SetPosition(destination);
                    FinishLeg();
                    continue;
                }

                float normalizedTime = segmentElapsed / duration;
                AnimationCurve curve = falling ? fallCurve : riseCurve;
                float normalizedDistance = curve == null || curve.length == 0
                    ? normalizedTime
                    : curve.Evaluate(normalizedTime);
                SetPosition(Vector2.Lerp(from, destination, Mathf.Clamp01(normalizedDistance)));
                return;
            }
        }

        private void BeginLeg()
        {
            phase = MotionPhase.Moving;
            segmentElapsed = 0f;
            PlayCue(falling ? fallCue : riseCue);
        }

        private void FinishLeg()
        {
            waitRemaining = falling ? stopTimeAtBottom : stopTimeAtTop;
            falling = !falling;     // the stop is spent at the endpoint just reached; flip for the next leg
            phase = MotionPhase.Waiting;
        }

        // Same cue pattern as the death strategies: null-safe, through the shared AudioManager pool
        private void PlayCue(SoundCue cue)
        {
            if (cue != null && AudioManager.Instance != null)
                AudioManager.Instance.Play(cue, target.position);
        }

        // The project sets m_AutoSyncTransforms = 0: write the rigidbody too when present, or its
        // physics collider trails behind the sprite
        private void SetPosition(Vector2 next)
        {
            target.position = next;
            if (body != null) body.position = next;
        }

        // In the editor Start never ran; draw the hint from the target's current position
        void OnDrawGizmosSelected()
        {
            Vector2 basePos = (target != null ? target : transform).position;
            Vector2 top = basePos + topOffset;
            Vector2 bottom = basePos + bottomOffset;

            Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.9f);
            Gizmos.DrawLine(top, bottom);
            Gizmos.DrawWireSphere(top, 0.15f);
            Gizmos.DrawWireSphere(bottom, 0.15f);
        }
    }
}
