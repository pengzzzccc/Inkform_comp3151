using System;
using System.Numerics;

namespace Inkform.Core
{
    /// <summary>
    /// Verlet 绳索解算器（纯数据类，不挂组件，引擎无关）：一串带距离约束的质点，
    /// 支持三种末端模式：
    /// ① 自由（无 body 无 pin，悬垂摆荡，如炸弹链被切断后的剩余段）；
    /// ② 钉在给定点上（endPin，只跟随不动它，如发射中绳索跟随子弹）；
    /// ③ 附在刚体上（body，与末端做零长约束、两侧各分一半修正，刚体那半
    ///    换算成速度写回 —— 能吊住刚体并随它摆荡，如炸弹悬挂链、绳索枪收绳）。
    ///
    /// 炸弹的悬挂链（Chain）和绳索枪共用这一份解算，各自只保留自己的
    /// 切断 / 渲染 / 事件逻辑。不持有任何引擎生命周期。
    ///
    /// 与旧版（UnityEngine 依赖版）逐行对应；重力与碰撞检测经 Physics 端口注入
    /// （未注入端口时 SolveFixed 抛异常 —— 漏接线要失败得响亮，而不是静默改手感）。
    /// </summary>
    public class VerletRope
    {
        /// <summary>度数→弧度常量（与 Unity Mathf.Deg2Rad 同值，保证换算一致）。</summary>
        public const float Deg2Rad = 0.017453292f;

        [Serializable]
        public struct Settings
        {
            public float segmentLength;         // 单段链长（世界单位）
            public int maxSegments;             // 段数上限
            public int solverIterations;
            public float gravityScale;          // 乘在注入重力上
            public float damping;
            public float attachStiffness;       // 附刚体时拉力换算成速度的比例
            public float attachDamping;         // 每帧扣掉的沿绳（径向）速度比例
            public float maxCorrectionSpeed;    // 单帧速度修正上限，防求解崩溃炸飞
            public LayerKey collisionMask;      // 段与地形碰撞的层，0 = 不碰撞
            public float collisionRadius;       // 段碰撞的圆半径（≈绳宽/2）

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
                collisionMask = default,
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

        /// <summary>物理端口：重力与段碰撞检测的唯一来源。必须在首次 SolveFixed 前注入。</summary>
        public IPhysicsPort Physics { get; set; }

        /// <summary>换配置（如收绳阶段临时调大 maxCorrectionSpeed）而不重建链段。之后正常 SolveFixed。</summary>
        public void Configure(in Settings settings) => cfg = settings;

        /// <summary>按锚点到末端位置的距离生成链段。之后每帧调 SolveFixed 推进。
        /// 数组按 maxSegments 预分配，段数怎么变都不产生垃圾。</summary>
        public void Init(in Settings settings, Vector2 anchor, Vector2 endPos)
        {
            cfg = settings;

            float dist = (anchor - endPos).Length();
            segmentCount = Clamp(
                (int)Math.Ceiling(dist / Math.Max(0.01f, cfg.segmentLength)),
                1, cfg.maxSegments);

            EnsureCapacity();

            for (int i = 0; i <= segmentCount; i++)
            {
                // 初始直接拉成直线：第一帧就绷紧，不会开场猛拽一下
                float t = (float)i / segmentCount;
                points[i] = anchor + (endPos - anchor) * t;
                prev[i] = points[i];
            }
        }

