using Inkform.Bus;
using Inkform.Fx;
using Inkform.Interactable;
using Inkform.Tool;
using System.Collections.Generic;
using UnityEngine;

namespace Inkform.Item
{
    /// <summary>
    /// Bomb: explodes on contact / is swallowed after being hit by the rope gun and reeled to the
    /// player's mouth / detonates on a fuse countdown after being spit out.
    /// Swallowing does not destroy it — physics and rendering are disabled and it hangs on the player;
    /// spitting puts the same instance back into the world.
    /// Note: dash (attack key) no longer swallows bombs — contact detonates; swallowing is rope-gun
    /// only (TrySwallowByRope).
    ///
    /// Two modes:
    /// Normal — the behavior above, identical to the old version.
    /// Hanging — hanging mode: Awake generates a physical chain (Chain, Verlet rope) per hanging point
    /// (HangingPoint child, editor-generated, draggable), the anchor = the hanging point's initial
    /// world position (fixed); chains can be severed by the player's attacks or explosions; when all
    /// are severed the bomb free-falls. Hanging-mode state is unrecoverable (severed chains, spit out
    /// after being swallowed, explosion destruction) — the player dying and respawning does not reset
    /// it; this class deliberately does not implement IRestorable as part of that.
    ///
    /// Speed detonation: once velocity reaches speedExplodeThreshold, contact with any object
    /// detonates; shared by both modes.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(CircleCollider2D))]
    public class Bomb : ItemSuper, ICarriable
    {
        public enum BombMode { Normal, Hanging }

        private enum BombPhase { Idle, Held, Fuse }

        [Header("Mode")]
        [SerializeField] private BombMode mode = BombMode.Normal;

        [Header("Bomb setting")]
        [SerializeField] private float fuseTime = 1.5f;         // fuse duration from spit-out to detonation
        [SerializeField] private float armTime = 0.3f;          // immunity window after spit-out: touching the player only pushes him away
        [SerializeField] private float blastRadius = 2f;
        [SerializeField] private float blastForce = 18f;
        [SerializeField] private float chainDelay = 0.1f;       // how long after being caught in another blast before detonating (0 = same-frame chain)
        [SerializeField] private LayerMask blastMask;

        [Header("Animation")]
        [SerializeField] private Sprite[] triggerFrames;        // fuse frames, 0 = normal, last = about to detonate
        [SerializeField] private float proximityRadius = 3.5f;  // proximity warning radius, independent of blastRadius

        [Header("Break setting")]
        [SerializeField] private FragmentCue breakCue;          // what it shatters into is all written in this asset

        [Header("Hanging mode")]
        [Tooltip("Editor only: Generate Hanging Points generates this many hanging points")]
        [SerializeField] private int hangingPointCount = 1;
        [SerializeField] private Vector2 hangingPointSpacing = new Vector2(0.6f, 0f);
        [SerializeField] private float hangingPointHeight = 3f;
        [SerializeField] private Chain.Settings chainSettings = new Chain.Settings();

        [Header("Speed explode")]
        [Tooltip("Once velocity reaches this threshold, contact with any object detonates; <= 0 disables")]
        [SerializeField] private float speedExplodeThreshold = 0f;

        private BombPhase phase = BombPhase.Idle;
        private bool exploded = false;      // when the player is touching on the fuse-expiry frame, the physics callback and Update would each detonate once — guards re-entry
        private Rigidbody2D body;
        private CircleCollider2D hitBox;
        private Timer FuseTimer;
        private Timer ArmTimer;

        private readonly List<Chain> chains = new List<Chain>();    // chains generated in hanging mode, destroyed with the bomb subtree

        private int frameIndex = 1;        // -1 = never set, guaranteeing the first frame is always written
        private float fuseDuration;         // this fuse's total duration (spit = fuseTime, chain = chainDelay)
        private bool snapToLast;            // chain-triggered: too short to play the animation, jump straight to the last frame

        // All bombs share one player reference. After the player is destroyed or the scene changes it
        // becomes a Unity fake-null and is re-looked-up on next use, so no ResetStatics needed like the buses
        private static Transform playerCache;

        private static Transform Player
        {
            get
            {
                if (playerCache == null)
                {
                    GameObject go = GameObject.FindGameObjectWithTag(Tags.Player);
                    playerCache = go != null ? go.transform : null;
                }
                return playerCache;
            }
        }

