using Inkform.Bus;
using Inkform.Tool;
using System.Collections.Generic;
using UnityEngine;

namespace Inkform.Item
{
    /// <summary>
    /// Verlet 绳索链：连接炸弹与悬挂锚点（悬挂点的世界坐标）的可切断链条。
    /// 解算本体在 VerletRope（与绳索枪共用），本类只保留：
    /// ① 切断（CutAt / CutAll）；
    /// ② 切断途径：爆炸（HazardBus.Blast）波及 / 绳索枪飞行命中（静态注册表逐段检测）；
    /// ③ LineRenderer 渲染。
    /// 注：dash 已是纯冲刺，不再有 Eat 攻击状态 —— 玩家攻击切断已被移除。
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public class Chain : MonoBehaviour
    {
        /// <summary>
        /// 链条调参包。由 Bomb 在 Inspector 里配好后经 Configure 推给运行时新建的链，
        /// 这样链的组件本身不需要在场景里预先摆放。
        /// </summary>
        [System.Serializable]
        public class Settings
        {
            public VerletRope.Settings solver = VerletRope.Settings.Default();

            [Header("Render")]
            public float lineWidth = 0.08f;
            public int sortingOrder = -1;               // 默认画在炸弹背后
        }

        private readonly VerletRope rope = new VerletRope();
        private Rigidbody2D body;       // 末端绑定的炸弹刚体
        private Vector2 anchor;         // 世界固定锚点：悬挂点的初始世界坐标，不随炸弹移动
        private LineRenderer line;

        private Settings cfg = new Settings();
        private int segmentCount;       // 链段总数
        private int intactEnd;          // 仍完整的末端点下标：segmentCount = 未断，0 = 全断

        // 所有在世链的注册表：OnEnable/OnDisable 维护，绳索枪飞行时据此做断链检测
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

        /// <summary>绑定炸弹刚体与锚点，按距离生成链段。必须在 AddComponent 之后立刻调用。</summary>
        public void Init(Rigidbody2D bombBody, Vector2 anchorPos)
        {
            body = bombBody;
            anchor = anchorPos;

            rope.Init(cfg.solver, anchor, body.position);
            segmentCount = rope.SegmentCount;
            intactEnd = segmentCount;

            line.widthMultiplier = cfg.lineWidth;
            line.sortingOrder = cfg.sortingOrder;
            line.positionCount = segmentCount + 1;
            line.enabled = true;
        }

        /// <summary>切断第 segmentIndex 段及以下的所有链段（0 = 最上面那段）。</summary>
        public void CutAt(int segmentIndex)
        {
            if (intactEnd <= 0) return;
            intactEnd = Mathf.Min(segmentIndex, intactEnd);
            if (intactEnd <= 0) line.enabled = false;
        }

        /// <summary>全部切断：炸弹不再受任何约束，链整体消失。</summary>
        public void CutAll()
        {
            intactEnd = 0;
            line.enabled = false;
        }

        /// <summary>离 point 最近的完整段下标；最近距离超过 maxDist 返回 -1。供绳索枪断链用。</summary>
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
                            IsIntact ? body : null, null);
        }

        // 爆炸波及：爆心到某段中点 < 爆炸半径就切，且切最靠近爆心的那段
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

        // 渲染放 LateUpdate：位置已由 FixedUpdate 更新完，画出来不抖
        void LateUpdate()
        {
            if (!line.enabled || intactEnd <= 0) return;
            line.positionCount = intactEnd + 1;
            for (int i = 0; i <= intactEnd; i++)
                line.SetPosition(i, rope.GetPoint(i));
        }
    }
}
