using Inkform.Bus;
using Inkform.Fx;
using UnityEngine;

namespace Inkform.Level
{
    /// <summary>
    /// 可破坏墙：被爆炸波及时按网格碎成若干块，每块沿「爆心 → 块中心」的 8 向弹开。
    /// 一发即毁，不做耐久度。碎块尺寸/位置由自身碰撞体包围盒算出，墙缩放成多大都自适应。
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    [RequireComponent(typeof(SpriteRenderer))]
    public class BreakableWall : MonoBehaviour
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

        // 由 HazardBus 在爆炸时回调：拆成碎块然后销毁本体
        private void OnExploded(GameObject victim, Vector2 center, float force)
        {
            if (victim != gameObject) return;
            if (broken) return;
            broken = true;

            // 必须赶在关掉碰撞体之前取包围盒：Collider2D 一 disabled，
            // 物理形状就被移除，bounds 会退化成原点上的零尺寸
            Bounds bounds = box.bounds;

            // Destroy 要等到帧末才生效，这期间本体碰撞体还在，会把刚生成的碎块顶飞；
            // 渲染同理，不关的话碎块和整墙会重叠显示一帧
            box.enabled = false;
            sprite.enabled = false;

            // bounds 在关碰撞体之前就取好了，这里传的是那份快照
            Shatter.Burst(breakCue, bounds, center, force);

            // 碎裂信号：音效等表现靠它驱动。用 bounds.center 而不是 transform.position ——
            // bounds 是关碰撞体之前取的快照，才是这面墙真正的几何中心
            HazardBus.RaiseBroken(bounds.center);

            Destroy(gameObject);
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