        protected override void Awake()
        {
            base.Awake();                       // ItemSuper caches the SpriteRenderer here
            body = GetComponent<Rigidbody2D>();
            hitBox = GetComponent<CircleCollider2D>();

            RefreshFrame();                     // settle the normal frame first, so the prefab's original sprite does not flash on the first frame

            if (mode == BombMode.Hanging) BuildHangingMode();
        }

        // Hanging mode: generates one physical chain per hanging point. The anchor = the hanging
        // point's initial world position, fixed from then on — so even though the hanging points are
        // bomb children, moving the bomb does not move the anchors
        private void BuildHangingMode()
        {
            HangingPoint[] pts = GetComponentsInChildren<HangingPoint>(true);
            if (pts.Length == 0)
            {
                Debug.LogWarning($"{name} is in Hanging mode but has no hanging points; right-click in the Inspector and run Generate Hanging Points", this);
                return;
            }

            chains.Clear();
            foreach (HangingPoint pt in pts)
            {
                GameObject chainGo = new GameObject($"Chain_{pt.name}");
                chainGo.transform.SetParent(pt.transform, false);

                Chain chain = chainGo.AddComponent<Chain>();
                chain.Configure(chainSettings);
                chain.Init(body, pt.transform.position);
                chains.Add(chain);
            }
        }

        void OnEnable()
        {
            ItemBus.ItemReleased += OnItemReleased;
            HazardBus.Exploded += OnChainExploded;
        }

        void OnDisable()
        {
            ItemBus.ItemReleased -= OnItemReleased;
            HazardBus.Exploded -= OnChainExploded;
        }

        void Update()
        {
            if (phase == BombPhase.Fuse && !FuseTimer.IsRunning) Explode();
            RefreshFrame();
        }

        /// <summary>
        /// Derives the frame to show each frame, raising Ticked once on change. During the fuse phase
        /// the frame order is derived directly from FuseTimer — the same clock that triggers the
        /// detonation, so "animation finished" and "detonate" always happen simultaneously; no two
        /// clocks to align.
        /// </summary>
        private void RefreshFrame()
        {
            if (triggerFrames == null || triggerFrames.Length == 0) return;
            if (phase == BombPhase.Held) return;        // in the mouth: invisible, must stay silent too

            int n = triggerFrames.Length;
            int index;

            if (phase == BombPhase.Fuse)
            {
                index = snapToLast || fuseDuration <= 0f
                    ? n - 1
                    : Mathf.Min((int)((1f - Mathf.Clamp01(FuseTimer.Remaining / fuseDuration)) * n), n - 1);
            }
            else                                        // Idle: the closer the player, the later the frame
            {
                Transform p = Player;
                if (p == null || proximityRadius <= 0f)
                {
                    index = 0;
                }
                else
                {
                    float d = Vector2.Distance(transform.position, p.position);
                    index = Mathf.Min((int)((1f - Mathf.Clamp01(d / proximityRadius)) * n), n - 1);
                }
            }

            if (index == frameIndex) return;

            // Sound only in the "getting tenser" direction: when the player jitters across a frame
            // boundary, the retreating half stays silent — with SoundCue.cooldown this suffices to
            // suppress jitter; no extra hysteresis needed
            bool advanced = index > frameIndex;
            frameIndex = index;
            SetSprite(triggerFrames[index]);

            if (advanced) HazardBus.RaiseTicked(transform.position, index, n);
        }

        // Stay matters too: staying in contact does not re-raise Enter, so the moment the immunity
        // window ends while still touching must detonate
        void OnCollisionEnter2D(Collision2D collision)
        {
            if (CheckSpeedExplode(collision)) return;
            HandlePlayerContact(collision);
        }

        void OnCollisionStay2D(Collision2D collision)
        {
            if (CheckSpeedExplode(collision)) return;
            HandlePlayerContact(collision);
        }

        // Speed detonation: once velocity is past the threshold, contact with anything detonates —
        // regardless of the object and without the player's immunity window.
        // Returns true = already detonated, the caller skips the player-contact path
        private bool CheckSpeedExplode(Collision2D collision)
        {
            if (phase == BombPhase.Held) return false;          // in the mouth: collider already off, defensive intercept
            if (speedExplodeThreshold <= 0f) return false;
            if (body.linearVelocity.magnitude < speedExplodeThreshold) return false;

            Explode();
            return true;
        }

