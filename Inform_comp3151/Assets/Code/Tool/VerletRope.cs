using UnityEngine;

namespace Inkform.Tool
{
    /// <summary>
    /// Verlet rope solver (pure data class, no component): a chain of distance-constrained
    /// particles with three end modes:
    /// ① Free (no body, no pin — dangling/swinging, e.g. the remaining segment of a cut chain);
    /// ② Pinned to a point (endPin, follows it without moving it, e.g. an in-flight rope following a bullet);
    /// ③ Attached to a rigidbody (body, zero-length constraint to the end, correction split half/half —
    ///    the rigidbody half is written back as velocity — can hold a body up and swing with it,
    ///    e.g. bomb hanging chains).
    ///
    /// Hanging chains (Chain) use this solver; the rope gun does not — it draws a straight line.
    /// Each caller keeps only its own cutting / rendering / event logic. Holds no Unity lifecycle.
    /// </summary>
    public class VerletRope
    {
        [System.Serializable]
        public struct Settings
        {
            public float segmentLength;         // length of one segment (world units)
            public int maxSegments;             // segment count cap
            [Range(1, 16)] public int solverIterations;
            public float gravityScale;          // multiplier on Physics2D.gravity
            [Range(0.8f, 1f)] public float damping;
            [Range(0f, 1f)] public float attachStiffness;   // fraction of pull force converted to velocity when attached
            [Range(0f, 1f)] public float attachDamping;     // fraction of along-rope (radial) velocity removed each frame
            public float maxCorrectionSpeed;    // per-frame velocity correction cap, prevents solver blow-ups
            public LayerMask collisionMask;     // layers for segment-vs-terrain collision, 0 = none (bomb chains keep it off)
            public float collisionRadius;       // segment collision circle radius (≈ rope width / 2)

            public static Settings Default() => new Settings
            {
                segmentLength = 0.25f,
                maxSegments = 32,
                solverIterations = 6,
                gravityScale = 1f,
                damping = 0.99f,
                attachStiffness = 0.6f,
                attachDamping = 0.25f,
                maxCorrectionSpeed = 20f,
                collisionMask = 0,
                collisionRadius = 0.04f,
            };
        }

        private Settings cfg;
        private Vector2[] points;
        private Vector2[] prev;
        private int segmentCount;

        public int SegmentCount => segmentCount;

        public Vector2 GetPoint(int i) => points[i];
        public Vector2 EndPoint => points[segmentCount];

        /// <summary>Swaps settings (e.g. temporarily raising maxCorrectionSpeed while reeling in) without rebuilding segments. Call SolveFixed normally afterwards.</summary>
        public void Configure(in Settings settings) => cfg = settings;

        /// <summary>Builds segments from the distance between anchor and end position. Call SolveFixed each frame afterwards.
        /// Arrays are preallocated at maxSegments so segment count changes allocate nothing.</summary>
        public void Init(in Settings settings, Vector2 anchor, Vector2 endPos)
        {
            cfg = settings;

            float dist = Vector2.Distance(anchor, endPos);
            segmentCount = Mathf.Clamp(
                Mathf.CeilToInt(dist / Mathf.Max(0.01f, cfg.segmentLength)),
                1, cfg.maxSegments);

            EnsureCapacity();

            for (int i = 0; i <= segmentCount; i++)
            {
                // Start stretched straight: taut from the first frame, no initial jerk
                points[i] = Vector2.Lerp(anchor, endPos, (float)i / segmentCount);
                prev[i] = points[i];
            }
        }

        /// <summary>Sets rope length to a target (longer or shorter). Longer: new segments stack from the current end,
        /// spread open by later constraints — rope feeds out of the muzzle continuously. Shorter: segment count drops
        /// but the end point stays, so the rope is longer than its rest length and later constraint iterations tighten
        /// it frame by frame, smoothly dragging an attached body toward the anchor — used by reeling and hanging lifts.
        /// Arrays are preallocated so segment count changes allocate nothing.</summary>
        public void SetLength(float targetLength)
        {
            int wanted = Mathf.Clamp(
                Mathf.CeilToInt(targetLength / Mathf.Max(0.01f, cfg.segmentLength)),
                1, cfg.maxSegments);
            if (wanted == segmentCount) return;

            if (wanted > segmentCount)
            {
                EnsureCapacity();

                Vector2 lastPos = points[segmentCount];
                for (int i = segmentCount + 1; i <= wanted; i++)
                {
                    points[i] = lastPos;
                    prev[i] = lastPos;
                }
            }
            else
            {
                // Shortening: splice the end point (and its velocity) onto the new end instead of
                // teleporting — simply lowering segmentCount makes the new end (an old middle point)
                // one full segment too close to the body, and the attach constraint eats a
                // segmentLength-scale instantaneous error that frame, kicking the body (once per
                // segment removed while reeling/pulling). After the splice the rope is longer than
                // its rest length and later constraint iterations tighten it gradually — the body
                // is pulled in smoothly.
                points[wanted] = points[segmentCount];
                prev[wanted] = prev[segmentCount];
            }
            segmentCount = wanted;
        }

        private void EnsureCapacity()
        {
            int capacity = cfg.maxSegments + 1;
            if (points == null || points.Length < capacity)
            {
                points = new Vector2[capacity];
                prev = new Vector2[capacity];
            }
        }

        /// <summary>Rotates a vector by an angle in degrees. The attach offset in attached mode is a rigidbody-local
        /// coordinate and must be transformed back to world space by the current rotation, or the chain
        /// attaches at the wrong spot once the wall rotates.</summary>
        public static Vector2 Rotate(Vector2 v, float angleDeg)
        {
            float rad = angleDeg * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad);
            float sin = Mathf.Sin(rad);
            return new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
        }

