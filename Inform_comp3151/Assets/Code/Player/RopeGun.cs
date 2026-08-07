using Inkform.Bus;
using Inkform.Interactable;
using Inkform.Item;
using Inkform.Life;
using Inkform.Tool;
using UnityEngine;

namespace Inkform.Player
{
    /// <summary>
    /// Rope gun: replaces dash as the primary weapon (dash moved to Shift). On hit, pulls in a straight
    /// line — no hanging, no swinging.
    ///
    /// Aiming: the reticle is a free cursor, driven by mouse delta / right stick (the Aim action),
    /// moving freely around the player within the range radius; the muzzle and reticle solve an initial
    /// velocity that passes through the reticle (ballistic solve); the bullet flies along that parabola
    /// — the dashed line samples the same trajectory, truncated at the hit point when terrain
    /// intercepts (green = will hook), drawn to the reticle when clear (red = will miss → reel in).
    ///
    /// Flight: the bullet is a real rigidbody (gravity, initial velocity = solve result, damping 0
    /// matches the preview; excludeLayers excludes the player/bombs etc., terrain only); the rope
    /// feeds out of the muzzle with its end pinned to the hook, sagging under the solve — heavier rope
    /// (ropeGravityScale) sags more.
    /// Hit: terrain → short hitstop, then pulls the player along a straight line toward the anchor
    /// (Pulling); pressing fire or jump mid-pull stops the force and reels the rope in; reaching near
    /// the anchor releases automatically.
    /// Miss / cancel: reel in — anchor = muzzle, end attached to the bullet rigidbody pulling it back
    /// (the same Verlet solve as bomb hanging chains).
    ///
    /// Special hits:
    /// ① carriable objects (objects implementing ICarriable: Bomb, CarriablePart-mounted objects, etc.):
    ///    short hitstop, then pulls the player toward the target, swallowing on arrival
    ///    (ICarriable.TrySwallowByRope);
    /// ② bomb hanging chains: severs at the hit point (Chain.CutAt, probing the Chain static registry
    ///    segment by segment).
    ///
    /// Range: default maxRange; special zones (RopeRangeZone) override/restore it via RopeGunBus.
    /// Attach to the Player; without it the game still plays (PlayerHandler probes with TryGetComponent).
    /// </summary>
    public class RopeGun : MonoBehaviour
    {
        private enum RopePhase { Idle, Flying, ReelIn, Pulling, PullingEat }

        [Header("Range")]
        [SerializeField] private float maxRange = 4.2f;                     // default max range (also reticle radius / rope length cap)
        [SerializeField] private LayerMask hitMask = (1 << 6) | (1 << 11);  // Terrain | Breakable

        [Header("Projectile")]
        [SerializeField] private float launchSpeed = 22f;
        [SerializeField] private float bulletGravityScale = 1f;
        [SerializeField] private float bulletRadius = 0.12f;
        [SerializeField] private Sprite bulletSprite;                       // swappable
        [SerializeField] private float muzzleOffset = 0.5f;                 // hook spawn point moved forward along the aim, avoids overlapping the player
        [SerializeField] private float bombDetectRadius = 0.55f;            // bomb-probe radius while the hook flies

        [Header("Aim")]
        [SerializeField] private float mouseAimSensitivity = 1f;
        [SerializeField] private float stickAimSpeed = 5f;
        [SerializeField] private float mouseShowThreshold = 0.5f;           // mouse |delta| (pixels) above this counts as "aiming input"
        [SerializeField] private float stickShowThreshold = 0.1f;           // right stick |delta| above this counts as "aiming input"
        [SerializeField] private float aimShowTime = 0.15f;                 // delay hiding the preview after input stops (debounce; 0 = hide immediately)
        [SerializeField] private float aimFallbackElevation = 30f;          // fire direction tilt upward (degrees) without aiming input
        [SerializeField] private Sprite crosshairSprite;                    // reticle, swappable
        [SerializeField] private float crosshairSize = 0.6f;
        [SerializeField] private Color hitColor = new Color(0.35f, 1f, 0.35f);
        [SerializeField] private Color missColor = new Color(1f, 0.35f, 0.35f);

