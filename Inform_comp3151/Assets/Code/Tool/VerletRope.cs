using UnityEngine;

namespace Inkform.Tool
{
    /// <summary>
    /// Verlet 绳索解算器（纯数据类，不挂组件）：一串带距离约束的质点，
    /// 支持三种末端模式：
    /// ① 自由（无 body 无 pin，悬垂摆荡，如炸弹链被切断后的剩余段）；
    /// ② 钉在给定点上（endPin，只跟随不动它，如发射中绳索跟随子弹）；
    /// ③ 附在刚体上（body，与末端做零长约束、两侧各分一半修正，刚体那半
    ///    换算成速度写回 —— 能吊住刚体并随它摆荡，如炸弹悬挂链、绳索枪收绳）。
    ///
    /// 炸弹的悬挂链（Chain）和绳索枪共用这一份解算，各自只保留自己的
    /// 切断 / 渲染 / 事件逻辑。不持有任何 Unity 生命周期。
    /// </summary>
    public class VerletRope
    {
        [System.Serializable]
        public struct Settings
        {
            public float segmentLength;         // 单段链长（世界单位）
            public int maxSegments;             // 段数上限
            [Range(1, 16)] public int solverIterations;
            public float gravityScale;          // 乘在 Physics2D.gravity 上
            [Range(0.8f, 1f)] public float damping;
            [Range(0f, 1f)] public float attachStiffness;   // 附刚体时拉力换算成速度的比例
            public float maxCorrectionSpeed;    // 单帧速度修正上限，防求解崩溃炸飞

            public static Settings Default() => new Settings
            {
                segmentLength = 0.25f,
                maxSegments = 32,
                solverIterations = 6,
                gravityScale = 1f,
                damping = 0.99f,
                attachStiffness = 0.6f,
                maxCorrectionSpeed = 20f,
            };
        }

        private Settings cfg;
        private Vector2[] points;
        private Vector2[] prev;
        private int segmentCount;

        public int SegmentCount => segmentCount;

        public Vector2 GetPoint(int i) => points[i];
        public Vector2 EndPoint => points[segmentCount];

        /// <summary>按锚点到末端位置的距离生成链段。之后每帧调 SolveFixed 推进。
        /// 段数变化（绳索枪随飞行重新 Init）时会复用已有数组，只在容量不够时扩容 —— 飞行中反复 Init 不产生垃圾。</summary>
        public void Init(in Settings settings, Vector2 anchor, Vector2 endPos)
        {
            cfg = settings;

            float dist = Vector2.Distance(anchor, endPos);
            int wanted = Mathf.Clamp(
                Mathf.CeilToInt(dist / Mathf.Max(0.01f, cfg.segmentLength)),
                1, cfg.maxSegments);

            if (wanted != segmentCount)
            {
                segmentCount = wanted;
                if (points == null || points.Length < segmentCount + 1)
                {
                    points = new Vector2[segmentCount + 1];
                    prev = new Vector2[segmentCount + 1];
                }
            }

            for (int i = 0; i <= segmentCount; i++)
            {
                // 初始直接拉成直线：第一帧就绷紧，不会开场猛拽一下
                points[i] = Vector2.Lerp(anchor, endPos, (float)i / segmentCount);
                prev[i] = points[i];
            }
        }

        /// <summary>
        /// 推进一步。anchor = 世界固定锚点（每帧更新也没关系，钉死不动的那端）。
        /// solveCount = 参与解算的末端点下标（Chain 切断后传 intactEnd，完整链传 SegmentCount）。
        /// body 非 null = 附刚体模式；endPin 非 null = 钉点模式；两者都 null = 自由悬垂。
        /// </summary>
        public void SolveFixed(float dt, Vector2 anchor, int solveCount,
                               Rigidbody2D body, Vector2? endPin)
        {
            if (solveCount <= 0) return;

            bool attached = body != null && solveCount == segmentCount && endPin == null;

            Vector2 g = Physics2D.gravity * cfg.gravityScale * dt * dt;

            // 1. Verlet 积分：带阻尼，锚点端速度归零防漂移
            for (int i = 0; i <= solveCount; i++)
            {
                Vector2 p = points[i];
                points[i] = p + (p - prev[i]) * cfg.damping + g;
                prev[i] = p;
            }
            points[0] = anchor;
            prev[0] = anchor;

            Vector2 bodyPos = attached ? body.position : Vector2.zero;
            Vector2 solverBody = bodyPos;

            // 2. 距离约束迭代：链段间定长；附刚体时末端与刚体间零长（各分一半修正）
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
                    points[solveCount] = endPin.Value;      // 钉点：每轮重钉，保证不漂
                }

                points[0] = anchor;
            }

            // 3. 刚体那半修正换算成速度：拉力、重力在解里自然平衡
            if (attached)
            {
                Vector2 delta = (solverBody - bodyPos) * cfg.attachStiffness;
                float maxC = cfg.maxCorrectionSpeed * dt;
                if (delta.sqrMagnitude > maxC * maxC) delta = delta.normalized * maxC;
                body.linearVelocity += delta / dt;
            }
        }
    }
}