        /// <summary>
        /// Advances one step. anchor = world-fixed anchor (updating it every frame is fine; it is the pinned end).
        /// solveCount = index of the last endpoint to solve (Chain passes intactEnd after cutting, SegmentCount when intact).
        /// body != null = attached mode; endPin != null = pinned mode; both null = free hanging.
        /// bodyOffset = attach offset relative to the body position (rotated into world space with the body, default zero = center of mass).
        /// </summary>
        public void SolveFixed(float dt, Vector2 anchor, int solveCount,
                               Rigidbody2D body, Vector2? endPin, Vector2 bodyOffset = default)
        {
            if (solveCount <= 0) return;

            bool attached = body != null && solveCount == segmentCount && endPin == null;

            Vector2 g = Physics2D.gravity * cfg.gravityScale * dt * dt;

            // 1. Verlet integration with damping; anchor end velocity zeroed to prevent drift
            for (int i = 0; i <= solveCount; i++)
            {
                Vector2 p = points[i];
                points[i] = p + (p - prev[i]) * cfg.damping + g;
                prev[i] = p;
            }
            points[0] = anchor;
            prev[0] = anchor;

            Vector2 bodyPos = attached ? body.position + Rotate(bodyOffset, body.rotation) : Vector2.zero;
            Vector2 solverBody = bodyPos;

            // 2. Distance constraint iterations: fixed length between segments; zero-length between end
            //    and body when attached (correction split half/half)
            for (int iter = 0; iter < cfg.solverIterations; iter++)
            {
                for (int i = 0; i < solveCount; i++)
                {
                    Vector2 a = points[i];
                    Vector2 b = points[i + 1];
                    Vector2 d = b - a;
                    float dist = d.magnitude;
                    if (dist < 1e-5f) continue;
                    float err = (dist - cfg.segmentLength) / dist * 0.5f;
                    points[i] = a + d * err;
                    points[i + 1] = b - d * err;
                }

                if (attached)
                {
                    Vector2 d = points[solveCount] - solverBody;
                    float dist = d.magnitude;
                    if (dist > 1e-5f)
                    {
                        Vector2 half = d * 0.5f;
                        points[solveCount] -= half;
                        solverBody += half;
                    }
                }
                else if (endPin != null)
                {
                    points[solveCount] = endPin.Value;      // pinned: re-pin every iteration to prevent drift
                }

                points[0] = anchor;
            }

            // 3. Segment-vs-terrain collision: CircleCast each segment; when terrain intercepts a segment,
            //    clamp its end to just before the contact point. The rope then wraps/rests along terrain
            //    edges instead of passing through walls. Skip the last segment when the end is pinned/attached
            //    (the end must stay connected to the hook/body); re-pin the end after clamping.
            if (cfg.collisionMask != 0)
            {
                bool endFixed = attached || endPin != null;
                int clampTo = endFixed ? solveCount - 1 : solveCount;

                for (int i = 0; i < clampTo; i++)
                {
                    Vector2 a = points[i];
                    Vector2 b = points[i + 1];
                    Vector2 dir = b - a;
                    float dist = dir.magnitude;
                    if (dist < 1e-5f) continue;

                    RaycastHit2D hit = Physics2D.CircleCast(
                        a, cfg.collisionRadius, dir / dist, dist, cfg.collisionMask);

                    // distance ≈ 0 = the start is already buried in a collider (the project has
                    // QueriesStartInColliders on, and hanging anchors sit right on terrain surfaces).
                    // Such a hit must not be clamped: stopDist would compute to 0, smashing points[i+1]
                    // onto points[i], the next segment starts measuring from the same point — the whole
                    // rope cascades into collapse at the anchor, next frame Verlet reads a huge p−prev
                    // and blows up, flinging the attached body away.
                    if (hit.collider != null && hit.distance > cfg.collisionRadius)
                    {
                        // clamp to just before the contact point, leaving radius clearance
                        float stopDist = hit.distance - cfg.collisionRadius;
                        points[i + 1] = a + dir / dist * stopDist;
                    }
                }

                if (endPin != null) points[solveCount] = endPin.Value;  // re-pin the end after clamping
            }

            // 4. Convert the rigidbody half of the correction into velocity: pull and gravity
            //    balance out naturally in the solve
            if (attached)
            {
                Vector2 delta = (solverBody - bodyPos) * cfg.attachStiffness;
                float maxC = cfg.maxCorrectionSpeed * dt;
                if (delta.sqrMagnitude > maxC * maxC) delta = delta.normalized * maxC;
                body.linearVelocity += delta / dt;

                // The line above only adds, never subtracts: each frame the error becomes a slice of
                // velocity stacked on top, and nothing removes the body's own velocity — so every rope
                // tightening pumps energy into the system, flinging the body to tens of m/s in a few
                // frames. The rope cannot stretch — along-rope velocity is precisely what the rope eats,
                // so remove a fraction of it via attachDamping. Use the anchor→body direction rather than
                // the last segment: hanging is a pendulum, this is the true swing radius; removing radial
                // while keeping tangential = rope stays taut yet the body can still swing.
                if (cfg.attachDamping > 0f)
                {
                    // Direction from the attach point (not the center of mass) to the anchor:
                    // hanging is a pendulum, the attach point is the true swing radius
                    Vector2 radialDir = bodyPos - anchor;
                    float len = radialDir.magnitude;
                    if (len > 1e-5f)
                    {
                        radialDir /= len;
                        float radial = Vector2.Dot(body.linearVelocity, radialDir);
                        body.linearVelocity -= radialDir * (radial * cfg.attachDamping);
                    }
                }
            }
        }
    }
}
