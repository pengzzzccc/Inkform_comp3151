using Inkform.Bus;
using Inkform.Player;
using Inkform.Settings;
using Inkform.Tool;
using UnityEngine;

namespace Inkform.Fx
{
    /// <summary>
    /// Follow camera: smoothly follows the target with a look-ahead offset toward its facing.
    /// Shake and zoom punches are amounts layered on top of the follow result, never written back to the
    /// base position — otherwise they fight the follow and never settle back.
    /// Shake runs on unscaled time, follow on scaled time — so during hitstop the screen "freezes but still shakes".
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CamHandler : MonoBehaviour
    {
        [Header("Follow setting")]
        [SerializeField] private Transform target;                  // drag in the Player, same as InputHandler
        [SerializeField] private Vector2 followOffset = Vector2.zero;
        [SerializeField] private float followSmooth = 0.18f;        // SmoothDamp time for normal follow
        [SerializeField] private float lookAhead = 1.2f;            // look-ahead distance toward facing, 0 = off
        [SerializeField] private float lookAheadSmooth = 0.35f;

        [Header("Shake")]
        [SerializeField] private float traumaDecay = 1.6f;          // trauma decay per second
        [SerializeField] private float maxShakeOffset = 0.6f;       // max offset at trauma = 1
        [SerializeField] private float shakeFrequency = 22f;

        private Camera cam;
        private float baseZ;
        private float baseOrthoSize;

        private Vector2 followVel;

        private float trauma;
        private float seedX, seedY;

        private float zoomAmount, zoomDuration;
        private UnscaledTimer zoomTimer;

        private float lookAheadNow, lookAheadVel;

        void Awake()
        {
            cam = GetComponent<Camera>();
            baseZ = transform.position.z;
            baseOrthoSize = cam.orthographicSize;

            // Different noise seeds per axis, or x/y would be perfectly in phase and shake in a line
            seedX = Random.value * 1000f;
            seedY = Random.value * 1000f;
        }

        void OnEnable()
        {
            FxBus.ShakeRequested += OnShake;
            FxBus.ZoomRequested += OnZoom;
            FxBus.SnapRequested += SnapToTarget;
        }

        void OnDisable()
        {
            FxBus.ShakeRequested -= OnShake;
            FxBus.ZoomRequested -= OnZoom;
            FxBus.SnapRequested -= SnapToTarget;
        }

        void Start()
        {
            // Snap into place at startup rather than sliding over from (0,0)
            SnapToTarget();
        }

        /// <summary>Snaps onto the target immediately. Shared path for startup and player teleports (respawn).</summary>
        private void SnapToTarget()
        {
            if (target == null) return;

            // All three velocities must zero: SmoothDamp's velocity lives in fields, and without this
            // the residual inertia overshoots the frame after snapping — looks like "cut over then bounced"
            followVel = Vector2.zero;
            lookAheadVel = 0f;
            lookAheadNow = lookAhead * (PlayerBus.Face == FaceDirection.R ? 1f : -1f);

            Vector2 want = (Vector2)target.position + followOffset + new Vector2(lookAheadNow, 0f);
            transform.position = new Vector3(want.x, want.y, baseZ);
        }

        // LateUpdate: the player has finished moving in Update, so following this frame avoids a one-frame lag jitter
        void LateUpdate()
        {
            Vector2 basePos = FollowStep();
            Vector2 shakeOffset = ShakeStep();
            ZoomStep();

            // z must stay constant, otherwise 2D render sorting breaks
            transform.position = new Vector3(basePos.x + shakeOffset.x, basePos.y + shakeOffset.y, baseZ);
        }

        private Vector2 FollowStep()
        {
            if (target == null) return transform.position;

            // Look-ahead toward facing: read the bus snapshot directly, no PlayerHandler reference needed
            float wantAhead = lookAhead * (PlayerBus.Face == FaceDirection.R ? 1f : -1f);
            lookAheadNow = Mathf.SmoothDamp(lookAheadNow, wantAhead, ref lookAheadVel, lookAheadSmooth);

            Vector2 want = (Vector2)target.position + followOffset + new Vector2(lookAheadNow, 0f);
            return Vector2.SmoothDamp(transform.position, want, ref followVel, followSmooth);
        }

        private Vector2 ShakeStep()
        {
            trauma = Mathf.Max(0f, trauma - traumaDecay * Time.unscaledDeltaTime);
            if (trauma <= 0f) return Vector2.zero;

            // Squared: small trauma is barely felt, large is strong — more layered than linear
            float shake = trauma * trauma;
            float t = Time.unscaledTime * shakeFrequency;

            // Perlin rather than pure random: adjacent frames are continuous, shaking rather than twitching
            return new Vector2(
                Mathf.PerlinNoise(seedX, t) * 2f - 1f,
                Mathf.PerlinNoise(seedY, t) * 2f - 1f) * (maxShakeOffset * shake);
        }

        private void ZoomStep()
        {
            if (zoomDuration <= 0f || !zoomTimer.IsRunning)
            {
                cam.orthographicSize = baseOrthoSize;
                return;
            }

            cam.orthographicSize = baseOrthoSize + zoomAmount * (zoomTimer.Remaining / zoomDuration);
        }

        // Accumulate rather than overwrite: chain explosions hit harder instead of restarting each time.
        // Scaled by the user's FX intensity at request time (read-style like AudioManager's volume):
        // intensity 0 = the camera never shakes; a later change affects new requests, not current trauma.
        private void OnShake(float amount)
        {
            trauma = Mathf.Clamp01(trauma + amount * SettingsStore.FxIntensity);
        }

        private void OnZoom(float amount, float duration)
        {
            if (duration <= 0f) return;

            zoomAmount = amount * SettingsStore.FxIntensity;
            zoomDuration = duration;
            zoomTimer.Set(duration);
        }
    }
}
