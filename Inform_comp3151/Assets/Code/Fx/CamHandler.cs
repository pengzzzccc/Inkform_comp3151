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
    /// The follow mode picks the anchor: Player centres on the player, PlayerCursorMidpoint centres on
    /// the middle of the line between player and aim cursor — pointing the reticle somewhere pans the
    /// view halfway toward it while the player never leaves the frame.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CamHandler : MonoBehaviour
    {
        public enum FollowMode { Player, PlayerCursorMidpoint }

        /// <summary>Active follow mode — logged by the performance recorder so runs with different
        /// camera modes stay comparable.</summary>
        public FollowMode Mode => mode;

        [Header("Follow setting")]
        [SerializeField] private Transform target;                  // drag in the Player, same as InputHandler
        [SerializeField] private FollowMode mode = FollowMode.Player;
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
        private Vector2 followBasePosition;

        private float trauma;
        private float seedX, seedY;

        private float zoomAmount, zoomDuration, zoomRemaining;
        private float shakeClock;

        private float lookAheadNow, lookAheadVel;

        // Follow target = the current scene's live player, resolved from the bus at use time so
        // follow/snap survive scene switches; the serialized field only serves as a fallback for
        // scenes without a player (menus) and for older scene data
        private Transform Target => PlayerBus.Player != null ? PlayerBus.Player.transform : target;

        // Same lazy fake-null re-lookup pattern as RespawnDirector.deathCache: after a scene switch
        // the destroyed gun reads null here and the next use re-resolves against the new player
        private RopeGun cursorGunCache;

        private RopeGun CursorGun
        {
            get
            {
                if (cursorGunCache == null)
                {
                    Transform current = Target;
                    cursorGunCache = current != null ? current.GetComponent<RopeGun>() : null;
                }
                return cursorGunCache;
            }
        }

        // The point the camera centres on before offsets and look-ahead are applied
        private Vector2 AnchorOf(Transform followTarget)
        {
            Vector2 anchor = (Vector2)followTarget.position;
            if (mode == FollowMode.PlayerCursorMidpoint && CursorGun != null)
                anchor = (anchor + CursorGun.CursorPosition) * 0.5f;
            return anchor;
        }

        void Awake()
        {
            cam = GetComponent<Camera>();
            baseZ = transform.position.z;
            baseOrthoSize = cam.orthographicSize;
            followBasePosition = transform.position;

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
            Transform target = Target;
            if (target == null) return;

            // All three velocities must zero: SmoothDamp's velocity lives in fields, and without this
            // the residual inertia overshoots the frame after snapping — looks like "cut over then bounced"
            followVel = Vector2.zero;
            lookAheadVel = 0f;
            lookAheadNow = lookAhead * (PlayerBus.Face == FaceDirection.R ? 1f : -1f);

            Vector2 want = AnchorOf(target) + followOffset + new Vector2(lookAheadNow, 0f);
            followBasePosition = want;
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
            Transform target = Target;
            if (target == null) return followBasePosition;

            // Look-ahead toward facing: read the bus snapshot directly, no PlayerHandler reference needed
            float wantAhead = lookAhead * (PlayerBus.Face == FaceDirection.R ? 1f : -1f);
            lookAheadNow = Mathf.SmoothDamp(lookAheadNow, wantAhead, ref lookAheadVel, lookAheadSmooth);

            Vector2 want = AnchorOf(target) + followOffset + new Vector2(lookAheadNow, 0f);
            followBasePosition = Vector2.SmoothDamp(followBasePosition, want, ref followVel, followSmooth);
            return followBasePosition;
        }

        private Vector2 ShakeStep()
        {
            float dt = GameTimeController.PresentationDeltaTime;
            trauma = Mathf.Max(0f, trauma - traumaDecay * dt);
            if (trauma <= 0f) return Vector2.zero;

            // Squared: small trauma is barely felt, large is strong — more layered than linear
            float shake = trauma * trauma;
            shakeClock += dt;
            float t = shakeClock * shakeFrequency;

            // Perlin rather than pure random: adjacent frames are continuous, shaking rather than twitching
            return new Vector2(
                Mathf.PerlinNoise(seedX, t) * 2f - 1f,
                Mathf.PerlinNoise(seedY, t) * 2f - 1f) * (maxShakeOffset * shake);
        }

        private void ZoomStep()
        {
            zoomRemaining = Mathf.Max(0f, zoomRemaining - GameTimeController.PresentationDeltaTime);
            if (zoomDuration <= 0f || zoomRemaining <= 0f)
            {
                cam.orthographicSize = baseOrthoSize;
                return;
            }

            cam.orthographicSize = baseOrthoSize + zoomAmount * (zoomRemaining / zoomDuration);
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
            zoomRemaining = duration;
        }
    }
}