        /// <summary>把绳长设为目标值（可放长可缩短）。放长：新段从当前末端叠起，由后续约束自然撑开；
        /// 缩短：段数下调但末端点留在原处，整条绳因此比静止长度长，由后续约束迭代逐帧收紧。
        /// 数组预分配，段数怎么变都不产生垃圾。</summary>
        public void SetLength(float targetLength)
        {
            int wanted = Clamp(
                (int)Math.Ceiling(targetLength / Math.Max(0.01f, cfg.segmentLength)),
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
                // 缩短：把末端点连同它的速度接到原末端上，末端不瞬移 —— 直接下调 segmentCount
                // 会让新末端（原来的中间点）比刚体近一整段，附着约束当帧就吃到 segmentLength
                // 量级的瞬时误差，把刚体踢一脚。接过去之后整条绳比静止长度长，
                // 由后续约束迭代逐帧收紧，刚体是被平滑拉过来的。
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

        /// <summary>按角度（度）旋转向量。附刚体模式的挂点偏移是刚体局部坐标，
        /// 必须按当前 rotation 换算回世界，否则墙一转链就挂错位置。</summary>
        public static Vector2 Rotate(Vector2 v, float angleDeg)
        {
            float rad = angleDeg * Deg2Rad;
            float cos = (float)Math.Cos(rad);
            float sin = (float)Math.Sin(rad);
            return new Vector2(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);
        }

        /// <summary>
        /// 推进一步。anchor = 世界固定锚点（钉死不动的那端）。
        /// solveCount = 参与解算的末端点下标（Chain 切断后传 intactEnd，完整链传 SegmentCount）。
        /// body 非 null = 附刚体模式；endPin 非 null = 钉点模式；两者都 null = 自由悬垂。
        /// bodyOffset = 挂点相对刚体位置的局部偏移（随刚体旋转换算成世界，默认零 = 挂在质心）。
        /// </summary>
        public void SolveFixed(float dt, Vector2 anchor, int solveCount,
                               IAttachedBody body, Vector2? endPin, Vector2 bodyOffset = default)
        {
            if (solveCount <= 0) return;

            if (Physics == null)
                throw new InvalidOperationException("VerletRope.Physics 未注入端口（Shell 接线方须在 Init 前设置）");

            bool attached = body != null && solveCount == segmentCount && endPin == null;

            Vector2 g = Physics.Gravity * cfg.gravityScale * dt * dt;

            // 1. Verlet 积分：带阻尼，锚点端速度归零防漂移
            for (int i = 0; i <= solveCount; i++)
            {
                Vector2 p = points[i];
                points[i] = p + (p - prev[i]) * cfg.damping + g;
                prev[i] = p;
            }
            points[0] = anchor;
            prev[0] = anchor;

            Vector2 bodyPos = attached ? body.Position + Rotate(bodyOffset, body.Rotation) : Vector2.Zero;
            Vector2 solverBody = bodyPos;

            // 2. 距离约束迭代：链段间定长；附刚体时末端与刚体间零长（各分一半修正）
            for (int iter = 0; iter < cfg.solverIterations; iter++)
            {
                for (int i = 0; i < solveCount; i++)
                {
                    Vector2 a = points[i];
                    Vector2 b = points[i + 1];
                    Vector2 d = b - a;
                    float dist = d.Length();
                    if (dist < 1e-5f) continue;
                    float err = (dist - cfg.segmentLength) / dist * 0.5f;
                    points[i] = a + d * err;
                    points[i + 1] = b - d * err;
                }

                if (attached)
                {
                    Vector2 d = points[solveCount] - solverBody;
                    float dist = d.Length();
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

            // 3. 段与地形碰撞：逐段 CircleCast，段被地形截住时把末端钳到接触点前。
            //    绳子因此沿地形边缘缠绕/搭住，不再穿墙。末端是钉点/刚体时跳过最后一段
            //    （末端必须连住钩子/刚体），钳制后重钉末端。
            if (cfg.collisionMask.Mask != 0)
            {
                bool endFixed = attached || endPin != null;
                int clampTo = endFixed ? solveCount - 1 : solveCount;

                for (int i = 0; i < clampTo; i++)
                {
                    Vector2 a = points[i];
                    Vector2 b = points[i + 1];
                    Vector2 dir = b - a;
                    float dist = dir.Length();
                    if (dist < 1e-5f) continue;

                    bool hit = Physics.CastCircle(
                        a, cfg.collisionRadius, dir / dist, dist, cfg.collisionMask, out float hitDistance);

                    // distance ≈ 0 = 起点已经埋在碰撞体里（工程的 QueriesStartInColliders 是开的，
                    // 而悬挂锚点就钉在地形表面上）。这种命中不能钳：stopDist 会算成 0，
                    // 把 points[i+1] 拍到 points[i] 上，下一段又从同一个点起测 —— 整条绳级联
                    // 塌缩到锚点，下一帧 Verlet 读到巨大的 p−prev 就炸开，附着刚体被甩飞。
                    if (hit && hitDistance > cfg.collisionRadius)
                    {
                        // 钳到接触点前，留出半径余量
                        float stopDist = hitDistance - cfg.collisionRadius;
                        points[i + 1] = a + dir / dist * stopDist;
                    }
                }

                if (endPin != null) points[solveCount] = endPin.Value;  // 钳制后重钉末端
            }

            // 4. 刚体那半修正换算成速度：拉力、重力在解里自然平衡
            if (attached)
            {
                Vector2 delta = (solverBody - bodyPos) * cfg.attachStiffness;
                float maxC = cfg.maxCorrectionSpeed * dt;
                if (delta.LengthSquared() > maxC * maxC) delta = delta / delta.Length() * maxC;
                body.AddVelocity(delta / dt);

                // 上面那句是「只加不减」的：误差每帧换成一份速度叠上去，刚体自己的速度没人扣，
                // 于是绳子每收紧一次就往系统里泵一次能量，几帧就能把刚体甩到几十 m/s。
                // 绳不可伸长 —— 沿绳方向的速度本就该被绳子吃掉，按 attachDamping 扣一部分。
                // 取挂点（而非质心）到锚点的方向：悬挂本质是单摆，挂点才是真正的摆半径，
                // 扣径向、留切向 = 绳子绷得住又照样能荡。
                if (cfg.attachDamping > 0f)
                {
                    Vector2 radialDir = bodyPos - anchor;
                    float len = radialDir.Length();
                    if (len > 1e-5f)
                    {
                        radialDir /= len;
                        float radial = Vector2.Dot(body.Velocity, radialDir);
                        body.AddVelocity(-(radialDir * (radial * cfg.attachDamping)));
                    }
                }
            }
        }

        private static float Clamp(float v, float min, float max)
            => v < min ? min : (v > max ? max : v);
    }
}
