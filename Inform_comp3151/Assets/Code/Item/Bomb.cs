using UnityEngine;
using Inkform.player;
using Inkform.Bus;

/// <summary>
/// 炸弹：碰到即爆 / 攻击态从正面接触时被吞下 / 吐出后引信倒计时爆炸。
/// 被吞下时不销毁自己，只是关掉物理和显示挂到玩家身上，吐出时把同一个实例原样放回世界。
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CircleCollider2D))]
public class Bomb : ItemSuper
{
    private enum BombPhase { Idle, Held, Fuse }

    [Header("Bomb setting")]
    [SerializeField] private float fuseTime = 1.5f;         // 吐出后到爆炸的引信时长
    [SerializeField] private float armTime = 0.3f;          // 吐出后的免疫期：期间撞到玩家只被推开
    [SerializeField] private float blastRadius = 2f;
    [SerializeField] private float blastForce = 18f;
    [SerializeField] private LayerMask blastMask;
    [SerializeField] private float frontEpsilon = 0.05f;    // 正前方判定容差：几乎重合时算正面

    [Header("Break setting")]
    [SerializeField] private GameObject bombFragment;
    [SerializeField] private int fragmentsX = 2;            // 横向切几块
    [SerializeField] private int fragmentsY = 3;            // 纵向切几块
    [SerializeField][Range(0f, 2f)] private float forceMultiper = 0.6f;   // 碎块速度 = 爆炸推力 × 本系数
    [SerializeField] private float spinSpeed = 180f;        // 碎块随机自转的角速度上限

    private BombPhase phase = BombPhase.Idle;
    private bool exploded = false;      // 引信到期那一帧玩家正贴着时，物理回调和 Update 会各炸一次，防重入
    private Rigidbody2D body;
    private CircleCollider2D hitBox;
    private Timer FuseTimer;
    private Timer ArmTimer;

    protected override void Awake()
    {
        base.Awake();                       // ItemSuper 在这里缓存 SpriteRenderer
        body = GetComponent<Rigidbody2D>();
        hitBox = GetComponent<CircleCollider2D>();
    }

    void OnEnable()
    {
        ItemBus.ItemReleased += OnItemReleased;
    }

    void OnDisable()
    {
        ItemBus.ItemReleased -= OnItemReleased;
    }

    void Update()
    {
        if (phase == BombPhase.Fuse && !FuseTimer.IsRunning) Explode();
    }

    // Stay 也要接：一直贴着玩家不会重新触发 Enter，免疫期结束的那一刻就得炸
    void OnCollisionEnter2D(Collision2D collision) => HandlePlayerContact(collision);
    void OnCollisionStay2D(Collision2D collision) => HandlePlayerContact(collision);

    private void HandlePlayerContact(Collision2D collision)
    {
        if (!collision.gameObject.CompareTag("Player")) return;

        switch (phase)
        {
            case BombPhase.Held:                    // 已经在玩家嘴里，不可能再碰到
                return;

            case BombPhase.Fuse:                    // 吐出来的：过了免疫期一碰就炸
                if (!ArmTimer.IsRunning) Explode();
                return;

            default:                                // Idle：满足吞下条件就被吃掉，否则原地爆炸
                // 状态直接读总线快照：触发和状态切换同一帧时不会读到上一帧的旧值
                if (EatAble && PlayerBus.State == PlayerState.Eat && IsInFront(collision.gameObject.transform))
                    Swallow(collision.gameObject.transform);
                else
                    Explode();
                return;
        }
    }


    // 以玩家为原点，看炸弹是不是在玩家朝向的那一侧
    private bool IsInFront(Transform player)
    {
        float dx = transform.position.x - player.position.x;
        if (Mathf.Abs(dx) < frontEpsilon) return true;
        return PlayerBus.Face == FaceDirection.R ? dx > 0f : dx < 0f;
    }

    // 吞下：不销毁，只关物理 + 关显示挂到玩家身上，等着被吐出来
    // 注意不能 SetActive(false)，否则 OnDisable 会退订总线，就收不到「吐出」事件了
    private void Swallow(Transform player)
    {
        phase = BombPhase.Held;
        body.simulated = false;
        hitBox.enabled = false;
        SetVisible(false);
        transform.SetParent(player, false);
        transform.localPosition = Vector3.zero;

        ItemBus.RaiseItemEaten(this);
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
        Shatter.Burst(bombFragment, bounds, fragmentsX, fragmentsY,
                      transform.position, blastForce, forceMultiper, spinSpeed);

        // TODO: 爆炸音效（工程里目前没有音频资源）
        Destroy(gameObject);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, blastRadius);

        // 黄色网格 = 碎块怎么切，方便调 fragmentsX / fragmentsY
        // 这里不能用 hitBox：编辑器下 Awake 没跑过，缓存还是空的
        CircleCollider2D box = GetComponent<CircleCollider2D>();
        if (box == null) return;

        Gizmos.color = Color.yellow;
        Shatter.DrawGrid(box.bounds, fragmentsX, fragmentsY);
    }
}