        private void HandlePlayerContact(Collision2D collision)
        {
            if (!collision.gameObject.CompareTag(Tags.Player)) return;

            switch (phase)
            {
                case BombPhase.Held:                    // already in the player's mouth, cannot collide
                    return;

                case BombPhase.Fuse:                    // spit out: contact past the immunity window detonates
                    if (collision.gameObject.CompareTag(Tags.BreakAble)) Explode();
                    if (!ArmTimer.IsRunning) Explode();
                    return;

                default:                                // Idle: contact detonates.
                    // dash (Shift) no longer swallows bombs — only the rope gun's TrySwallowByRope can.
                    // After being marked by the rope gun (ropeGrappled), contact while the player is
                    // reeled in swallows instead of detonating
                    if (ropeGrappled && EatAble && ItemBus.Held == null)
                        Swallow(collision.gameObject.transform);
                    else
                        Explode();
                    return;
            }
        }


        // Rope-gun grapple mark: after being hit, the player is reeled in; contact during that window
        // must swallow, never detonate
        private bool ropeGrappled;

        public void MarkRopeGrappled() => ropeGrappled = true;
        public void ClearRopeGrappled() => ropeGrappled = false;

        /// <summary>Swallow entry when the rope gun reels the player to the mouth. Returns true on success.</summary>
        public bool TrySwallowByRope(Transform player)
        {
            if (phase != BombPhase.Idle) return false;
            if (!EatAble) return false;
            if (ItemBus.Held != null) return false;

            Swallow(player);
            return true;
        }

        // Swallow: no destruction, only disables physics + hides, parents to the player, waiting to
        // be spit out. Note: must NOT SetActive(false), or OnDisable unsubscribes the bus and the
        // "released" event would never arrive
        private void Swallow(Transform player)
        {
            phase = BombPhase.Held;
            ropeGrappled = false;
            body.simulated = false;
            hitBox.enabled = false;
            SetVisible(false);
            transform.SetParent(player, false);
            transform.localPosition = Vector3.zero;

            // A bomb in hanging mode being swallowed: all chains severed and hidden. After being spit
            // out / dropped on death it is a free bomb — the hanging state is unrecoverable, by design
            foreach (Chain c in chains) c.CutAll();

            ItemBus.RaiseItemEaten(this);
        }

        /// <summary>
        /// Dropped back into the world when the player dies (Idle phase, no fuse): restored from Held
        /// to a normal bomb. Unlike OnItemReleased, a death-drop bomb does not start a countdown —
        /// it just sits there waiting to be picked up again.
        /// </summary>
        public override void DropAt(Vector2 pos)
        {
            phase = BombPhase.Idle;
            transform.SetParent(null);
            SetVisible(true);
            hitBox.enabled = true;
            body.simulated = true;

            // The project sets m_AutoSyncTransforms = 0: transform and rigidbody positions do not sync,
            // write both
            transform.position = pos;
            body.position = pos;
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;

            exploded = false;       // this instance never detonated, reset just in case
            frameIndex = -1;        // force the normal frame to rewrite next frame — it is in Idle
            // phase now, must not sit on the fuse's last frame
            RefreshFrame();
        }

        // Spit out: back into the world, given initial velocity, fuse lit
        private void OnItemReleased(ICarriable item, Vector2 pos, Vector2 velocity)
        {
            if (item != (ICarriable)this) return;        // not the one being spit

            phase = BombPhase.Fuse;
            transform.SetParent(null);
            SetVisible(true);
            hitBox.enabled = true;
            body.simulated = true;

            // The project sets m_AutoSyncTransforms = 0: transform and rigidbody positions do not sync,
            // write both
            transform.position = pos;
            body.position = pos;
            body.linearVelocity = velocity;
            body.angularVelocity = 0f;

            FuseTimer.Set(fuseTime);
            ArmTimer.Set(armTime);

            fuseDuration = fuseTime;    // animation spreads across this duration; the last frame
            // lighting up is the detonation moment
            snapToLast = false;
        }