        [Header("Parabola preview")]
        [SerializeField] private Material dashMaterial;                     // dashed-line material (per-unit tiling), swappable
        [SerializeField] private float dashWidth = 0.05f;
        [SerializeField] private float dashUnitScale = 0.5f;                // texture repeat spacing (world units)
        [SerializeField] private int dashSortingOrder = 10;
        [SerializeField] private float previewStepDt = 1f / 30f;

        [Header("Rope")]
        [SerializeField] private VerletRope.Settings ropeSettings = VerletRope.Settings.Default();
        [SerializeField] private LayerMask ropeCollisionMask = (1 << 6) | (1 << 11);  // rope segment collision layers (default Terrain|Breakable)
        [SerializeField] private float ropeGravityScale = 2.5f;                // rope weight: segment gravity factor, larger = sags more
        [SerializeField] private Material ropeMaterial;
        [SerializeField] private float ropeWidth = 0.06f;
        [SerializeField] private int ropeSortingOrder = -5;
        [SerializeField] private float chainCutRadius = 0.35f;              // how close the hook must be to a bomb chain segment to sever it

        [Header("Pull")]
        [SerializeField] private float pullSpeed = 16f;                     // hard-velocity pull (terrain hit / bomb grab shared): must beat the player's 3x gravity or upward pulls fail
        [SerializeField] private float arrivalDistance = 0.7f;              // arrival-at-contact-point threshold (blocked by a wall, the center sits ≈0.5 from the wall; 0.7 = arrived)
        [SerializeField] private float stuckTime = 0.25f;                   // pull-stuck fallback: distance stops dropping this long → release the rope
        [SerializeField] private float tautRopeGravity = 0.15f;             // rope gravity factor while pulling: low = taut, near-straight line
        [SerializeField] private float swallowDistance = 0.75f;             // distance to a bomb that triggers swallowing
        [SerializeField] private float detachDistance = 0.5f;               // reel-in release distance
        [SerializeField] private float reelTimeout = 2.5f;                  // reel-in timeout: forced recovery when the hook is stuck in a corner

        [Header("Reel & hit")]
        [SerializeField] private float reelSpeed = 5f;                      // miss/cancel: rope shortening speed (hook recovery)
        [SerializeField] private float hitStopTime = 0.06f;                 // brief hitstop on hit (via FxBus; ScreenFx caps at 0.25s)

        private RopePhase phase = RopePhase.Idle;
        private float currentMaxRange;
        private float ropeLength;       // current rope length (absolute cap = currentMaxRange); reel-in drives it shorter
        private Timer reelTimer;        // reel-in timeout: forced recovery when the hook is stuck in a corner
        private bool ropeTaut;          // hook is stretched against the range circle (truly reels only on the second consecutive frame, see FixedUpdate)

        private Rigidbody2D playerBody;
        private PlayerMotor motor;

        private Vector2 aimOffset;      // aim cursor offset from the player (world units), free-moving around the player
        private Vector2 moveInput;      // latest frame's move input (forwarded by PlayerHandler): fire-direction source without aiming input
        private Timer aimShowTimer;     // refreshed while aiming input exists; expired = hide preview + fire falls back to move direction tilted up
        private bool previewVisible;    // preview show/hide dedup

        private GameObject hookGo;
        private Rigidbody2D hookBody;
        private HookHit hookHit;

        private ICarriable grapple;        // PullingEat target (interface only, never knows the implementation)
        private Vector2 pullTarget;     // anchor: terrain hit point / carriable's current position
        private float lastPullDist;     // pull-stuck detection: distance to the anchor last physics step
        private float pullStuck;        // accumulated time the distance has stopped dropping

        private readonly VerletRope rope = new VerletRope();
        private LineRenderer ropeLine;

        private Transform reticle;
        private SpriteRenderer reticleSprite;
        private LineRenderer dashLine;

        // Preview parabola sample points (max 64 steps + start), avoids per-frame allocation
        private readonly Vector2[] arcPoints = new Vector2[65];
        private int arcCount;

        private static Sprite discSprite;   // runtime-generated white disc, fallback when no sprite is configured

