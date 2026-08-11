using Inkform.Bus;
using Inkform.Tool;
using System.Collections.Generic;
using UnityEngine;

namespace Inkform.Item
{
    /// <summary>
    /// Verlet rope chain: a severable chain connecting a bomb to its hanging anchor (the hanging
    /// point's world coordinate). The solver body lives in VerletRope (shared with the rope gun);
    /// this class keeps only:
    /// ① severing (CutAt / CutAll);
    /// ② severing paths: blast waves (HazardBus.Blast) / rope-gun flight hits (the static registry is
    ///    probed segment by segment);
    /// ③ LineRenderer rendering.
    /// Note: dash is now pure dash, the Eat attack state was removed — player-attack severing is gone.
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public class Chain : MonoBehaviour
    {
        /// <summary>
        /// Chain tuning pack. Configured by Bomb in the Inspector and pushed to chains created at
        /// runtime via Configure, so chain components never need to be pre-placed in the scene.
        /// </summary>
        [System.Serializable]
        public class Settings
        {
            public VerletRope.Settings solver = VerletRope.Settings.Default();

            [Header("Render")]
            public float lineWidth = 0.08f;
            public int sortingOrder = -1;               // default draws behind the bomb
        }

        private readonly VerletRope rope = new VerletRope();
        private Rigidbody2D body;       // attached bomb rigidbody at the end
        private Vector2 anchor;         // world-fixed anchor: the hanging point's initial world position, does not move with the bomb
        private Vector2 attachOffset;   // attach offset relative to the body position (rotates with the body), zero = center of mass
        private LineRenderer line;

        private Settings cfg = new Settings();
        private int segmentCount;       // total segment count
        private int intactEnd;          // index of the last still-intact endpoint: segmentCount = intact, 0 = fully severed

        // Registry of all living chains: maintained via OnEnable/OnDisable; the rope gun probes it
        // while flying to sever chains
        private static readonly List<Chain> active = new List<Chain>();
        public static IReadOnlyList<Chain> Active => active;

        void Awake()
        {
            line = GetComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.alignment = LineAlignment.TransformZ;
            line.loop = false;
            line.positionCount = 0;
            line.enabled = false;
        }

        void OnEnable()
        {
            active.Add(this);
            HazardBus.Blast += OnBlast;
        }

        void OnDisable()
        {
            active.Remove(this);
            HazardBus.Blast -= OnBlast;
        }

        public bool IsIntact => intactEnd >= segmentCount;

        public void Configure(Settings s)
        {
            if (s != null) cfg = s;
        }

        /// <summary>Swaps material. null = keep default; callable before or after Init (material is independent of geometry).</summary>
        public void SetMaterial(Material material)
        {
            if (material != null && line != null) line.material = material;
        }

        /// <summary>
        /// Binds the hanging object's rigidbody and anchor, generating segments by distance. Must be
        /// called immediately after AddComponent.
        /// attachOffset = attach offset relative to the body position (e.g. a wall's right edge), zero = center of mass.
        /// </summary>
        public void Init(Rigidbody2D body, Vector2 anchorPos, Vector2 attachOffset = default)
        {
            this.body = body;
            anchor = anchorPos;
            this.attachOffset = attachOffset;

            rope.Init(cfg.solver, anchor, body.position + VerletRope.Rotate(attachOffset, body.rotation));
            segmentCount = rope.SegmentCount;
            intactEnd = segmentCount;

            line.widthMultiplier = cfg.lineWidth;
            line.sortingOrder = cfg.sortingOrder;
            line.positionCount = segmentCount + 1;
            line.enabled = true;
        }

        /// <summary>Severs segment segmentIndex and everything below (0 = the topmost segment).</summary>
        public void CutAt(int segmentIndex)
        {
            if (intactEnd <= 0) return;
            intactEnd = Mathf.Min(segmentIndex, intactEnd);
            if (intactEnd <= 0) line.enabled = false;
        }

        /// <summary>Severs everything: the bomb is free of all constraints, the chain disappears as a whole.</summary>
        public void CutAll()
        {
            intactEnd = 0;
            line.enabled = false;
        }

        /// <summary>Index of the intact segment nearest to point; -1 when farther than maxDist. Used by the rope gun for severing.</summary>
        public int NearestSegment(Vector2 point, float maxDist)
        {
            if (!IsIntact) return -1;

            float bestSq = maxDist * maxDist;
            int best = -1;
            for (int i = 0; i < segmentCount; i++)
            {
                Vector2 a = rope.GetPoint(i);
                Vector2 b = rope.GetPoint(i + 1);
                Vector2 ab = b - a;
                Vector2 ap = point - a;
                float lenSq = ab.sqrMagnitude;
                float t = lenSq > 1e-8f ? Mathf.Clamp01(Vector2.Dot(ap, ab) / lenSq) : 0f;
                float d2 = (a + ab * t - point).sqrMagnitude;
                if (d2 <= bestSq)
                {
                    bestSq = d2;
                    best = i;
                }
            }
            return best;
        }

        void FixedUpdate()
        {
            if (intactEnd <= 0) return;

            rope.SolveFixed(Time.fixedDeltaTime, anchor, intactEnd,
                            IsIntact ? body : null, null, attachOffset);
        }

        // Blast wave: severs when the blast center is within the blast radius of a segment's midpoint,
        // cutting at the segment nearest the center
        private void OnBlast(Vector2 center, float radius, float force)
        {
            if (!IsIntact) return;

            float bestSq = radius * radius;
            int best = -1;
            for (int i = 0; i < segmentCount; i++)
            {
                Vector2 mid = (rope.GetPoint(i) + rope.GetPoint(i + 1)) * 0.5f;
                float d2 = (mid - center).sqrMagnitude;
                if (d2 <= bestSq && (best < 0 || d2 < bestSq))
                {
                    bestSq = d2;
                    best = i;
                }
            }
            if (best >= 0) CutAt(best);
        }

        // Rendering in LateUpdate: positions were updated by FixedUpdate, drawing now does not jitter
        void LateUpdate()
        {
            if (!line.enabled || intactEnd <= 0) return;
            line.positionCount = intactEnd + 1;
            for (int i = 0; i <= intactEnd; i++)
                line.SetPosition(i, rope.GetPoint(i));
        }
    }
}
