using Inkform.Bus;
using Inkform.Interactable;
using Inkform.Item;
using Inkform.Life;
using Inkform.Settings;
using System.Collections.Generic;
using UnityEngine;

namespace Inkform.Player
{
    /// <summary>
    /// Rope gun: replaces dash as the primary weapon (dash moved to Shift). On hit, pulls in a straight
    /// line — no hanging, no swinging.
    ///
    /// Aiming: the reticle is a free cursor, driven by mouse delta / right stick (the Aim action),
    /// moving freely around the player within the range radius, always visible — firing and spitting
    /// always go exactly toward it, no movement-direction fallback. The muzzle and reticle solve an
    /// initial velocity that passes through the reticle (ballistic solve); the bullet flies along that
    /// parabola — the reticle is tinted green when the trajectory is intercepted by terrain before it
    /// (will hook), red when clear (will miss → reel in).
    ///
    /// Flight: the bullet is a real rigidbody (gravity, initial velocity = solve result, damping 0
    /// matches the preview; excludeLayers excludes the player/bombs etc., terrain only); the rope
    /// feeds out of the muzzle as a straight line to the hook — no Verlet simulation for the rope gun
    /// (bomb hanging chains still use it).
    /// Hit: terrain → short hitstop, then pulls the player along a straight line toward the anchor
    /// (Pulling); pressing fire or jump mid-pull releases the rope outright; reaching near the anchor
    /// releases automatically.
    /// Miss / cancel: reel in — the hook is dragged back in a straight line to the muzzle (no rope
    /// solve, collider off, catches nothing on the way).
    ///
    /// Special hits:
    /// ① carriable objects (objects implementing ICarriable: CarriablePart-mounted objects, etc.):
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
        private enum RopePhase { Idle, Flying, Pulling, Miss }

        [Header("Range")]
        [SerializeField] private float maxRange = 4.2f;                     // default max range (also reticle radius / hook travel cap)
        [SerializeField] private LayerMask hitMask = (1 << 6) | (1 << 11);  // Terrain | Breakable

        [Header("Projectile")]
        [SerializeField] private float launchSpeed = 22f;
        [SerializeField] private float bulletGravityScale = 1f;
        [SerializeField] private float bulletRadius = 0.12f;
        [SerializeField] private Sprite bulletSprite;                       // swappable
        [SerializeField] private float spriteAngleOffset = 0f;              // hook sprite facing offset (degrees); 0 = art points right (+X)
        [SerializeField] private float muzzleOffset = 0.5f;                 // hook spawn point moved forward along the aim, avoids overlapping the player
        [SerializeField] private float bombDetectRadius = 0.55f;            // bomb-probe radius while the hook flies

        [Header("Aim")]
        [SerializeField] private float mouseAimSensitivity = 1f;
        [SerializeField] private float stickAimSpeed = 5f;
        [SerializeField] private Sprite crosshairSprite;                    // reticle, swappable
        [SerializeField] private float crosshairSize = 0.6f;
        [SerializeField] private Color hitColor = new Color(0.35f, 1f, 0.35f);
        [SerializeField] private Color missColor = new Color(1f, 0.35f, 0.35f);

        [Header("Reticle prediction")]
        [SerializeField] private float previewStepDt = 1f / 30f;            // trajectory sample step for the hit prediction

        [Header("Rope")]
        [SerializeField] private Sprite ropeSegmentSprite;                  // vertical rope strip: the art runs along the sprite's +Y, tiled along the rope's length
        [SerializeField] private Material ropeMaterial;                     // optional; null = the SpriteRenderer default (Sprites-Default)
        [SerializeField] private float ropeWidth = 0.06f;
        [SerializeField] private int ropeSortingOrder = -5;
        [SerializeField] private float anchorClearance = 0.04f;             // hook anchor offset outward from the contact surface (keeps a bit of rope out of the wall)
        [SerializeField] private float chainCutRadius = 0.35f;              // how close the hook must be to a bomb chain segment to sever it

        [Header("Pull")]
        [SerializeField] private float pullSpeed = 16f;                     // hard-velocity pull (terrain hit / bomb grab shared): must beat the player's 3x gravity or upward pulls fail
        [SerializeField] private float arrivalDistance = 0.7f;              // arrival-at-contact-point threshold (blocked by a wall, the center sits ≈0.5 from the wall; 0.7 = arrived)
        [SerializeField] private float stuckTime = 0.25f;                   // pull-stuck fallback: distance stops dropping this long → release the rope
        [SerializeField] private float swallowDistance = 0.75f;             // distance to a bomb that triggers swallowing
        [SerializeField] private float detachDistance = 0.5f;               // miss recovery: release distance from the muzzle