        private static Sprite DiscSprite
        {
            get
            {
                if (discSprite != null) return discSprite;

                const int size = 32;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                float r = size * 0.5f - 1f;
                Vector2 c = new Vector2(size * 0.5f, size * 0.5f);
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c);
                        tex.SetPixel(x, y, d <= r ? Color.white : Color.clear);
                    }
                }
                tex.Apply();
                discSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
                return discSprite;
            }
        }

        /// <summary>Latest frame's move input (forwarded every frame by PlayerHandler, incl. keyboard stick synthesis / left stick).
        /// Without aiming input, the fire and spit direction = it tilted up aimFallbackElevation°.</summary>
        public void SetMoveInput(Vector2 input) => moveInput = input;

        /// <summary>
        /// Effective fire direction: with aiming input → precisely toward the reticle; otherwise → the
        /// current move direction (or the facing direction) tilted aimFallbackElevation° toward straight
        /// up (never past it). Shared by rope-gun firing and bomb spitting.
        /// </summary>
        public Vector2 EffectiveFireDir
        {
            get
            {
                if (aimShowTimer.IsRunning && aimOffset.sqrMagnitude > 0.0001f)
                    return aimOffset.normalized;

                Vector2 d = moveInput.sqrMagnitude > 0.01f
                    ? moveInput.normalized
                    : (PlayerBus.Face == FaceDirection.R ? Vector2.right : Vector2.left);

                float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                float elevated = angle < 90f
                    ? Mathf.Min(angle + aimFallbackElevation, 90f)
                    : Mathf.Max(angle - aimFallbackElevation, 90f);
                float rad = elevated * Mathf.Deg2Rad;
                return new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
            }
        }

        void Awake()
        {
            playerBody = GetComponent<Rigidbody2D>();
            TryGetComponent(out motor);
            currentMaxRange = maxRange;

            reticle = CreateFx("RopeReticle", out reticleSprite,
                crosshairSprite != null ? crosshairSprite : DiscSprite, 20);
            reticle.localScale = Vector3.one * crosshairSize;

            dashLine = CreateLine("RopeDashLine", dashMaterial, dashWidth, dashSortingOrder);
            ropeLine = CreateLine("RopeLine", ropeMaterial, ropeWidth, ropeSortingOrder);

            aimOffset = Vector2.right * currentMaxRange * 0.6f;
        }

        void OnEnable()
        {
            RopeGunBus.RangeOverride += OnRangeOverride;
            RopeGunBus.RangeRestored += OnRangeRestored;
            LifeBus.Died += OnDied;
            LifeBus.Respawned += OnRespawned;
        }

        void OnDisable()
        {
            RopeGunBus.RangeOverride -= OnRangeOverride;
            RopeGunBus.RangeRestored -= OnRangeRestored;
            LifeBus.Died -= OnDied;
            LifeBus.Respawned -= OnRespawned;
        }

        // ---- Input entries (forwarded by PlayerHandler) ----

        /// <summary>Aiming input. pixelDelta = mouse pixel delta (needs conversion to world units by screen
        /// height), otherwise a right-stick analog value (speed × dt). Refreshes the preview-show timer
        /// when the input passes the device threshold.</summary>
        public void Aim(Vector2 delta, bool pixelDelta)
        {
            if (phase != RopePhase.Idle || LifeBus.IsDead) return;

            if (pixelDelta)
            {
                Camera cam = Camera.main;
                float worldPerPixel = cam != null
                    ? cam.orthographicSize * 2f / Mathf.Max(1f, Screen.height)
                    : 0.01f;
                aimOffset += delta * worldPerPixel * mouseAimSensitivity;
                if (Mathf.Abs(delta.x) > mouseShowThreshold || Mathf.Abs(delta.y) > mouseShowThreshold)
                    aimShowTimer.Set(aimShowTime);
            }
            else
            {
                aimOffset += delta * (stickAimSpeed * Time.deltaTime);
                if (delta.sqrMagnitude > stickShowThreshold * stickShowThreshold)
                    aimShowTimer.Set(aimShowTime);
            }

            if (aimOffset.sqrMagnitude > currentMaxRange * currentMaxRange)
                aimOffset = aimOffset.normalized * currentMaxRange;
        }

        public void TryFire()
        {
            if (LifeBus.IsDead) return;
            if (phase != RopePhase.Idle)
            {
                Cancel();           // pressing again = cancel this shot / release the rope
                return;
            }
            if (ItemBus.Held != null) return;   // cannot fire with something in the mouth

            // With aiming input → precisely toward the reticle; when hidden → move direction tilted up (EffectiveFireDir)
            Vector2 fireDir = EffectiveFireDir;
            Vector2 origin = playerBody.position + fireDir * muzzleOffset;
            // Ballistic target: while aiming = the reticle point (passes through it exactly); hidden = along the effective direction to the range
            Vector2 target = aimShowTimer.IsRunning
                ? playerBody.position + aimOffset
                : origin + fireDir * currentMaxRange;
            SolveBallistic(origin, target, launchSpeed, Physics2D.gravity * bulletGravityScale,
                out Vector2 v0, out _);

            phase = RopePhase.Flying;
            ropeTaut = false;
            SetPreviewShown(false);

            hookGo = new GameObject("GrappleHook");
            hookGo.transform.position = origin;
            hookBody = hookGo.AddComponent<Rigidbody2D>();
            hookBody.gravityScale = bulletGravityScale;
            hookBody.linearDamping = 0f;                // matches the preview solve, so the parabola lines up
            // includeLayers has "append-allowed" semantics and does not restrict collisions — must use
            // excludeLayers to exclude everything but terrain (player/bombs etc.; the layer collision
            // matrix is all-on by default, without the exclusion the hook would hit the player at spawn)
            hookBody.excludeLayers = ~hitMask;
            var col = hookGo.AddComponent<CircleCollider2D>();
            col.radius = bulletRadius;
            var sr = hookGo.AddComponent<SpriteRenderer>();
            sr.sprite = bulletSprite != null ? bulletSprite : DiscSprite;
            sr.sortingOrder = 15;
            hookBody.linearVelocity = v0;
            hookHit = hookGo.AddComponent<HookHit>();
            hookHit.Init(this);

            rope.Init(RopeCfg(), origin, origin);
            ropeLine.enabled = true;
        }

        /// <summary>Called by PlayerHandler on jump press: during a pull, release the rope and let the jump happen.</summary>
        public void DetachOnJump()
        {
            if (phase == RopePhase.Idle || phase == RopePhase.ReelIn || phase == RopePhase.Flying) return;
            Finish();
        }

        // ---- Internal flow ----

        // Merges the serialized rope collision layers and weight into the solver config (rope segments
        // collide with terrain, sag heavier; bomb chains keep it off)
        private VerletRope.Settings RopeCfg()
        {
            var s = ropeSettings;
            s.collisionMask = ropeCollisionMask;
            s.gravityScale = ropeGravityScale;
            return s;
        }

        private void Cancel()
        {
            switch (phase)
            {
                case RopePhase.Idle: return;
                case RopePhase.Flying:
                    StartReelIn();
                    return;
                case RopePhase.ReelIn:
                    return;                         // already reeling
                default:
                    Finish();                       // pulling: release and recover the rope outright
                    return;
            }
        }

        private void OnHookTerrainHit(Collision2D collision)
        {
            if (phase != RopePhase.Flying) return;

            // Terrain/Breakable layers may hold carriables (solid eatable entities like food crates):
            // when the hook physically hits one, route to "eat-pull" instead of a plain terrain anchor
            if (collision.collider.TryGetComponent(out ICarriable carriable))
            {
                StartPullingCarriable(carriable);
                return;
            }

            ContactPoint2D contact = collision.GetContact(0);

            // Move the anchor outward by half a rope width along the contact normal: the contact point
            // itself sits on the terrain surface, a segment CircleCast starting there would spawn
            // overlapping (the project has QueriesStartInColliders on), and it keeps a small bit of
            // rope from rendering inside the wall. The normal direction convention is easy to get
            // backwards — check the sign with the hook's actual position.
            Vector2 outward = contact.normal;
            if (Vector2.Dot(outward, hookBody.position - contact.point) < 0f) outward = -outward;
            Vector2 hitPoint = contact.point + outward * Mathf.Max(ropeSettings.collisionRadius, 0.01f);

            // The hit point sits on the hook's surface, another bulletRadius beyond the hook center
            // (plus the normal offset above) — hitting a wall at the edge of the range without this
            // slack would mark a legal hit as over-range
            float rangeSlack = bulletRadius + Mathf.Max(ropeSettings.collisionRadius, 0.01f);
            if (Vector2.Distance(playerBody.position, hitPoint) > currentMaxRange + rangeSlack)
            {
                StartReelIn();      // beyond range: treat as a miss
                return;
            }

            pullTarget = hitPoint;
            AnchorHook();
            motor?.SetMoveLocked(true);
            reelTimer.Clear();      // hooked now, no longer a reel flow — do not leak the expiry time into the next shot

            // Brief hitstop at the hit moment: impact feel. Via FxBus; ScreenFx restores timeScale
            if (hitStopTime > 0f) FxBus.RaiseHitStop(hitStopTime);

            // Straight-line pull: no hanging, no swinging — the player is pulled along a straight line
            // toward the anchor (Pulling); pressing fire/jump mid-pull stops the force
            phase = RopePhase.Pulling;
            lastPullDist = float.MaxValue;      // first frame always advances; stuck timing only starts when truly stopped
            pullStuck = 0f;
        }

        private void StartReelIn()
        {
            if (phase == RopePhase.ReelIn) return;
            phase = RopePhase.ReelIn;
            reelTimer.Set(reelTimeout);

            // Rope length starts from the current hook distance and shortens by reelSpeed — the hook is
            // dragged back BY the rope; writing velocity directly would snap to full speed (reeling
            // "whoosh" with no rope-drag feel)
            if (hookBody != null)
                ropeLength = Mathf.Min(Vector2.Distance(playerBody.position, hookBody.position), currentMaxRange);
            // Reel-in keeps the collider: the hook slides back along walls without clipping (corners are covered by reelTimeout)
        }

        private void StartPullingCarriable(ICarriable target)
        {
            target.MarkRopeGrappled();
            grapple = target;
            phase = RopePhase.PullingEat;
            AnchorHook();
            motor?.SetMoveLocked(true);

            // Grabbing a carriable is also a "hit", same brief hitstop
            if (hitStopTime > 0f) FxBus.RaiseHitStop(hitStopTime);
        }

        // Hook becomes a static anchor: physics off, collision callbacks off, collider off — no longer
        // participates in any physical interaction
        private void AnchorHook()
        {
            hookBody.simulated = false;
            hookHit.enabled = false;
            if (hookGo.TryGetComponent(out CircleCollider2D col)) col.enabled = false;
        }

        private void Finish()
        {
            // The interface has no Unity fake-null overload: cast to MonoBehaviour first to recognize
            // destroyed targets
            if (grapple as MonoBehaviour != null)
            {
                grapple.ClearRopeGrappled();
            }
            grapple = null;
            motor?.SetMoveLocked(false);
            phase = RopePhase.Idle;
            reelTimer.Clear();
            ropeTaut = false;
            DespawnHook();
            ropeLine.enabled = false;
            // Preview show/hide is left to Update's input timer (stays hidden without input)
        }

        private void DespawnHook()
        {
            if (hookGo != null) Destroy(hookGo);
            hookGo = null;
            hookBody = null;
            hookHit = null;
        }

        private void SetPreviewShown(bool shown)
        {
            if (shown == previewVisible) return;    // dedup: per-frame driving never SetActive every frame
            previewVisible = shown;
            if (reticle != null) reticle.gameObject.SetActive(shown);
            dashLine.enabled = shown;
        }

        // ---- Frame loop ----

        void Update()
        {
            if (LifeBus.IsDead) return;
            if (phase == RopePhase.Idle)
            {
                // Preview only computes and shows with aiming input: the hidden state does not waste
                // ballistic solves and raycasts
                if (aimShowTimer.IsRunning) UpdatePreview();
                SetPreviewShown(aimShowTimer.IsRunning);
            }
        }

        void FixedUpdate()
        {
            if (phase == RopePhase.Idle || LifeBus.IsDead) return;

            float dt = Time.fixedDeltaTime;
            Vector2 origin = playerBody.position;

            switch (phase)
            {
                case RopePhase.Flying:
                    // Rope feeds out of the muzzle after the hook, never past currentMaxRange (fixed rope cap)
                    float hookDist = Vector2.Distance(origin, hookBody.position);
                    if (hookDist >= currentMaxRange)
                    {
                        // Physical rope length limit: the hook is stretched onto the range circle,
                        // removing only the outward radial velocity, keeping the tangential (like
                        // hitting the end of the rope)
                        Vector2 d = hookBody.position - origin;
                        Vector2 dir = d.sqrMagnitude > 0.0001f ? d.normalized : Vector2.right;
                        hookBody.position = origin + dir * currentMaxRange;
                        float radialOut = Vector2.Dot(hookBody.linearVelocity, dir);
                        if (radialOut > 0f) hookBody.linearVelocity -= dir * radialOut;
                        hookBody.angularVelocity = 0f;
                        ropeLength = currentMaxRange;
                        rope.SetLength(ropeLength);
                        rope.SolveFixed(dt, origin, rope.SegmentCount, null, hookBody.position);

                        // The first taut frame does not reel: OnCollisionEnter2D runs after FixedUpdate,
                        // and switching to ReelIn that same frame would drop the immediately-arriving
                        // legal terrain collision in OnHookTerrainHit's phase check — hitting a wall at
                        // the range edge would "touch but only reel". Leave one full step for the
                        // collision callback; only the second consecutive taut frame truly reels.
                        if (ropeTaut) StartReelIn();
                        else ropeTaut = true;
                        break;
                    }

                    ropeTaut = false;
                    rope.SetLength(hookDist);
                    rope.SolveFixed(dt, origin, rope.SegmentCount, null, hookBody.position);

                    CutChainsNearHook();
                    if (DetectCarriable()) return;
                    break;

                case RopePhase.ReelIn:
                    // Reeling = the rope reels in: length shortens by reelSpeed, the hook rides the rope
                    // end being dragged back (sagging with gravity)
                    if (!reelTimer.IsRunning) { Finish(); break; }      // corner-stuck timeout: forced recovery

                    ropeLength = Mathf.Max(0f, ropeLength - reelSpeed * dt);
                    var reel = RopeCfg();
                    rope.Configure(reel);
                    rope.SetLength(ropeLength);
                    rope.SolveFixed(dt, origin, rope.SegmentCount, hookBody, null);
                    if (ropeLength <= detachDistance
                        || Vector2.Distance(origin, hookBody.position) <= detachDistance)
                        Finish();
                    break;

                case RopePhase.PullingEat:
                    // The interface has no Unity fake-null overload: cast to MonoBehaviour first to
                    // recognize a blasted-away target
                    if (grapple as MonoBehaviour == null) { Finish(); break; }
                    pullTarget = grapple.transform.position;

                    float eatDist = PullStep(pullTarget, dt);

                    if (eatDist <= swallowDistance)
                    {
                        if (grapple.TrySwallowByRope(transform)) Finish();
                        else Finish();      // cannot swallow (mouth full etc.): release the rope, do not stall
                        break;
                    }

                    if (pullStuck >= stuckTime) Finish();   // target behind a wall and unreachable: timeout release
                    break;

                case RopePhase.Pulling:
                    float dist = PullStep(pullTarget, dt);

                    // Reached the contact point (blocked by a wall, the center sits ≈0.5 from it):
                    // release, keep momentum
                    if (dist <= arrivalDistance) { Finish(); break; }
                    if (pullStuck >= stuckTime) Finish();   // stuck fallback: sliding along a wall keeps distance dropping, not stuck
                    break;
            }
        }

        // Single pull step (terrain Pulling and carriable PullingEat share it):
        // hard velocity write-back (beats gravity, straight line) + taut rope + stuck-distance advance.
        // Returns the current distance to the target; the endpoint decision is the caller's.
        private float PullStep(Vector2 target, float dt)
        {
            Vector2 to = target - playerBody.position;
            float dist = to.magnitude;
            if (dist < 0.0001f) return dist;

            // The player's 3x gravity (≈29.4 m/s²) would crush an acceleration-style pull (upward
            // pulls would fail entirely); hard-writing beats gravity, path near-straight
            playerBody.linearVelocity = to / dist * pullSpeed;

            // Rope taut: the pulling phase solves with low-gravity config, near-straight line;
            // flight/reel-in still use the heavy sagging rope
            var taut = RopeCfg();
            taut.gravityScale = tautRopeGravity;
            rope.Configure(taut);
            rope.SetLength(dist);
            rope.SolveFixed(dt, target, rope.SegmentCount, null, playerBody.position);

            // Stuck advance: distance stops dropping (sliding along a wall keeps it dropping, not
            // stuck) → accumulated timeout releases the rope
            if (dist < lastPullDist - 0.01f) pullStuck = 0f;
            else pullStuck += dt;
            lastPullDist = dist;
            return dist;
        }

        // During flight, probes for carriables segment by segment: the hook physically does not touch
        // Default-layer objects (bombs etc.); found via the probe. The probe is layer-agnostic —
        // TryGetComponent recognizes the interface; both Bomb (direct implementation) and CarriablePart
        // (framework objects) hit
        private bool DetectCarriable()
        {
            Collider2D[] hits = Physics2D.OverlapCircleAll(
                hookBody.position, bombDetectRadius + bulletRadius);
            foreach (Collider2D h in hits)
            {
                if (h.TryGetComponent(out ICarriable carriable))
                {
                    StartPullingCarriable(carriable);
                    return true;
                }
            }
            return false;
        }

        // During flight, probes bomb hanging chains: point-to-segment distance under the radius severs
        // at the nearest segment
        private void CutChainsNearHook()
        {
            Vector2 p = hookBody.position;
            float maxDist = chainCutRadius + bulletRadius;
            foreach (Chain chain in Chain.Active)
            {
                int seg = chain.NearestSegment(p, maxDist);
                if (seg >= 0) chain.CutAt(seg);
            }
        }

        // Aim preview: reticle = free cursor (around the player); trajectory = the parabola solved to
        // pass through the reticle, raycast segment by segment — terrain blocking before the reticle →
        // dashed line truncated at the hit point (green = will hook); clear → drawn to the reticle
        // (red = will miss, reel in)
        private void UpdatePreview()
        {
            Vector2 aim = aimOffset.sqrMagnitude > 0.0001f ? aimOffset.normalized : Vector2.right;

            // Muzzle origin identical to firing: preview and actual trajectory strictly match
            Vector2 origin = playerBody.position + aim * muzzleOffset;
            Vector2 target = playerBody.position + aimOffset;
            Vector2 g = Physics2D.gravity * bulletGravityScale;

            SolveBallistic(origin, target, launchSpeed, g, out Vector2 v0, out float flightTime);

            float maxT = Mathf.Max(flightTime, previewStepDt);
            arcPoints[0] = origin;
            arcCount = 1;
            Vector2 last = origin;
            bool hit = false;

            // Sample slightly past the reticle (inertia continues past it and may hit farther terrain)
            for (int i = 1; i < arcPoints.Length; i++)
            {
                float t = i * previewStepDt;
                if (t > maxT + 0.25f) break;
                Vector2 p = origin + v0 * t + 0.5f * g * t * t;
                arcPoints[arcCount++] = p;

                Vector2 seg = p - last;
                float segLen = seg.magnitude;
                if (segLen > 0.0001f)
                {
                    RaycastHit2D rh = Physics2D.Raycast(last, seg / segLen, segLen, hitMask);
                    if (rh.collider != null)
                    {
                        arcPoints[arcCount - 1] = rh.point;
                        hit = true;
                        break;
                    }
                }

                if (Vector2.Distance(origin, p) >= currentMaxRange) break;
                last = p;
            }

            // Reticle = the free cursor itself; color hint: green = trajectory intercepted by terrain
            // (will hook), red = will miss
            reticle.position = target;
            reticleSprite.color = hit ? hitColor : missColor;
            float s = crosshairSize * (hit ? 1.25f : 1f);
            reticle.localScale = new Vector3(s, s, 1f);

            // Dashed line: parabola sample points; texture spread at per-unit density
            float totalLen = 0f;
            for (int i = 1; i < arcCount; i++) totalLen += Vector2.Distance(arcPoints[i - 1], arcPoints[i]);
            dashLine.positionCount = arcCount;
            for (int i = 0; i < arcCount; i++) dashLine.SetPosition(i, arcPoints[i]);
            dashLine.textureScale = new Vector2(
                dashUnitScale > 0.001f ? totalLen / dashUnitScale : totalLen, 1f);
        }

        /// <summary>
        /// Ballistic solve: given the start and target points, launch speed magnitude, and gravity, find
        /// the initial velocity that sends the projectile through the target. Letting u = t² and
        /// substituting into the parabola equation yields a quadratic in u; take the low-arc root
        /// (smaller t). t is clamped with a lower bound to avoid explosive speeds when the target is
        /// too close.
        /// </summary>
        private void SolveBallistic(Vector2 origin, Vector2 target, float speed,
                                    Vector2 g, out Vector2 velocity, out float flightTime)
        {
            float dx = target.x - origin.x;
            float dy = target.y - origin.y;
            float gy = g.y;                 // negative (downward)

            // 0.25·g²·u² − (dy·g + v²)·u + (dx² + dy²) = 0
            float a = 0.25f * gy * gy;
            float b = -(dy * gy + speed * speed);
            float c = dx * dx + dy * dy;

            float u;
            if (a > 1e-8f && b * b >= 4f * a * c)
            {
                float disc = Mathf.Sqrt(b * b - 4f * a * c);
                u = (-b - disc) / (2f * a);     // low-arc root
                u = Mathf.Max(u, 0f);
            }
            else
            {
                u = 0f;                          // target unreachable (cannot happen: the cursor is clamped inside the range)
            }

            flightTime = Mathf.Max(Mathf.Sqrt(u), 0.05f);
            velocity = new Vector2(
                dx / flightTime,
                dy / flightTime - 0.5f * gy * flightTime);
        }

        void LateUpdate()
        {
            if (phase == RopePhase.Idle) return;

            // A hook attached to a carriable follows the target (PullingEat has the hook's physics off;
            // synced manually)
            if (hookGo != null && hookBody != null && !hookBody.simulated && phase == RopePhase.PullingEat)
                hookGo.transform.position = pullTarget;

            // Rope rendering
            ropeLine.positionCount = rope.SegmentCount + 1;
            for (int i = 0; i <= rope.SegmentCount; i++)
                ropeLine.SetPosition(i, rope.GetPoint(i));
        }

        // ---- Bus callbacks ----

        private void OnRangeOverride(float range)
        {
            currentMaxRange = Mathf.Max(0.1f, range);
        }

        private void OnRangeRestored()
        {
            currentMaxRange = maxRange;
        }

        private void OnDied(DeathContext ctx)
        {
            if (ctx.Victim != gameObject) return;

            // Finish is idempotent: clears the grapple mark, unlocks movement, resets state, destroys
            // the hook — all in one
            Finish();
            // Update is blocked by IsDead during death, so the preview show/hide gate never runs —
            // must hide explicitly here
            SetPreviewShown(false);
        }

        private void OnRespawned(GameObject victim, Vector2 pos)
        {
            if (victim != gameObject) return;
            aimOffset = (PlayerBus.Face == FaceDirection.R ? Vector2.right : Vector2.left) * currentMaxRange * 0.6f;
            // Preview is decided by Update's input timer: stays hidden without input
        }

        // ---- Runtime objects ----

        private Transform CreateFx(string name, out SpriteRenderer sprite, Sprite fallback, int sortingOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            sprite = go.AddComponent<SpriteRenderer>();
            sprite.sprite = fallback;
            sprite.sortingOrder = sortingOrder;
            return go.transform;
        }

        private LineRenderer CreateLine(string name, Material material, float width, int sortingOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.alignment = LineAlignment.TransformZ;
            line.loop = false;
            line.widthMultiplier = width;
            line.sortingOrder = sortingOrder;
            line.material = material;   // null = default solid color
            line.positionCount = 0;
            line.enabled = false;
            return line;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.9f, 0.2f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, maxRange);
        }

        // The hook's own collision callback: excludeLayers guarantees terrain only (Terrain|Breakable),
        // never the player/bombs
        private class HookHit : MonoBehaviour
        {
            private RopeGun owner;

            public void Init(RopeGun ropeGun) => owner = ropeGun;

            void OnCollisionEnter2D(Collision2D collision) => owner.OnHookTerrainHit(collision);
        }
    }
}
