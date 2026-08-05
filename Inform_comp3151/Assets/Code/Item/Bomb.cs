using Inkform.Bus;
using Inkform.Fx;
using Inkform.Tool;
using System.Collections.Generic;
using UnityEngine;

namespace Inkform.Item
{
    /// <summary>
    /// 炸弹：碰到即爆 / 被绳索枪命中后拉到玩家嘴边吞下 / 吐出后引信倒计时爆炸。
    /// 吞下时不销毁自己，只是关掉物理和显示挂到玩家身上，吐出时把同一个实例原样放回世界。
    /// 注意：dash（攻击键）不再吞炸弹 —— 碰到就直接爆，吞只能靠绳索枪（TrySwallowByRope）。
    ///
    /// 两种模式：
    /// Normal —— 上述行为，与旧版完全一致。
    /// Hanging —— 悬挂模式：Awake 时给每个悬挂点（HangingPoint 子物体，编辑器生成、
    /// 可拖动）生成一条物理链（Chain，Verlet 绳索），锚点 = 悬挂点的初始世界坐标（固定）；
    /// 链条可被玩家攻击或爆炸切断，全断后炸弹自由下落。悬挂模式的任何状态（断链、被吞后
    /// 吐出、爆炸销毁）都不可恢复 —— 玩家死亡复活不会重置它，本类不实现 IRestorable 正是
    /// 这一点的一部分。
    ///
    /// 速度爆炸：速度达到 speedExplodeThreshold 时与任何物体碰撞都会爆炸，两种模式共用。
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(CircleCollider2D))]
    public class Bomb : ItemSuper
    {
        public enum BombMode { Normal, Hanging }

        private enum BombPhase { Idle, Held, Fuse }

        [Header("Mode")]
        [SerializeField] private BombMode mode = BombMode.Normal;

        [Header("Bomb setting")]
        [SerializeField] private float fuseTime = 1.5f;         // 吐出后到爆炸的引信时长
        [SerializeField] private float armTime = 0.3f;          // 吐出后的免疫期：期间撞到玩家只被推开
        [SerializeField] private float blastRadius = 2f;
        [SerializeField] private float blastForce = 18f;
        [SerializeField] private float chainDelay = 0.1f;       // 被别的爆炸波及后，隔多久跟着炸（0 = 当帧同步连爆）
        [SerializeField] private LayerMask blastMask;

        [Header("Animation")]
        [SerializeField] private Sprite[] triggerFrames;        // 引信帧，0 = 常态，末帧 = 待爆
        [SerializeField] private float proximityRadius = 3.5f;  // 逼近预警半径，独立于 blastRadius

        [Header("Break setting")]
        [SerializeField] private FragmentCue breakCue;          // 碎成什么样全写在这份资产里

        [Header("Hanging mode")]
        [Tooltip("仅编辑器用：Generate Hanging Points 菜单按这个数量生成悬挂点")]
        [SerializeField] private int hangingPointCount = 1;
        [SerializeField] private Vector2 hangingPointSpacing = new Vector2(0.6f, 0f);
        [SerializeField] private float hangingPointHeight = 3f;
        [SerializeField] private Chain.Settings chainSettings = new Chain.Settings();

        [Header("Speed explode")]
        [Tooltip("速度达到该阈值后，与任何物体碰撞都会爆炸；<= 0 关闭")]
        [SerializeField] private float speedExplodeThreshold = 0f;

        private BombPhase phase = BombPhase.Idle;
        private bool exploded = false;      // 引信到期那一帧玩家正贴着时，物理回调和 Update 会各炸一次，防重入
        private Rigidbody2D body;
        private CircleCollider2D hitBox;
        private Timer FuseTimer;
        private Timer ArmTimer;

        private readonly List<Chain> chains = new List<Chain>();    // 悬挂模式生成的链，随炸弹子树一并销毁

        private int frameIndex = 1;        // -1 = 还没定过，保证第一次一定会写一次图
        private float fuseDuration;         // 本次引信的总时长（吐出 = fuseTime，连锁 = chainDelay）
        private bool snapToLast;            // 连锁触发：时间太短，不播动画直接停末帧

        // 所有炸弹共用一份玩家引用。玩家被销毁或换场景后它会变成 Unity 的 fake-null，
        // 下次取用时自动重找，所以不需要像总线那样写 ResetStatics
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
            base.Awake();                       // ItemSuper 在这里缓存 SpriteRenderer
            body = GetComponent<Rigidbody2D>();
            hitBox = GetComponent<CircleCollider2D>();

            RefreshFrame();                     // 先定好常态帧，免得第一帧闪一下预制体上原本那张图

            if (mode == BombMode.Hanging) BuildHangingMode();
        }

        // 悬挂模式：给每个悬挂点生成一条物理链。锚点 = 悬挂点的初始世界坐标，
        // 之后固定不动 —— 所以悬挂点虽然是炸弹子物体，移动炸弹不会移动锚点
        private void BuildHangingMode()
        {
            HangingPoint[] pts = GetComponentsInChildren<HangingPoint>(true);
            if (pts.Length == 0)
            {
                Debug.LogWarning($"{name} 处于 Hanging 模式但没有悬挂点，请在 Inspector 右键执行 Generate Hanging Points", this);
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
        /// 逐帧推导当前该显示哪一帧，变化时顺带发一次 Ticked。
        /// 引信期的帧序直接从 FuseTimer 推导 —— 和触发爆炸的是同一个时钟，
        /// 所以「动画播完」与「炸」必然同时发生，不存在两套时钟需要对齐的问题。
        /// </summary>
        private void RefreshFrame()
        {
            if (triggerFrames == null || triggerFrames.Length == 0) return;
            if (phase == BombPhase.Held) return;        // 叼在嘴里：不可见，也不该发声

            int n = triggerFrames.Length;
            int index;

            if (phase == BombPhase.Fuse)
            {
                index = snapToLast || fuseDuration <= 0f
                    ? n - 1
                    : Mathf.Min((int)((1f - Mathf.Clamp01(FuseTimer.Remaining / fuseDuration)) * n), n - 1);
            }
            else                                        // Idle：玩家越近，帧序越靠后
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

            // 只在「变紧张」的方向发声：玩家卡在帧边界上来回抖时，退回去的那半不发声，
            // 配合 SoundCue.cooldown 足以压住抖动，不需要额外的迟滞逻辑
            bool advanced = index > frameIndex;
            frameIndex = index;
            SetSprite(triggerFrames[index]);

            if (advanced) HazardBus.RaiseTicked(transform.position, index, n);
        }

        // Stay 也要接：一直贴着玩家不会重新触发 Enter，免疫期结束的那一刻就得炸
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

        // 速度爆炸：速度达标时无论撞上什么都直接炸，与对象是谁无关、也不吃玩家那套免疫期。
        // 返回 true = 已爆炸，调用方不用再走玩家接触逻辑
        private bool CheckSpeedExplode(Collision2D collision)
        {
            if (phase == BombPhase.Held) return false;          // 叼在嘴里：碰撞体已关，防御性拦截
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
                case BombPhase.Held:                    // 已经在玩家嘴里，不可能再碰到
                    return;

                case BombPhase.Fuse:                    // 吐出来的：过了免疫期一碰就炸
                    if (collision.gameObject.CompareTag(Tags.BreakAble)) Explode();
                    if (!ArmTimer.IsRunning) Explode();
                    return;

                default:                                // Idle：碰到就爆。
                    // dash（Shift）不再吞炸弹 —— 只有绳索枪的 TrySwallowByRope 能吞。
                    // 被绳索枪标记（ropeGrappled）后，玩家被拉过来接触时直接吞而不是爆
                    if (ropeGrappled && EatAble && ItemBus.Held == null)
                        Swallow(collision.gameObject.transform);
                    else
                        Explode();
                    return;
            }
        }


        // 绳索枪抓取标记：被命中后玩家被拉过来，期间接触玩家必须吞、不能爆
        private bool ropeGrappled;

        public void MarkRopeGrappled() => ropeGrappled = true;
        public void ClearRopeGrappled() => ropeGrappled = false;

        /// <summary>绳索枪命中后把玩家拉到嘴边时的吞下入口。成功返回 true。</summary>
        public bool TrySwallowByRope(Transform player)
        {
            if (phase != BombPhase.Idle) return false;
            if (!EatAble) return false;
            if (ItemBus.Held != null) return false;

            Swallow(player);
            return true;
        }

        // 吞下：不销毁，只关物理 + 关显示挂到玩家身上，等着被吐出来
        // 注意不能 SetActive(false)，否则 OnDisable 会退订总线，就收不到「吐出」事件了
        private void Swallow(Transform player)
        {
            phase = BombPhase.Held;
            ropeGrappled = false;
            body.simulated = false;
            hitBox.enabled = false;
            SetVisible(false);
            transform.SetParent(player, false);
            transform.localPosition = Vector3.zero;

            // 悬挂模式的炸弹被吞：链条全部切断并隐藏。之后吐出/死亡掉落时
            // 它就是一颗自由炸弹 —— 悬挂状态不可恢复，这正是设计意图
            foreach (Chain c in chains) c.CutAll();

            ItemBus.RaiseItemEaten(this);
        }

        /// <summary>
        /// 玩家死亡时被放回世界（Idle 相，不点引信）：从 Held 恢复成一颗普通炸弹。
        /// 与 OnItemReleased 的区别：死亡掉落的炸弹不进入倒计时，只是停在原地等玩家再捡。
        /// </summary>
        public override void DropAt(Vector2 pos)
        {
            phase = BombPhase.Idle;
            transform.SetParent(null);
            SetVisible(true);
            hitBox.enabled = true;
            body.simulated = true;

            // 工程里 m_AutoSyncTransforms = 0：transform 和刚体位置互不同步，两个都要写
            transform.position = pos;
            body.position = pos;
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;

            exploded = false;       // 本实例并未炸过，重置以防万一
            frameIndex = -1;        // 强制下一帧重写常态帧 —— 现在是 Idle 相，不该停在引信末帧
            RefreshFrame();
        }

        // 被玩家吐出来：放回世界、给初速度、点引信
        private void OnItemReleased(ItemSuper item, Vector2 pos, Vector2 velocity)
        {
            if (item != (ItemSuper)this) return;        // 吐的不是我

            phase = BombPhase.Fuse;
            transform.SetParent(null);
            SetVisible(true);
            hitBox.enabled = true;
            body.simulated = true;

            // 工程里 m_AutoSyncTransforms = 0：transform 和刚体位置互不同步，两个都要写
            transform.position = pos;
            body.position = pos;
            body.linearVelocity = velocity;
            body.angularVelocity = 0f;

            FuseTimer.Set(fuseTime);
            ArmTimer.Set(armTime);

            fuseDuration = fuseTime;    // 动画按这个时长铺满，末帧亮起就是爆炸的瞬间
            snapToLast = false;
        }

        // 由 HazardBus 在别的炸弹爆炸波及到自己时回调：隔 chainDelay 后跟着炸。
        // 不直接 Explode()，而是转成 Fuse 相复用 Update() 里现成的引信判定 ——
        // 一排炸弹因此会依次炸开而不是同一帧全炸光，也顺带避免了链有多长、
        // 同步递归就有多深（A.Explode 里直接调 B.Explode 再调 C.Explode…）。
        private void OnChainExploded(GameObject victim, Vector2 center, float force)
        {
            if (victim != gameObject) return;       // 波及的不是我
            if (exploded) return;                   // 我自己已经在炸了
            if (phase == BombPhase.Held) return;    // 叼在玩家嘴里的那颗不连锁

            // 已经在倒计时、而且比连锁还快时不要把它拖慢：引信只缩短、不延长
            if (phase == BombPhase.Fuse && FuseTimer.Remaining <= chainDelay) return;

            phase = BombPhase.Fuse;
            FuseTimer.Set(chainDelay);
            // 免疫期恰好盖住连锁引信：否则玩家还贴着时 OnCollisionStay2D 会当场把它引爆，
            // chainDelay 被跳过、级联节奏乱掉，而且玩家往往刚被上一发炸飞还没脱离接触
            ArmTimer.Set(chainDelay);

            fuseDuration = chainDelay;
            snapToLast = true;          // 0.1s 塞不下整段动画，直接停在待爆帧
        }

        private void Explode()
        {
            // Destroy 要到帧末才生效，拦不住同一帧内的第二次调用；不挡的话抖屏、击退、碎块全是双份
            if (exploded) return;
            exploded = true;

            Vector2 center = transform.position;

            Collider2D[] hits = Physics2D.OverlapCircleAll(center, blastRadius, blastMask);
            foreach (Collider2D h in hits)
            {
                if (h.gameObject == gameObject) continue;      // 不炸自己
                HazardBus.RaiseExploded(h.gameObject, center, blastForce);
            }

            playExplode();      // 无论炸到什么（哪怕一个都没炸到）都要销毁自己
        }

        private void playExplode()
        {
            // 必须赶在关掉碰撞体之前取包围盒：Collider2D 一 disabled，
            // 物理形状就被移除，bounds 会退化成原点上的零尺寸
            Bounds bounds = hitBox.bounds;

            // 整体爆炸信号：每次爆炸恰好一次，屏幕抖动等特效靠它驱动
            // （不能用 Exploded —— 那个在 foreach 里逐受害者发，炸到 N 个就发 N 次）
            HazardBus.RaiseBlast(transform.position, blastRadius, blastForce);

            // Destroy 要等到帧末才生效，这期间本体还在渲染，不关的话本体和碎块会重叠显示一帧
            hitBox.enabled = false;
            SetVisible(false);

            // 炸弹自身碎成小块从爆心向四周弹开，和可破坏墙同一套表现
            Shatter.Burst(breakCue, bounds, transform.position, blastForce);

            // 爆炸音效由 AudioDirector 订阅上面那条 Blast 播放，本类不碰音频
            Destroy(gameObject);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, blastRadius);

            // 青色 = 逼近预警范围，玩家一进来帧序就开始推进。调参时要让它明显大于 blastRadius
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, proximityRadius);

            if (mode == BombMode.Hanging)
            {
                // 蓝色虚线 = 悬挂点到炸弹的链位，方便在 Scene 里调悬挂点位置
                HangingPoint[] pts = GetComponentsInChildren<HangingPoint>(true);
                Gizmos.color = new Color(0.2f, 0.7f, 1f, 0.6f);
                foreach (HangingPoint pt in pts)
                {
                    Gizmos.DrawLine(pt.transform.position, transform.position);
                }
            }

            // 黄色网格 = 碎块怎么切，方便调 Cue 里的 cellsX / cellsY
            // 这里不能用 hitBox：编辑器下 Awake 没跑过，缓存还是空的
            CircleCollider2D box = GetComponent<CircleCollider2D>();
            if (box == null || breakCue == null) return;

            Gizmos.color = Color.yellow;
            Shatter.DrawGrid(box.bounds, breakCue.cellsX, breakCue.cellsY);
        }

        // ---- 编辑器工具：生成/清理悬挂点（场景实例和预制体上都能跑）----

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

                // 以炸弹为中心横向等距排开、整体抬高 hangingPointHeight，生成后自己在 Scene 里拖
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