        [Header("Reel & hit")]
        [SerializeField] private float reelSpeed = 5f;                      // miss recovery: straight-line hook pull-back speed
        [SerializeField] private float hitStopTime = 0.06f;                 // brief hitstop on hit (via FxBus; ScreenFx caps at 0.25s)

        private RopePhase phase = RopePhase.Idle;
        private float currentMaxRange;
        private readonly Dictionary<object, float> rangeOverrides = new Dictionary<object, float>();

        // Sensitivity bases: the serialized values are the designers' tuning. The user setting is a
        // multiplier applied on top, captured once in Awake before SettingsStore overrides the fields.
        private float mouseSensitivityBase;
        private float stickAimSpeedBase;

        private Rigidbody2D playerBody;
        private PlayerMotor motor;

        private Vector2 aimOffset;      // aim cursor offset from the player (world units), free-moving around the player

        private GameObject hookGo;
        private Rigidbody2D hookBody;
        private HookHit hookHit;

        private ICarriable grapple;        // eat-pull target; null = plain terrain pull
        private Vector2 pullTarget;     // anchor: terrain hit point / carriable's current position
        private Collider2D anchor;      // the collider the terrain hook is attached to; null = static anchor
        private Vector2 anchorLocal;    // hit point in the anchor collider's local space, so the hook follows moving terrain
        private float lastPullDist;     // pull-stuck detection: distance to the anchor last physics step
        private float pullStuck;        // accumulated time the distance has stopped dropping

        private Transform ropeTf;
        private SpriteRenderer ropeRenderer;
        private float ropeTileWidth;    // world size of one tile across the rope's width; derived from the sprite once in Awake

        private Transform reticle;
        private SpriteRenderer reticleSprite;

        private static Sprite discSprite;   // runtime-generated white disc, fallback when no sprite is configured
        private bool equipped = true;

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

        /// <summary>
        /// Effective fire direction: always exactly toward the reticle (the persistent aim cursor),
        /// whether or not the player recently moved it — no movement-direction fallback. Shared by
        /// rope-gun firing and bomb spitting.
        /// </summary>
        public Vector2 EffectiveFireDir
        {
            get
            {
                if (aimOffset.sqrMagnitude > 0.0001f) return aimOffset.normalized;

                // aimOffset is initialized to a non-zero value and only ever clamped to a range ≥ 0.1,
                // so this path is unreachable in practice — defensive fallback only
                return PlayerBus.Face == FaceDirection.R ? Vector2.right : Vector2.left;
            }
        }

        public void SetEquipped(bool value)
        {
            if (equipped == value) return;
            equipped = value;
            if (!equipped && ropeRenderer != null) Finish();
            if (reticleSprite != null) reticleSprite.enabled = equipped;
            if (ropeRenderer != null && !equipped) ropeRenderer.enabled = false;
        }

        public void CancelForControlLock()
        {
            if (ropeRenderer != null) Finish();
        }

        void Awake()
        {
            playerBody = GetComponent<Rigidbody2D>();
            TryGetComponent(out motor);
            currentMaxRange = maxRange;

            mouseSensitivityBase = mouseAimSensitivity;
            stickAimSpeedBase = stickAimSpeed;

            reticle = CreateFx("RopeReticle", out reticleSprite,
                crosshairSprite != null ? crosshairSprite : DiscSprite, 20);
            reticle.localScale = Vector3.one * crosshairSize;

            CreateRope();

            aimOffset = Vector2.right * currentMaxRange * 0.6f;
            reticleSprite.enabled = equipped;
        }

        void OnEnable()
        {
            RopeGunBus.RangeOverride += OnRangeOverride;
            RopeGunBus.RangeRestored += OnRangeRestored;
            LifeBus.Died += OnDied;
            LifeBus.Respawned += OnRespawned;

            // SettingsStore is static and always loaded, so subscription needs no instance guard.
            // Re-apply here too: after a scene reload this RopeGun is fresh while the settings live on.
            SettingsStore.Changed += OnSettingsChanged;
            ApplySensitivity();
        }

        void OnDisable()
        {
            RopeGunBus.RangeOverride -= OnRangeOverride;
            RopeGunBus.RangeRestored -= OnRangeRestored;
            LifeBus.Died -= OnDied;
            LifeBus.Respawned -= OnRespawned;
            SettingsStore.Changed -= OnSettingsChanged;
            rangeOverrides.Clear();
            currentMaxRange = maxRange;
        }

