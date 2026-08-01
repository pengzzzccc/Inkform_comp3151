using Inkform.Bus;
using Inkform.Fx;
using Inkform.Life;
using UnityEngine;

namespace Inkform.Level
{
    /// <summary>
    /// 可破坏墙：被爆炸波及时按网格碎成若干块，每块沿「爆心 → 块中心」的 8 向弹开。
    /// 一发即毁，不做耐久度。碎块尺寸/位置由自身碰撞体包围盒算出，墙缩放成多大都自适应。
    ///
    /// 碎掉之后**不销毁自己**，只关碰撞体和渲染 —— 玩家死在这一段时 LevelMemento 要把墙还原回来，
    /// 已经 Destroy 掉的物体是还不回来的。这也和 PlayerHandler 死亡时用 simulated = false、
    /// Bomb 被吞时用 SetVisible(false) 是同一个路数：都刻意避开真正的销毁/失活。
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    [RequireComponent(typeof(SpriteRenderer))]
    public class BreakableWall : MonoBehaviour, IRestorable
    {
        [Header("Break setting")]
        [SerializeField] private FragmentCue breakCue;          // 碎成什么样全写在这份资产里

        private Collider2D box;
        private SpriteRenderer sprite;
        private bool broken = false;    // 同一帧两颗炸弹都炸到时，防止碎两次

        void Awake()
        {
            box = GetComponent<Collider2D>();
            sprite = GetComponent<SpriteRenderer>();
        }

        void OnEnable()
        {
            HazardBus.Exploded += OnExploded;
        }

        void OnDisable()
        {
            HazardBus.Exploded -= OnExploded;
        }

        // 由 HazardBus 在爆炸时回调：拆成碎块然后把本体隐掉（不销毁，留着等还原）
        private void OnExploded(GameObject victim, Vector2 center, float force)
        {
            if (victim != gameObject) return;
            if (broken) return;

            // 必须赶在关掉碰撞体之前取包围盒：Collider2D 一 disabled，
            // 物理形状就被移除，bounds 会退化成原点上的零尺寸
            Bounds bounds = box.bounds;

            SetBroken(true);

            // bounds 在关碰撞体之前就取好了，这里传的是那份快照
            Shatter.Burst(breakCue, bounds, center, force);

            // 碎裂信号：音效等表现靠它驱动。用 bounds.center 而不是 transform.position ——
            // bounds 是关碰撞体之前取的快照，才是这面墙真正的几何中心
            HazardBus.RaiseBroken(bounds.center);
        }

        // 碎与不碎的唯一开关，破坏和还原共用 —— 两边各写一遍迟早会漏掉其中一个 enabled
        private void SetBroken(bool value)
        {
            broken = value;
            box.enabled = !value;
            sprite.enabled = !value;
        }

        // ---- IRestorable ----

        public IMemento Capture() => new WallMemento(this, broken);

        /// <summary>墙的快照。内容对 LevelMemento 不透明，只有本类知道里面存的是「碎没碎」。</summary>
        private class WallMemento : IMemento
        {
            private readonly BreakableWall wall;
            private readonly bool broken;

            public WallMemento(BreakableWall wall, bool broken)
            {
                this.wall = wall;
                this.broken = broken;
            }

            public void Restore()
            {
                if (wall == null) return;   // 场景切换等原因下墙没了，静默跳过
                wall.SetBroken(broken);
            }
        }

        // 在 Scene 视图里画出切分网格，方便调 Cue 里的 cellsX / cellsY
        void OnDrawGizmosSelected()
        {
            Collider2D c = GetComponent<Collider2D>();
            if (c == null || breakCue == null) return;

            Gizmos.color = Color.yellow;
            Shatter.DrawGrid(c.bounds, breakCue.cellsX, breakCue.cellsY);
        }
    }
}
