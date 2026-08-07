using Inkform.Bus;
using Inkform.Fx;
using Inkform.Tool;
using UnityEngine;

namespace Inkform.Interactable.Parts
{
    /// <summary>
    /// 爆炸核心：定义「炸」本身 —— 半径、推力、波及层、碎片表现，提供幂等的 Explode() 入口。
    /// 自身完全被动：不订阅总线、不消费接触，由触发类 part（ExplodeOnContact / ExplodeOnBlast /
    /// ExplodeAfterDelay / ExplodeOnImpact）在满足条件时调用 Explode()。
    ///
    /// 爆炸传播全走 HazardBus：逐受害者 RaiseExploded（BreakableWall 碎、PlayerHandler 击退、
    /// 其他爆炸物连锁）+ 整体 RaiseBlast（抖屏/音效/断链）—— 这些听众都已在总线上，零改动。
    ///
    /// 逼近预警动画：配了 triggerFrames + proximityRadius 后，玩家进入预警半径帧序就开始推进
    /// （越近越靠后，0 = 常态），变化时发一次 Ticked（AudioDirector 播 tick 音）—— 与 Bomb 的
    /// 逼近逻辑同源，只取「玩家距离」这一分支；引信期那半帧序要读引信时钟，暂不进框架。
    /// </summary>
    public class ExplodePart : MonoBehaviour, IInteractablePart
    {
        [Header("Blast")]
        [SerializeField] private float blastRadius = 2f;
        [SerializeField] private float blastForce = 18f;
        [SerializeField] private LayerMask blastMask;
        [Tooltip("碎成什么样全写在这份资产里；null = 静默无碎片")]
        [SerializeField] private FragmentCue breakCue;

        [Header("Animation")]
        [Tooltip("逼近预警帧序列，0 = 常态，越靠后越接近玩家；null/空数组 = 不启用动画")]
        [SerializeField] private Sprite[] triggerFrames;
        [Tooltip("玩家进入该半径后帧序开始推进；<= 0 关闭")]
        [SerializeField] private float proximityRadius = 0f;

        private Interactable root;
        private Collider2D body;
        private SpriteRenderer sprite;
        private bool exploded;      // 幂等：同一帧多个触发同时进来只炸一次

        // ---- 逼近预警动画状态 ----

        private int frameIndex = -1;        // -1 = 还没定过，保证第一次一定会写一次图
        private Sprite _current;            // SetSprite 去重：逐帧驱动但大多数帧是同一张图

        // 所有爆炸物共用一份玩家引用。玩家被销毁或换场景后它会变成 Unity 的 fake-null，
        // 下次取用时自动重找，所以不需要像总线那样写 ResetStatics（Bomb 同款）
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

        // 不消费接触：让触发 part 正常收到分发
        public bool HandleContact(ContactPhase phase, Collider2D other) => false;

        void Update()
        {
            RefreshFrame();
        }

        /// <summary>
        /// 逐帧推导当前该显示哪一帧（逼近预警）：玩家越近，帧序越靠后，变化时顺带发一次 Ticked。
        /// 渲染器被关（被吞/隐藏/爆炸隐掉）时不推帧不发声 —— 叼在嘴里的爆炸物不该有逼近预警音。
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

            // 只在「变紧张」的方向发声：玩家卡在帧边界上来回抖时，退回去的那半不发声，
            // 配合 SoundCue.cooldown 足以压住抖动，不需要额外的迟滞逻辑
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

        /// <summary>爆炸。幂等：只生效一次，触发 part 可放心重复调用。</summary>
        public void Explode()
        {
            if (exploded) return;
            exploded = true;

            // 必须赶在关掉碰撞体之前取包围盒：Collider2D 一 disabled，
            // 物理形状就被移除，bounds 会退化成原点上的零尺寸
            Bounds bounds = body != null ? body.bounds : new Bounds(root.transform.position, Vector3.one);
            Vector2 center = root.transform.position;

            // 逐受害者：爆炸波及圈内的每个物体各收一次 Exploded
            Collider2D[] hits = Physics2D.OverlapCircleAll(center, blastRadius, blastMask);
            foreach (Collider2D h in hits)
            {
                if (h.gameObject == root.gameObject) continue;      // 不炸自己
                HazardBus.RaiseExploded(h.gameObject, center, blastForce);
            }

            // 整体爆炸信号：每次爆炸恰好一次，屏幕抖动等特效靠它驱动
            HazardBus.RaiseBlast(center, blastRadius, blastForce);

            // 本体先隐掉再碎（Destroy 要帧末才生效，不关的话本体和碎块重叠显示一帧）
            if (body != null) body.enabled = false;
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true)) r.enabled = false;

            // 爆炸物不可恢复（与 Bomb 一致）：碎完直接销毁
            Shatter.Burst(breakCue, bounds, center, blastForce);
            Destroy(root.gameObject);
        }
    }
}