        private void OnSettingsChanged() => ApplySensitivity();

        private void ApplySensitivity()
        {
            mouseAimSensitivity = mouseSensitivityBase * SettingsStore.MouseSensitivity;
            stickAimSpeed = stickAimSpeedBase * SettingsStore.StickSensitivity;
        }

        // ---- Input entries (forwarded by PlayerHandler) ----

        /// <summary>Aiming input. pixelDelta = mouse pixel delta (needs conversion to world units by screen
        /// height), otherwise a right-stick analog value (speed × dt). Moves the always-visible reticle.</summary>
        public void Aim(Vector2 delta, bool pixelDelta)
        {
            if (!equipped) return;
            if (phase != RopePhase.Idle || LifeBus.IsDead) return;

            if (pixelDelta)
            {
                Camera cam = Camera.main;
                float worldPerPixel = cam != null
                    ? cam.orthographicSize * 2f / Mathf.Max(1f, Screen.height)
                    : 0.01f;
                aimOffset += delta * worldPerPixel * mouseAimSensitivity;
            }
            else
            {
                aimOffset += delta * (stickAimSpeed * Time.deltaTime);
            }

            if (aimOffset.sqrMagnitude > currentMaxRange * currentMaxRange)
                aimOffset = aimOffset.normalized * currentMaxRange;
        }

        public void TryFire()
        {
            if (!equipped) return;
            if (LifeBus.IsDead) return;
            if (phase != RopePhase.Idle)
            {
                Cancel();           // pressing again = cancel this shot / release the rope
                return;
            }

            // Always exactly toward the reticle (EffectiveFireDir), no movement-direction fallback
            Vector2 fireDir = EffectiveFireDir;
            Vector2 origin = playerBody.position + fireDir * muzzleOffset;
            // Ballistic target = the reticle point: the bullet passes through it exactly
            Vector2 target = playerBody.position + aimOffset;
            SolveBallistic(origin, target, launchSpeed, Physics2D.gravity * bulletGravityScale,
                out Vector2 v0, out _);

            phase = RopePhase.Flying;

            RopeGunBus.RaiseFired(fireDir);

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
            FaceHookRotation(v0);
            hookHit = hookGo.AddComponent<HookHit>();
            hookHit.Init(this);

            ropeRenderer.enabled = true;
        }

        /// <summary>Called by PlayerHandler on jump press: during a pull, release the rope and let the jump happen.</summary>
        public void DetachOnJump()
        {
            if (phase != RopePhase.Pulling) return;
            Finish();
        }

        // ---- Internal flow ----

