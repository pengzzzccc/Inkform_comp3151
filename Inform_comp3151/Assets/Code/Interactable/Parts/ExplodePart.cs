using Inkform.Bus;
using Inkform.Fx;
using Inkform.Tool;
using UnityEngine;

namespace Inkform.Interactable.Parts
{
    /// <summary>
    /// Explosion core: defines "the blast itself" — radius, push, affected layers, shatter look, with
    /// an idempotent Explode() entry. Fully passive itself: subscribes to no bus, consumes no contact;
    /// trigger parts (ExplodeOnContact / ExplodeOnBlast / ExplodeAfterDelay / ExplodeOnImpact) call
    /// Explode() when their condition is met.
    ///
    /// Blast propagation runs entirely through HazardBus: RaiseExploded per victim (BreakablePart
    /// shatters, PlayerHandler gets knocked back, other explosives chain) + one RaiseBlast overall
    /// (screen shake / sound / rope cutting) — all listeners already on the bus, zero changes.
    ///
    /// Proximity warning animation: with triggerFrames + proximityRadius configured, the frame
    /// sequence advances as the player approaches (closer = later frame, 0 = normal), raising Ticked
    /// on change (AudioDirector plays the tick sound) — same origin as Bomb's proximity logic,
    /// distance-driven branch only.
    /// </summary>
    public class ExplodePart : MonoBehaviour, IInteractablePart
    {
        [Header("Blast")]
        [SerializeField] private float blastRadius = 2f;
        [SerializeField] private float blastForce = 18f;
        [SerializeField] private LayerMask blastMask;
        [Tooltip("What the shatter looks like, all written in this asset; null = silent, no fragments")]
        [SerializeField] private FragmentCue breakCue;

        [Header("Animation")]
        [Tooltip("Proximity warning frames, 0 = normal, later = closer to the player; null/empty = animation off")]
        [SerializeField] private Sprite[] triggerFrames;
        [Tooltip("Frame sequence starts advancing once the player is within this radius; <= 0 disables")]
        [SerializeField] private float proximityRadius = 0f;

        private Interactable root;
        private Collider2D body;
        private SpriteRenderer sprite;
        private bool exploded;      // idempotent: multiple triggers in one frame only detonate once

        // ---- Proximity warning animation state ----

        private int frameIndex = -1;        // -1 = never set, guaranteeing the first frame is always written
        private Sprite _current;            // SetSprite dedup: per-frame driving but most frames are the same sprite

        // All explosives share one player reference. After the player is destroyed or the scene
        // changes it becomes a Unity fake-null and is re-looked-up on next use, so no ResetStatics
        // needed (same as Bomb)
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

        public void Attach(Interactable root)
        {
            this.root = root;
            body = root.GetComponent<Collider2D>();
            sprite = root.GetComponent<SpriteRenderer>();
        }

        // Does not consume contact: trigger parts receive the dispatch normally
        public bool HandleContact(ContactPhase phase, Collider2D other) => false;

        void Update()
        {
            RefreshFrame();
        }

        /// <summary>
        /// Derives the frame to show each frame (proximity warning): closer player = later frame, raising
        /// Ticked on change. No frames while the renderer is disabled (swallowed / hidden / hidden by
        /// the explosion) — a bomb in the mouth must not emit warning ticks.
        /// </summary>
        private void RefreshFrame()
        {
            if (triggerFrames == null || triggerFrames.Length == 0) return;
            if (sprite == null || !sprite.enabled) return;

            int n = triggerFrames.Length;
            Transform p = Player;
            int index;

            if (p == null || proximityRadius <= 0f)
                index = 0;
            else
            {
                float d = Vector2.Distance(transform.position, p.position);
                index = Mathf.Min((int)((1f - Mathf.Clamp01(d / proximityRadius)) * n), n - 1);
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

        private void SetSprite(Sprite s)
        {
            if (_current == s) return;
            _current = s;
            if (sprite != null) sprite.sprite = s;
        }

        /// <summary>Detonate. Idempotent: takes effect exactly once; trigger parts may call freely.</summary>
        public void Explode()
        {
            if (exploded) return;
            exploded = true;

            // Bounds must be captured before disabling the collider: once a Collider2D is disabled its
            // physics shape is removed and bounds degenerate to zero size at the origin
            Bounds bounds = body != null ? body.bounds : new Bounds(root.transform.position, Vector3.one);
            Vector2 center = root.transform.position;

            // Per victim: every object inside the blast circle receives one Exploded
            Collider2D[] hits = Physics2D.OverlapCircleAll(center, blastRadius, blastMask);
            foreach (Collider2D h in hits)
            {
                if (h.gameObject == root.gameObject) continue;      // do not blast the self
                HazardBus.RaiseExploded(h.gameObject, center, blastForce);
            }

            // Overall blast signal: exactly once per explosion; screen shake and such are driven by it
            HazardBus.RaiseBlast(center, blastRadius, blastForce);

            // Restorable items are not destroyed — the part hides them and respawn brings them back.
            // Everything else hides before the shards render (Destroy only applies at frame end, so
            // without hiding the body overlaps the shards for one frame), then is destroyed outright
            // (same as Bomb)
            bool restorable = root.TryGetPart(out RestorablePart restore);

            if (restorable)
                restore.HideForRestore();
            else
            {
                if (body != null) body.enabled = false;
                foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            }

            // The shatter look plays for restorable items too: being hidden for restore instead of
            // destroyed is no reason to skip the visual
            Shatter.Burst(breakCue, bounds, center, blastForce);

            // Explosives are unrecoverable (same as Bomb): shattered and destroyed outright
            if (!restorable) Destroy(root.gameObject);
        }
    }
}