        // Called by HazardBus when another bomb's blast affects this one: detonates after chainDelay.
        // Not an immediate Explode() — converted to the Fuse phase to reuse the existing fuse check in
        // Update(), so a row of bombs detonates in sequence instead of all in one frame, and the
        // recursion depth is bounded (A.Explode directly calling B.Explode calling C.Explode...).
        private void OnChainExploded(GameObject victim, Vector2 center, float force)
        {
            if (victim != gameObject) return;       // not the one affected
            if (exploded) return;                   // already detonating myself
            if (phase == BombPhase.Held) return;    // the one in the player's mouth does not chain

            // When already on a fuse and faster than the chain delay, do not slow it down:
            // the fuse only shortens, never extends
            if (phase == BombPhase.Fuse && FuseTimer.Remaining <= chainDelay) return;

            phase = BombPhase.Fuse;
            FuseTimer.Set(chainDelay);
            // The immunity window exactly covers the chain fuse: otherwise OnCollisionStay2D while the
            // player is still touching detonates it on the spot, skipping chainDelay and breaking the
            // cascade rhythm — and the player is often still in contact, just blasted away by the last one
            ArmTimer.Set(chainDelay);

            fuseDuration = chainDelay;
            snapToLast = true;          // 0.1s cannot fit a whole animation; jump straight to the about-to-detonate frame
        }

        private void Explode()
        {
            // Destroy only applies at frame end and cannot stop a second call within the same frame;
            // without this, screen shake, knockback, and shards all come double
            if (exploded) return;
            exploded = true;

            Vector2 center = transform.position;

            Collider2D[] hits = Physics2D.OverlapCircleAll(center, blastRadius, blastMask);
            foreach (Collider2D h in hits)
            {
                if (h.gameObject == gameObject) continue;      // do not blast the self
                HazardBus.RaiseExploded(h.gameObject, center, blastForce);
            }

            playExplode();      // destroy the self regardless of what was hit (even nothing)
        }

        private void playExplode()
        {
            // Bounds must be captured before disabling the collider: once a Collider2D is disabled its
            // physics shape is removed and bounds degenerate to zero size at the origin
            Bounds bounds = hitBox.bounds;

            // Overall blast signal: exactly once per explosion; screen shake and such are driven by it
            // (Exploded cannot be used — it fires per victim inside the foreach, N victims = N raises)
            HazardBus.RaiseBlast(transform.position, blastRadius, blastForce);

            // Destroy only applies at frame end; the body still renders during that window, so without
            // hiding it the body and the shards overlap for one frame
            hitBox.enabled = false;
            SetVisible(false);

            // The bomb shatters into small pieces flung from the blast center — the same presentation
            // as breakable walls
            Shatter.Burst(breakCue, bounds, transform.position, blastForce);

            // Explosion sound is played by AudioDirector subscribing to the Blast above; this class does not touch audio
            Destroy(gameObject);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, blastRadius);

            // Cyan = proximity warning radius; the frame order starts advancing when the player enters.
            // When tuning, make it clearly larger than blastRadius
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, proximityRadius);

            if (mode == BombMode.Hanging)
            {
                // Blue dashed = chain positions from hanging points to the bomb, for dragging points in the Scene view
                HangingPoint[] pts = GetComponentsInChildren<HangingPoint>(true);
                Gizmos.color = new Color(0.2f, 0.7f, 1f, 0.6f);
                foreach (HangingPoint pt in pts)
                {
                    Gizmos.DrawLine(pt.transform.position, transform.position);
                }
            }

            // Yellow grid = how the shards are sliced, for tuning cellsX / cellsY in the Cue
            // hitBox cannot be used here: Awake never ran in the editor, the cache is still empty
            CircleCollider2D box = GetComponent<CircleCollider2D>();
            if (box == null || breakCue == null) return;

            Gizmos.color = Color.yellow;
            Shatter.DrawGrid(box.bounds, breakCue.cellsX, breakCue.cellsY);
        }

        // ---- Editor tools: generate/clear hanging points (works on scene instances and prefabs) ----

        [ContextMenu("Generate Hanging Points")]
        private void GenerateHangingPoints()
        {
            ClearHangingPoints();

            int n = Mathf.Max(1, hangingPointCount);
            for (int i = 0; i < n; i++)
            {
                GameObject go = new GameObject($"HangingPoint_{i + 1}");
                go.transform.SetParent(transform, false);
                go.AddComponent<HangingPoint>();

                // Evenly spaced horizontally around the bomb, raised to hangingPointHeight; drag them
                // in the Scene view afterwards
                float x = (i - (n - 1) * 0.5f) * hangingPointSpacing.x;
                go.transform.localPosition = new Vector3(x, hangingPointHeight, 0f);
            }
        }

        [ContextMenu("Clear Hanging Points")]
        private void ClearHangingPoints()
        {
            HangingPoint[] old = GetComponentsInChildren<HangingPoint>(true);
            foreach (HangingPoint o in old)
            {
                if (Application.isPlaying) Destroy(o.gameObject);
                else DestroyImmediate(o.gameObject);
            }
        }
    }
}