        private void Cancel()
        {
            switch (phase)
            {
                case RopePhase.Idle: return;
                case RopePhase.Flying:
                    StartMiss();
                    return;
                case RopePhase.Miss:
                    return;                         // already recovering
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

            // Move the anchor outward from the contact surface: the contact point itself sits on the
            // terrain, and a small offset keeps a bit of rope from rendering inside the wall. The
            // normal direction convention is easy to get backwards — check the sign with the hook's
            // actual position.
            Vector2 outward = contact.normal;
            if (Vector2.Dot(outward, hookBody.position - contact.point) < 0f) outward = -outward;
            Vector2 hitPoint = contact.point + outward * Mathf.Max(anchorClearance, 0.01f);

            // The hit point sits on the hook's surface, another bulletRadius beyond the hook center
            // (plus the normal offset above) — hitting a wall at the edge of the range without this
            // slack would mark a legal hit as over-range
            float rangeSlack = bulletRadius + Mathf.Max(anchorClearance, 0.01f);
            if (Vector2.Distance(playerBody.position, hitPoint) > currentMaxRange + rangeSlack)
            {
                StartMiss();        // beyond range: treat as a miss
                return;
            }

            pullTarget = hitPoint;
            // Terrain may move (PatrolMover platforms, spinners, ...): keep the anchor attached to the
            // hit collider's local point so hook + rope follow the object instead of hanging in space.
            // Static terrain colliders never move, so this local-space bookkeeping is a no-op there
            anchor = collision.collider;
            anchorLocal = anchor.transform.InverseTransformPoint(hitPoint);
            AnchorHook();
            motor?.SetMoveLocked(true);

            // Direction from the player toward the anchor, for directional feedback (haptics, ...)
            RopeGunBus.RaiseHit((hookBody.position - playerBody.position).normalized);

            // Brief hitstop at the hit moment: impact feel. Via FxBus; ScreenFx restores timeScale
            if (hitStopTime > 0f) FxBus.RaiseHitStop(hitStopTime);

            // Straight-line pull: no hanging, no swinging — the player is pulled along a straight line
            // toward the anchor (Pulling); pressing fire/jump mid-pull stops the force
            phase = RopePhase.Pulling;
            lastPullDist = float.MaxValue;      // first frame always advances; stuck timing only starts when truly stopped
            pullStuck = 0f;
        }

        // Miss / cancel recovery: the hook is dragged straight back to the muzzle. Collider off — no
        // wall-sliding, no bumping bombs, no contact callbacks at all, it just rides the rope end
        // straight back (passes through geometry). The next shot creates a fresh hook with the collider on.
        // All callers guarantee the hook is still Flying when this runs.
        private void StartMiss()
        {
            phase = RopePhase.Miss;

            if (hookGo.TryGetComponent(out CircleCollider2D col)) col.enabled = false;
        }

        private void StartPullingCarriable(ICarriable target)
        {
            target.MarkRopeGrappled();
            grapple = target;

            // The anchor must be live before the phase flips: LateUpdate draws the rope (and snaps the
            // hook) from pullTarget, but FixedUpdate only refreshes it on the NEXT physics step — and
            // the hitstop below zeroes timeScale, which suspends FixedUpdate entirely. A stale
            // pullTarget would therefore hang the rope off the previous shot's anchor (or the world
            // origin on the first grab) for the whole freeze, not just one frame.
            pullTarget = target.transform.position;
            phase = RopePhase.Pulling;
            AnchorHook();
            motor?.SetMoveLocked(true);

            // Same directional feedback as the terrain hit: from the player toward the grabbed target
            RopeGunBus.RaiseHit(((Vector2)target.transform.position - playerBody.position).normalized);

            // Same reset as the terrain path: a stuck-timeout release leaves pullStuck at the limit,
            // and without clearing it the very first PullStep would trip the timeout and cancel the grab
            lastPullDist = float.MaxValue;
            pullStuck = 0f;

            // Grabbing a carriable is also a "hit", same brief hitstop
            if (hitStopTime > 0f) FxBus.RaiseHitStop(hitStopTime);
        }

        // Hook becomes a static anchor: physics and collider off — no longer participates in any
        // physical interaction (the collider being off already blocks all contact callbacks, so the
        // hook's own collision component needs no toggle)
        private void AnchorHook()
        {
            hookBody.simulated = false;
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
            anchor = null;
            motor?.SetMoveLocked(false);
            phase = RopePhase.Idle;
            DespawnHook();
            ropeRenderer.enabled = false;
        }

        private void DespawnHook()
        {
            if (hookGo != null) Destroy(hookGo);
            hookGo = null;
            hookBody = null;
            hookHit = null;
        }

        // Rotates the hook's sprite to face a direction (+X = 0°). Written to the rigidbody, not the
        // transform: with m_AutoSyncTransforms = 0 a direct transform write would be overwritten by
        // the next physics sync
        private void FaceHookRotation(Vector2 dir)
        {
            if (dir.sqrMagnitude <= 0.0001f) return;
            hookBody.rotation = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg + spriteAngleOffset;
        }

        // ---- Frame loop ----

        void Update()
        {
            if (!equipped) return;
            if (LifeBus.IsDead) return;
            // The reticle is always visible: it updates every frame while idle, so the player always
            // knows exactly where the next shot will go
            if (phase == RopePhase.Idle) UpdatePreview();
        }

        void FixedUpdate()
        {
            if (phase == RopePhase.Idle || LifeBus.IsDead) return;

            float dt = Time.fixedDeltaTime;
            Vector2 origin = playerBody.position;

            switch (phase)
            {
                case RopePhase.Flying:
                    // Range cap: reaching the range circle is a miss — recovery starts immediately,
                    // no grace frame for an edge-of-range hook (the hook must physically hit terrain
                    // while still inside the range to ever pull)
                    if (Vector2.Distance(origin, hookBody.position) >= currentMaxRange)
                    {
                        StartMiss();
                        break;
                    }

                    // The parabola bends under gravity: keep the sprite facing the current motion
                    // direction every physics step
                    FaceHookRotation(hookBody.linearVelocity);

                    CutChainsNearHook();
                    if (DetectCarriable()) return;
                    break;

                case RopePhase.Miss:
                    // Miss recovery = the hook is dragged back to the muzzle in a straight line at
                    // reelSpeed (no rope solve: position written directly, velocity zeroed so gravity
                    // never curves the path; the collider is off so nothing can block it)
                    hookBody.position = Vector2.MoveTowards(
                        hookBody.position, origin, reelSpeed * dt);
                    hookBody.linearVelocity = Vector2.zero;
                    if (Vector2.Distance(origin, hookBody.position) <= detachDistance)
                        Finish();
                    break;

                case RopePhase.Pulling:
                    if (grapple != null)
                    {
                        // Eat-pull: the anchor follows the target; swallowing ends the pull.
                        // The interface has no Unity fake-null overload: cast to MonoBehaviour first
                        // to recognize a blasted-away target
                        if (grapple as MonoBehaviour == null) { Finish(); break; }
                        pullTarget = grapple.transform.position;

                        if (PullStep(pullTarget, dt) <= swallowDistance)
                        {
                            grapple.TrySwallowByRope(transform);   // cannot swallow (mouth full etc.): release the rope, do not stall
                            Finish();
                            break;
                        }

                        if (pullStuck >= stuckTime) Finish();   // target behind a wall and unreachable: timeout release
                        break;
                    }

                    // Terrain pull: the anchor follows the hit collider (static terrain never moves;
                    // moving platforms/spinners carry the hook with them), release on arrival
                    if (anchor != null) pullTarget = anchor.transform.TransformPoint(anchorLocal);
                    if (PullStep(pullTarget, dt) <= arrivalDistance)
                    {
                        // Reached the contact point (blocked by a wall, the center sits ≈0.5 from it):
                        // release, keep momentum
                        Finish();
                        break;
                    }
                    if (pullStuck >= stuckTime) Finish();   // stuck fallback: sliding along a wall keeps distance dropping, not stuck
                    break;
            }
        }

        // Single pull step (terrain Pulling and eat Pulling share it):
        // hard velocity write-back (beats gravity, straight line) + stuck-distance advance.
        // Returns the current distance to the target; the endpoint decision is the caller's.
        private float PullStep(Vector2 target, float dt)
        {
            Vector2 to = target - playerBody.position;
            float dist = to.magnitude;
            if (dist < 0.0001f) return dist;

            // The player's 3x gravity (≈29.4 m/s²) would crush an acceleration-style pull (upward
            // pulls would fail entirely); hard-writing beats gravity, path near-straight
            playerBody.linearVelocity = to / dist * pullSpeed;

            // Stuck advance: distance stops dropping (sliding along a wall keeps it dropping, not
            // stuck) → accumulated timeout releases the rope
            if (dist < lastPullDist - 0.01f) pullStuck = 0f;
            else pullStuck += dt;
            lastPullDist = dist;
            return dist;
        }

        // During flight, probes for carriables segment by segment: the hook physically does not touch
        // Default-layer objects (bombs etc.); found via the probe. The probe is layer-agnostic —
        // TryGetComponent recognizes the interface; both the former Bomb (direct implementation) and
        // CarriablePart (framework objects) hit
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

        // Aim prediction: reticle = free cursor (around the player); the trajectory is the parabola
        // solved to pass through the reticle, sampled segment by segment — terrain blocking before the
        // reticle → green (will hook); clear → red (will miss, reel in). The reticle is always visible;
        // firing and spitting always go exactly toward it.
        private void UpdatePreview()
        {
            Vector2 aim = aimOffset.sqrMagnitude > 0.0001f ? aimOffset.normalized : Vector2.right;

            // Muzzle origin identical to firing: preview and actual trajectory strictly match
            Vector2 origin = playerBody.position + aim * muzzleOffset;
            Vector2 target = playerBody.position + aimOffset;
            Vector2 g = Physics2D.gravity * bulletGravityScale;

            SolveBallistic(origin, target, launchSpeed, g, out Vector2 v0, out float flightTime);

            float maxT = Mathf.Max(flightTime, previewStepDt);
            Vector2 last = origin;
            bool hit = false;

            // Sample slightly past the reticle (inertia continues past it and may hit farther terrain)
            for (float t = previewStepDt; ; t += previewStepDt)
            {
                if (t > maxT + 0.25f) break;
                Vector2 p = origin + v0 * t + 0.5f * g * t * t;

                Vector2 seg = p - last;
                float segLen = seg.magnitude;
                if (segLen > 0.0001f)
                {
                    RaycastHit2D rh = Physics2D.Raycast(last, seg / segLen, segLen, hitMask);
                    if (rh.collider != null)
                    {
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
            if (!equipped) return;
            if (phase == RopePhase.Idle) return;

            // A hook with physics off follows the live anchor — eat-pull follows the carriable, terrain
            // pull follows the moving collider (both synced manually; static terrain never moves)
            if (hookGo != null && hookBody != null && !hookBody.simulated)
                hookGo.transform.position = pullTarget;

            // Rope rendering: a straight span between the two ends — the rope gun uses no rope
            // simulation, so the rope is always straight (no sag)
            Vector2 a, b;
            if (phase == RopePhase.Pulling)
            {
                a = pullTarget;                 // anchor: terrain hit point / carriable position
                b = playerBody.position;
            }
            else
            {
                a = playerBody.position;        // muzzle
                b = hookBody != null ? hookBody.position : a;
            }

            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 0.0001f)
            {
                // Hook still sitting on the muzzle: the direction is undefined, so draw nothing rather
                // than flash a rope at an arbitrary angle
                ropeRenderer.enabled = false;
                return;
            }

            ropeRenderer.enabled = true;
            ropeTf.position = (a + b) * 0.5f;                    // the sprite's pivot is Center
            // The art runs along the sprite's own +Y, so aiming +Y down the rope is a -90° offset
            ropeTf.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg - 90f);
            ropeTf.localScale = new Vector3(ropeWidth / ropeTileWidth, 1f, 1f);
            ropeRenderer.size = new Vector2(ropeTileWidth, len); // tiles along the length: fixed spacing
        }

        // ---- Bus callbacks ----

        private void OnRangeOverride(object source, float range)
        {
            if (source == null) return;
            rangeOverrides[source] = Mathf.Max(0.1f, range);
            RecalculateRange();
        }

        private void OnRangeRestored(object source)
        {
            if (source == null) return;
            rangeOverrides.Remove(source);
            RecalculateRange();
        }

        private void RecalculateRange()
        {
            currentMaxRange = maxRange;
            foreach (float range in rangeOverrides.Values)
                currentMaxRange = Mathf.Min(currentMaxRange, range);

            if (aimOffset.sqrMagnitude > currentMaxRange * currentMaxRange)
                aimOffset = aimOffset.normalized * currentMaxRange;
        }

        private void OnDied(DeathContext ctx)
        {
            if (ctx.Victim != gameObject) return;

            // Finish is idempotent: clears the grapple mark, unlocks movement, resets state, destroys
            // the hook — all in one
            Finish();
        }

        private void OnRespawned(GameObject victim, Vector2 pos)
        {
            if (victim != gameObject) return;
            aimOffset = (PlayerBus.Face == FaceDirection.R ? Vector2.right : Vector2.left) * currentMaxRange * 0.6f;
            // The reticle repositions from the new aimOffset on the next UpdatePreview
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

        // Rope rendering is a tiled SpriteRenderer, not a LineRenderer: a LineRenderer maps the
        // texture's U axis along the line's length, but the rope art is a narrow vertical strip inside
        // a mostly-empty atlas, so the line sampled the atlas's blank columns and only a fifth of it
        // ever showed pixels. A SpriteRenderer samples the sprite's sub-rect instead — the strip is
        // all there is. The strip is turned along the rope by rotating the transform, and
        // SpriteDrawMode.Tiled repeats it so the link spacing holds at any rope length.
        private void CreateRope()
        {
            ropeTf = CreateFx("Rope", out ropeRenderer,
                ropeSegmentSprite != null ? ropeSegmentSprite : DiscSprite, ropeSortingOrder);
            ropeRenderer.drawMode = SpriteDrawMode.Tiled;
            ropeRenderer.tileMode = SpriteTileMode.Continuous;  // trailing part-tile just clips; spacing stays exact
            // sharedMaterial, not material: nothing here writes to the material, so there is no reason
            // to instantiate a per-player copy of it
            if (ropeMaterial != null) ropeRenderer.sharedMaterial = ropeMaterial;
            ropeRenderer.enabled = false;

            // Tiled mode clips any axis sized under one whole tile, so the rope's width must hold
            // exactly one tile and the visual thickness comes from localScale instead (applied in
            // LateUpdate, so ropeWidth stays tunable in play mode) — that keeps the sprite's
            // pixels-per-unit (link spacing) and ropeWidth (thickness) independent of each other.
            Sprite s = ropeRenderer.sprite;
            ropeTileWidth = s.rect.width / s.pixelsPerUnit;
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
