using Inkform.Bus;
using Inkform.Interactable;
using Inkform.Item;
using Inkform.Life;
using Inkform.Tool;
using UnityEngine;

namespace Inkform.Player
{
    /// <summary>
    /// 绳索枪：替换 dash 成为主武器（dash 移到 Shift）。命中后直线拉取 —— 不悬挂不荡。
    ///
    /// 瞄准：准星 = 自由光标，鼠标 delta / 右摇杆（Aim 动作）驱动、以玩家为圆心在
    /// 射程半径内上下左右任意移动；由枪口与准星点解出穿过准星的初速度（弹道解算），
    /// 子弹发射后沿该抛物线飞行 —— 虚线 = 同一弹道的采样，命中地形则截断在命中点
    /// （绿 = 会钩住），畅通则画到准星（红 = 会打空 → 收绳）。
    ///
    /// 飞行：子弹是真实刚体（重力、初速 = 解算结果、阻尼 0 与预览一致，
    /// excludeLayers 排除玩家/炸弹等，只碰地形）；绳索从枪口放长、末端钉在钩子上，
    /// 解算下垂 —— 绳索加重后（ropeGravityScale）拖拽感更强。
    /// 命中：地形 → 短 hitstop 后沿直线把玩家拉向锚点（Pulling），中途再按发射或
    /// 跳跃即停止施力并松绳回收；到达锚点附近自动松绳。
    /// 未命中 / 取消：收绳 —— 锚点 = 枪口，末端附子弹刚体把它拉回来
    /// （和炸弹悬挂链同一套 Verlet 解算）。
    ///
    /// 特殊命中：
    /// ① 可叼物（实现 ICarriable 的物体：Bomb、CarriablePart 挂载物等）：短 hitstop 后
    ///    把玩家拉向目标，到达后吞下（ICarriable.TrySwallowByRope）；
    /// ② 炸弹悬挂链：在命中点切断（Chain.CutAt，靠 Chain 静态注册表逐段检测）。
    ///
    /// 射程：默认 maxRange，特殊区域（RopeRangeZone）通过 RopeGunBus 覆盖/恢复。
    /// 挂在 Player 上；没挂时游戏照常玩（PlayerHandler 用 TryGetComponent 探测）。
    /// </summary>
    public class RopeGun : MonoBehaviour
    {
        private enum RopePhase { Idle, Flying, ReelIn, Pulling, PullingEat }

        [Header("Range")]
        [SerializeField] private float maxRange = 4.2f;                     // 默认最大射程（也是光标半径/绳索长度上限）
        [SerializeField] private LayerMask hitMask = (1 << 6) | (1 << 11);  // Terrain | Breakable

        [Header("Projectile")]
        [SerializeField] private float launchSpeed = 22f;
        [SerializeField] private float bulletGravityScale = 1f;
        [SerializeField] private float bulletRadius = 0.12f;
        [SerializeField] private Sprite bulletSprite;                       // 可替换
        [SerializeField] private float muzzleOffset = 0.5f;                 // 钩子出生点沿瞄准方向前移，避免与玩家重叠
        [SerializeField] private float bombDetectRadius = 0.55f;            // 钩子飞行中探测炸弹的半径

        [Header("Aim")]
        [SerializeField] private float mouseAimSensitivity = 1f;
        [SerializeField] private float stickAimSpeed = 5f;
        [SerializeField] private float mouseShowThreshold = 0.5f;           // 鼠标 |delta|（像素）超过此值算「有瞄准输入」
        [SerializeField] private float stickShowThreshold = 0.1f;           // 右摇杆 |delta| 超过此值算「有瞄准输入」
        [SerializeField] private float aimShowTime = 0.15f;                 // 输入停止后延迟隐藏预览（防抖；0 = 立即隐藏）
        [SerializeField] private float aimFallbackElevation = 30f;          // 无瞄准输入时，发射方向朝上倾的角度（度）
        [SerializeField] private Sprite crosshairSprite;                    // 准星，可替换
        [SerializeField] private float crosshairSize = 0.6f;
        [SerializeField] private Color hitColor = new Color(0.35f, 1f, 0.35f);
        [SerializeField] private Color missColor = new Color(1f, 0.35f, 0.35f);

        [Header("Parabola preview")]
        [SerializeField] private Material dashMaterial;                     // 虚线材质（含每单位贴图），可替换
        [SerializeField] private float dashWidth = 0.05f;
        [SerializeField] private float dashUnitScale = 0.5f;                // 贴图重复间距（世界单位）
        [SerializeField] private int dashSortingOrder = 10;
        [SerializeField] private float previewStepDt = 1f / 30f;

        [Header("Rope")]
        [SerializeField] private VerletRope.Settings ropeSettings = VerletRope.Settings.Default();
        [SerializeField] private LayerMask ropeCollisionMask = (1 << 6) | (1 << 11);  // 绳索段碰撞层（默认 Terrain|Breakable）
        [SerializeField] private float ropeGravityScale = 2.5f;                // 绳索重量：段重力系数，越大下垂越明显
        [SerializeField] private Material ropeMaterial;
        [SerializeField] private float ropeWidth = 0.06f;
        [SerializeField] private int ropeSortingOrder = -5;
        [SerializeField] private float chainCutRadius = 0.35f;              // 钩子离炸弹链段多近算切断

        [Header("Pull")]
        [SerializeField] private float pullSpeed = 16f;                     // 硬速度拉取（地形命中 / 抓炸弹共用）：必须压过玩家重力 3x，否则向上拉不动
        [SerializeField] private float arrivalDistance = 0.7f;              // 到达接触点判定（玩家被墙挡住时中心距墙≈0.5，0.7 即已到达）
        [SerializeField] private float stuckTime = 0.25f;                   // 拉取卡死兜底：距离不再下降持续这么久就松绳
        [SerializeField] private float tautRopeGravity = 0.15f;             // 拉取阶段绳索重力系数：压低 = 绷紧近乎直线
        [SerializeField] private float swallowDistance = 0.75f;             // 距炸弹多近触发吞下
        [SerializeField] private float detachDistance = 0.5f;               // 收绳拉到多近松绳
        [SerializeField] private float reelTimeout = 2.5f;                  // 收绳超时：钩子卡死角时强制回收

        [Header("Reel & hit")]
        [SerializeField] private float reelSpeed = 5f;                      // 未命中/取消：绳长缩短速度（收钩）
        [SerializeField] private float hitStopTime = 0.06f;                 // 命中瞬间的短卡帧（走 FxBus，ScreenFx 有 0.25s 上限）

        private RopePhase phase = RopePhase.Idle;
        private float currentMaxRange;
        private float ropeLength;       // 当前绳长（绝对上限 = currentMaxRange），收绳驱动它缩短
        private Timer reelTimer;        // 收绳超时：钩子卡死在角落时强制回收
        private bool ropeTaut;          // 钩子已被绷在射程圆上（连续第二帧才真正转收绳，见 FixedUpdate）

        private Rigidbody2D playerBody;
        private PlayerMotor motor;

        private Vector2 aimOffset;      // 瞄准游标相对玩家的偏移（世界单位），自由移动、以玩家为圆心
        private Vector2 moveInput;      // 最近一帧的移动输入（PlayerHandler 转发）：无瞄准输入时的发射方向来源
        private Timer aimShowTimer;     // 有瞄准输入时刷新；过期 = 隐藏预览 + 发射退回移动方向上倾
        private bool previewVisible;    // 预览显隐去重

        private GameObject hookGo;
        private Rigidbody2D hookBody;
        private HookHit hookHit;

        private ICarriable grapple;        // PullingEat 的目标（只经手接口，不认识具体实现）
        private Vector2 pullTarget;     // 锚点：地形命中点 / 可叼物当前位置
        private float lastPullDist;     // 拉取卡死检测：上一物理步到锚点的距离
        private float pullStuck;        // 距离不再下降的累计时长

        private readonly VerletRope rope = new VerletRope();
        private LineRenderer ropeLine;

        private Transform reticle;
        private SpriteRenderer reticleSprite;
        private LineRenderer dashLine;

        // 预览抛物线采样点（最多 64 步 + 起点），避免逐帧分配 List
        private readonly Vector2[] arcPoints = new Vector2[65];
        private int arcCount;

        private static Sprite discSprite;   // 运行时生成的白色圆盘，未配素材时的回退

        private static Sprite DiscSprite
        {
            get
            {
                if (discSprite != null) return discSprite;

                const int size = 32;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                float r = size * 0.5f - 1f;
                Vector2 c = new Vector2(size * 0.5f, size * 0.5f);
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c);
                        tex.SetPixel(x, y, d <= r ? Color.white : Color.clear);
                    }
                }
                tex.Apply();
                discSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
                return discSprite;
            }
        }

        /// <summary>最近一帧的移动输入（PlayerHandler 每帧转发，含键盘摇杆合成/左摇杆）。
        /// 无瞄准输入时，发射与吐炸弹的方向 = 它向上倾 aimFallbackElevation°。</summary>
        public void SetMoveInput(Vector2 input) => moveInput = input;

        /// <summary>
        /// 有效发射方向：有瞄准输入 → 精确朝准星；否则 → 当前移动方向（或面朝方向）朝正上
        /// 倾 aimFallbackElevation°（不越过正上）。绳索枪发射与吐炸弹共用。
        /// </summary>
        public Vector2 EffectiveFireDir
        {
            get
            {
                if (aimShowTimer.IsRunning && aimOffset.sqrMagnitude > 0.0001f)
                    return aimOffset.normalized;

                Vector2 d = moveInput.sqrMagnitude > 0.01f
                    ? moveInput.normalized
                    : (PlayerBus.Face == FaceDirection.R ? Vector2.right : Vector2.left);

                float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                float elevated = angle < 90f
                    ? Mathf.Min(angle + aimFallbackElevation, 90f)
                    : Mathf.Max(angle - aimFallbackElevation, 90f);
                float rad = elevated * Mathf.Deg2Rad;
                return new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
            }
        }

        void Awake()
        {
            playerBody = GetComponent<Rigidbody2D>();
            TryGetComponent(out motor);
            currentMaxRange = maxRange;

            reticle = CreateFx("RopeReticle", out reticleSprite,
                crosshairSprite != null ? crosshairSprite : DiscSprite, 20);
            reticle.localScale = Vector3.one * crosshairSize;

            dashLine = CreateLine("RopeDashLine", dashMaterial, dashWidth, dashSortingOrder);
            ropeLine = CreateLine("RopeLine", ropeMaterial, ropeWidth, ropeSortingOrder);

            aimOffset = Vector2.right * currentMaxRange * 0.6f;
        }

        void OnEnable()
        {
            RopeGunBus.RangeOverride += OnRangeOverride;
            RopeGunBus.RangeRestored += OnRangeRestored;
            LifeBus.Died += OnDied;
            LifeBus.Respawned += OnRespawned;
        }

        void OnDisable()
        {
            RopeGunBus.RangeOverride -= OnRangeOverride;
            RopeGunBus.RangeRestored -= OnRangeRestored;
            LifeBus.Died -= OnDied;
            LifeBus.Respawned -= OnRespawned;
        }

        // ---- 输入入口（PlayerHandler 转发）----

        /// <summary>瞄准输入。pixelDelta = 鼠标像素差（需要按屏幕高度换算世界单位），
        /// 否则是右摇杆模拟量（速度 × dt）。输入超过设备阈值时刷新预览显示计时器。</summary>
        public void Aim(Vector2 delta, bool pixelDelta)
        {
            if (phase != RopePhase.Idle || LifeBus.IsDead) return;

            if (pixelDelta)
            {
                Camera cam = Camera.main;
                float worldPerPixel = cam != null
                    ? cam.orthographicSize * 2f / Mathf.Max(1f, Screen.height)
                    : 0.01f;
                aimOffset += delta * worldPerPixel * mouseAimSensitivity;
                if (Mathf.Abs(delta.x) > mouseShowThreshold || Mathf.Abs(delta.y) > mouseShowThreshold)
                    aimShowTimer.Set(aimShowTime);
            }
            else
            {
                aimOffset += delta * (stickAimSpeed * Time.deltaTime);
                if (delta.sqrMagnitude > stickShowThreshold * stickShowThreshold)
                    aimShowTimer.Set(aimShowTime);
            }

            if (aimOffset.sqrMagnitude > currentMaxRange * currentMaxRange)
                aimOffset = aimOffset.normalized * currentMaxRange;
        }

        public void TryFire()
        {
            if (LifeBus.IsDead) return;
            if (phase != RopePhase.Idle)
            {
                Cancel();           // 再次按下 = 取消本次发射 / 松绳
                return;
            }
            if (ItemBus.Held != null) return;   // 嘴里叼着东西射不了

            // 有瞄准输入 → 精确朝准星；隐藏时 → 移动方向上倾（EffectiveFireDir）
            Vector2 fireDir = EffectiveFireDir;
            Vector2 origin = playerBody.position + fireDir * muzzleOffset;
            // 弹道目标：瞄准中 = 准星点（精确穿过）；隐藏时 = 沿有效方向到射程
            Vector2 target = aimShowTimer.IsRunning
                ? playerBody.position + aimOffset
                : origin + fireDir * currentMaxRange;
            SolveBallistic(origin, target, launchSpeed, Physics2D.gravity * bulletGravityScale,
                out Vector2 v0, out _);

            phase = RopePhase.Flying;
            ropeTaut = false;
            SetPreviewShown(false);

            hookGo = new GameObject("GrappleHook");
            hookGo.transform.position = origin;
            hookBody = hookGo.AddComponent<Rigidbody2D>();
            hookBody.gravityScale = bulletGravityScale;
            hookBody.linearDamping = 0f;                // 与预览解析一致，抛物线才吻合
            // includeLayers 是「追加允许」语义，不会限制碰撞 —— 必须用 excludeLayers 排除
            // 玩家/炸弹等除地形外的一切（层碰撞矩阵默认全开，不加排除钩子出生就会撞玩家）
            hookBody.excludeLayers = ~hitMask;
            var col = hookGo.AddComponent<CircleCollider2D>();
            col.radius = bulletRadius;
            var sr = hookGo.AddComponent<SpriteRenderer>();
            sr.sprite = bulletSprite != null ? bulletSprite : DiscSprite;
            sr.sortingOrder = 15;
            hookBody.linearVelocity = v0;
            hookHit = hookGo.AddComponent<HookHit>();
            hookHit.Init(this);

            rope.Init(RopeCfg(), origin, origin);
            ropeLine.enabled = true;
        }

        /// <summary>跳跃键按下时由 PlayerHandler 调：拉取中松绳并让跳跃生效。</summary>
        public void DetachOnJump()
        {
            if (phase == RopePhase.Idle || phase == RopePhase.ReelIn || phase == RopePhase.Flying) return;
            Finish();
        }

        // ---- 内部流程 ----

        // 把序列化的绳索碰撞层与重量合并进解算配置（绳索段要碰地形、加重下垂；炸弹链保持关闭）
        private VerletRope.Settings RopeCfg()
        {
            var s = ropeSettings;
            s.collisionMask = ropeCollisionMask;
            s.gravityScale = ropeGravityScale;
            return s;
        }

        private void Cancel()
        {
            switch (phase)
            {
                case RopePhase.Idle: return;
                case RopePhase.Flying:
                    StartReelIn();
                    return;
                case RopePhase.ReelIn:
                    return;                         // 已经在收绳
                default:
                    Finish();                       // 拉取中：直接松绳回收
                    return;
            }
        }

        private void OnHookTerrainHit(Collision2D collision)
        {
            if (phase != RopePhase.Flying) return;

            // Terrain/Breakable 层上可能躺着可叼物（食物箱这类「站得住又能吃」的实体）：
            // 钩子物理命中它时改走「吞吃拉取」，而不是当普通地形锚点
            if (collision.collider.TryGetComponent(out ICarriable carriable))
            {
                StartPullingCarriable(carriable);
                return;
            }

            ContactPoint2D contact = collision.GetContact(0);

            // 锚点沿接触法线往外挪半个绳宽：接触点本身贴在地形表面上，绳段的 CircleCast
            // 从那里起测会一出生就重叠（工程 QueriesStartInColliders 开着），顺带也让绳子
            // 渲染时不会有一小截插进墙里。法线方向约定容易记反，用钩子的实际位置校一次符号。
            Vector2 outward = contact.normal;
            if (Vector2.Dot(outward, hookBody.position - contact.point) < 0f) outward = -outward;
            Vector2 hitPoint = contact.point + outward * Mathf.Max(ropeSettings.collisionRadius, 0.01f);

            // 命中点在钩子表面上，比钩子中心又远出一个 bulletRadius（再加上面那点法线外移）——
            // 贴着射程边缘打墙时不留这点余量，合法命中会被判成超程
            float rangeSlack = bulletRadius + Mathf.Max(ropeSettings.collisionRadius, 0.01f);
            if (Vector2.Distance(playerBody.position, hitPoint) > currentMaxRange + rangeSlack)
            {
                StartReelIn();      // 超出射程：当未命中处理
                return;
            }

            pullTarget = hitPoint;
            AnchorHook();
            motor?.SetMoveLocked(true);
            reelTimer.Clear();      // 挂上了就不再是收绳流程，别把到期时间漏给下一次发射

            // 命中瞬间的短卡帧：impact 感。走 FxBus，ScreenFx 负责恢复 timeScale
            if (hitStopTime > 0f) FxBus.RaiseHitStop(hitStopTime);

            // 直线拉取：不悬挂不荡 —— 玩家被沿直线拉向锚点（Pulling），中途再按发射/跳跃即停止施力
            phase = RopePhase.Pulling;
            lastPullDist = float.MaxValue;      // 第一帧必算推进，卡死计时从真正停住才开始
            pullStuck = 0f;
        }

        private void StartReelIn()
        {
            if (phase == RopePhase.ReelIn) return;
            phase = RopePhase.ReelIn;
            reelTimer.Set(reelTimeout);

            // 绳长从当前钩子距离起算，之后按 reelSpeed 缓慢缩短 —— 钩子是被绳子拖回来的，
            // 不直接写速度（写速度会瞬间满速，收绳「嗖」一下且没有绳子拉扯的感觉）
            if (hookBody != null)
                ropeLength = Mathf.Min(Vector2.Distance(playerBody.position, hookBody.position), currentMaxRange);
            // 收绳阶段保留碰撞体：钩子沿墙滑回、不穿模（卡角落由 reelTimeout 兜底）
        }

        private void StartPullingCarriable(ICarriable target)
        {
            target.MarkRopeGrappled();
            grapple = target;
            phase = RopePhase.PullingEat;
            AnchorHook();
            motor?.SetMoveLocked(true);

            // 抓可叼物同样是「命中」，给一样的短卡帧
            if (hitStopTime > 0f) FxBus.RaiseHitStop(hitStopTime);
        }

        // 钩子变成静态锚点：关物理、关碰撞回调、关碰撞体，不再参与任何物理交互
        private void AnchorHook()
        {
            hookBody.simulated = false;
            hookHit.enabled = false;
            if (hookGo.TryGetComponent(out CircleCollider2D col)) col.enabled = false;
        }

        private void Finish()
        {
            // 接口没有 Unity 的假 null 重载：先转 MonoBehaviour 再判，认得出已销毁的目标
            if (grapple as MonoBehaviour != null)
            {
                grapple.ClearRopeGrappled();
            }
            grapple = null;
            motor?.SetMoveLocked(false);
            phase = RopePhase.Idle;
            reelTimer.Clear();
            ropeTaut = false;
            DespawnHook();
            ropeLine.enabled = false;
            // 预览显隐交给 Update 里的输入计时器（无输入则保持隐藏）
        }

        private void DespawnHook()
        {
            if (hookGo != null) Destroy(hookGo);
            hookGo = null;
            hookBody = null;
            hookHit = null;
        }

        private void SetPreviewShown(bool shown)
        {
            if (shown == previewVisible) return;    // 去重：逐帧驱动也不每帧 SetActive
            previewVisible = shown;
            if (reticle != null) reticle.gameObject.SetActive(shown);
            dashLine.enabled = shown;
        }

        // ---- 帧循环 ----

        void Update()
        {
            if (LifeBus.IsDead) return;
            if (phase == RopePhase.Idle)
            {
                // 预览只在有瞄准输入时计算并显示：隐藏态不白跑弹道解算与 Raycast
                if (aimShowTimer.IsRunning) UpdatePreview();
                SetPreviewShown(aimShowTimer.IsRunning);
            }
        }

        void FixedUpdate()
        {
            if (phase == RopePhase.Idle || LifeBus.IsDead) return;

            float dt = Time.fixedDeltaTime;
            Vector2 origin = playerBody.position;

            switch (phase)
            {
                case RopePhase.Flying:
                    // 绳索从枪口随钩子放长，但绝对不超过 currentMaxRange（绳长上限固定）
                    float hookDist = Vector2.Distance(origin, hookBody.position);
                    if (hookDist >= currentMaxRange)
                    {
                        // 绳子物理限长：钩子被绷在射程圆上，只消掉向外径向速度、保留切向
                        // （像撞到绳尾被拉住）
                        Vector2 d = hookBody.position - origin;
                        Vector2 dir = d.sqrMagnitude > 0.0001f ? d.normalized : Vector2.right;
                        hookBody.position = origin + dir * currentMaxRange;
                        float radialOut = Vector2.Dot(hookBody.linearVelocity, dir);
                        if (radialOut > 0f) hookBody.linearVelocity -= dir * radialOut;
                        hookBody.angularVelocity = 0f;
                        ropeLength = currentMaxRange;
                        rope.SetLength(ropeLength);
                        rope.SolveFixed(dt, origin, rope.SegmentCount, null, hookBody.position);

                        // 绷住的第一帧不转收绳：OnCollisionEnter2D 在 FixedUpdate 之后才跑，
                        // 当帧就切成 ReelIn 的话，紧接着到来的合法地形碰撞会被 OnHookTerrainHit
                        // 的 phase 判断丢掉 —— 贴着射程边缘打墙会「明明碰到了却只是收绳」。
                        // 留一整步给碰撞回调，连续第二帧还绷着才真的收。
                        if (ropeTaut) StartReelIn();
                        else ropeTaut = true;
                        break;
                    }

                    ropeTaut = false;
                    rope.SetLength(hookDist);
                    rope.SolveFixed(dt, origin, rope.SegmentCount, null, hookBody.position);

                    CutChainsNearHook();
                    if (DetectCarriable()) return;
                    break;

                case RopePhase.ReelIn:
                    // 收绳 = 绳子在收：绳长按 reelSpeed 缩短，钩子挂在绳端被拖回来（带重力下垂）
                    if (!reelTimer.IsRunning) { Finish(); break; }      // 卡死角超时：强制回收

                    ropeLength = Mathf.Max(0f, ropeLength - reelSpeed * dt);
                    var reel = RopeCfg();
                    rope.Configure(reel);
                    rope.SetLength(ropeLength);
                    rope.SolveFixed(dt, origin, rope.SegmentCount, hookBody, null);
                    if (ropeLength <= detachDistance
                        || Vector2.Distance(origin, hookBody.position) <= detachDistance)
                        Finish();
                    break;

                case RopePhase.PullingEat:
                    // 接口没有 Unity 的假 null 重载，先转 MonoBehaviour 再判：认得出被炸毁的目标
                    if (grapple as MonoBehaviour == null) { Finish(); break; }
                    pullTarget = grapple.transform.position;

                    float eatDist = PullStep(pullTarget, dt);

                    if (eatDist <= swallowDistance)
                    {
                        if (grapple.TrySwallowByRope(transform)) Finish();
                        else Finish();      // 吞不下（嘴满等）：松绳，别卡着
                        break;
                    }

                    if (pullStuck >= stuckTime) Finish();   // 目标在墙后够不到就超时松绳
                    break;

                case RopePhase.Pulling:
                    float dist = PullStep(pullTarget, dt);

                    // 到达接触点（被墙挡住时中心距墙≈0.5）：松绳、保留动量
                    if (dist <= arrivalDistance) { Finish(); break; }
                    if (pullStuck >= stuckTime) Finish();   // 卡死兜底：贴墙滑动中距离在降，不算卡死
                    break;
            }
        }

        // 拉取单步推进（地形 Pulling 与抓可叼物 PullingEat 共用）：
        // 硬速度写回（压过重力、直线）+ 绷紧绳索 + 卡死距离推进。
        // 返回当前到目标的距离，终点判定由调用方做。
        private float PullStep(Vector2 target, float dt)
        {
            Vector2 to = target - playerBody.position;
            float dist = to.magnitude;
            if (dist < 0.0001f) return dist;

            // 玩家重力 3x（≈29.4 m/s²）会把加速度式拉取彻底压住（朝上根本拉不动），
            // 硬写才能压过重力、路径近似直线
            playerBody.linearVelocity = to / dist * pullSpeed;

            // 绳索绷紧：拉取阶段用低重力配置解算，近乎直线；飞行/收绳仍用重绳下垂
            var taut = RopeCfg();
            taut.gravityScale = tautRopeGravity;
            rope.Configure(taut);
            rope.SetLength(dist);
            rope.SolveFixed(dt, target, rope.SegmentCount, null, playerBody.position);

            // 卡死推进：距离不再下降（贴墙滑动中距离在降，不算卡死）→ 累计超时松绳
            if (dist < lastPullDist - 0.01f) pullStuck = 0f;
            else pullStuck += dt;
            lastPullDist = dist;
            return dist;
        }

        // 飞行中逐段检测可叼物：钩子物理上不碰 Default 层的物体（炸弹等），靠探测找。
        // 探测不限层 —— TryGetComponent 认接口，Bomb（直接实现）和 CarriablePart（框架物）都命中
        private bool DetectCarriable()
        {
            Collider2D[] hits = Physics2D.OverlapCircleAll(
                hookBody.position, bombDetectRadius + bulletRadius);
            foreach (Collider2D h in hits)
            {
                if (h.TryGetComponent(out ICarriable carriable))
                {
                    StartPullingCarriable(carriable);
                    return true;
                }
            }
            return false;
        }

        // 飞行中检测炸弹悬挂链：点到线段距离 < 半径即在最近段切断
        private void CutChainsNearHook()
        {
            Vector2 p = hookBody.position;
            float maxDist = chainCutRadius + bulletRadius;
            foreach (Chain chain in Chain.Active)
            {
                int seg = chain.NearestSegment(p, maxDist);
                if (seg >= 0) chain.CutAt(seg);
            }
        }

        // 瞄准预览：准星 = 自由光标（以玩家为圆心）；弹道 = 解出穿过准星的初速度的抛物线，
        // 逐段射线检测地形 —— 地形先于准星挡住 → 虚线截断在命中点（绿 = 会钩住）；
        // 畅通 → 虚线画到准星（红 = 会打空，收绳）
        private void UpdatePreview()
        {
            Vector2 aim = aimOffset.sqrMagnitude > 0.0001f ? aimOffset.normalized : Vector2.right;

            // 与发射完全同源的枪口起点：预览和实际弹道严格一致
            Vector2 origin = playerBody.position + aim * muzzleOffset;
            Vector2 target = playerBody.position + aimOffset;
            Vector2 g = Physics2D.gravity * bulletGravityScale;

            SolveBallistic(origin, target, launchSpeed, g, out Vector2 v0, out float flightTime);

            float maxT = Mathf.Max(flightTime, previewStepDt);
            arcPoints[0] = origin;
            arcCount = 1;
            Vector2 last = origin;
            bool hit = false;

            // 采样到越过准星一小段（准星之后还有惯性飞行，可能继续命中更远的地形）
            for (int i = 1; i < arcPoints.Length; i++)
            {
                float t = i * previewStepDt;
                if (t > maxT + 0.25f) break;
                Vector2 p = origin + v0 * t + 0.5f * g * t * t;
                arcPoints[arcCount++] = p;

                Vector2 seg = p - last;
                float segLen = seg.magnitude;
                if (segLen > 0.0001f)
                {
                    RaycastHit2D rh = Physics2D.Raycast(last, seg / segLen, segLen, hitMask);
                    if (rh.collider != null)
                    {
                        arcPoints[arcCount - 1] = rh.point;
                        hit = true;
                        break;
                    }
                }

                if (Vector2.Distance(origin, p) >= currentMaxRange) break;
                last = p;
            }

            // 准星 = 自由光标本身；颜色提示：绿 = 弹道会被地形截住（会钩住），红 = 会打空
            reticle.position = target;
            reticleSprite.color = hit ? hitColor : missColor;
            float s = crosshairSize * (hit ? 1.25f : 1f);
            reticle.localScale = new Vector3(s, s, 1f);

            // 虚线：抛物线采样点；贴图按单位密度铺开
            float totalLen = 0f;
            for (int i = 1; i < arcCount; i++) totalLen += Vector2.Distance(arcPoints[i - 1], arcPoints[i]);
            dashLine.positionCount = arcCount;
            for (int i = 0; i < arcCount; i++) dashLine.SetPosition(i, arcPoints[i]);
            dashLine.textureScale = new Vector2(
                dashUnitScale > 0.001f ? totalLen / dashUnitScale : totalLen, 1f);
        }

        /// <summary>
        /// 弹道解算：给定起点与目标点、初速大小与重力，求能让弹体穿过目标点的初速度。
        /// 令 u = t²，代入抛物线方程整理成关于 u 的二次方程，取低抛根（较小的 t）。
        /// t 钳制下限防目标过近时算出爆速。
        /// </summary>
        private void SolveBallistic(Vector2 origin, Vector2 target, float speed,
                                    Vector2 g, out Vector2 velocity, out float flightTime)
        {
            float dx = target.x - origin.x;
            float dy = target.y - origin.y;
            float gy = g.y;                 // 负值（向下）

            // 0.25·g²·u² − (dy·g + v²)·u + (dx² + dy²) = 0
            float a = 0.25f * gy * gy;
            float b = -(dy * gy + speed * speed);
            float c = dx * dx + dy * dy;

            float u;
            if (a > 1e-8f && b * b >= 4f * a * c)
            {
                float disc = Mathf.Sqrt(b * b - 4f * a * c);
                u = (-b - disc) / (2f * a);     // 低抛根
                u = Mathf.Max(u, 0f);
            }
            else
            {
                u = 0f;                          // 目标不可达（不会发生：光标被钳在射程内）
            }

            flightTime = Mathf.Max(Mathf.Sqrt(u), 0.05f);
            velocity = new Vector2(
                dx / flightTime,
                dy / flightTime - 0.5f * gy * flightTime);
        }

        void LateUpdate()
        {
            if (phase == RopePhase.Idle) return;

            // 挂在可叼物上的钩子跟着目标走（PullingEat 时钩子物理已关，手动同步）
            if (hookGo != null && hookBody != null && !hookBody.simulated && phase == RopePhase.PullingEat)
                hookGo.transform.position = pullTarget;

            // 绳索渲染
            ropeLine.positionCount = rope.SegmentCount + 1;
            for (int i = 0; i <= rope.SegmentCount; i++)
                ropeLine.SetPosition(i, rope.GetPoint(i));
        }

        // ---- 总线回调 ----

        private void OnRangeOverride(float range)
        {
            currentMaxRange = Mathf.Max(0.1f, range);
        }

        private void OnRangeRestored()
        {
            currentMaxRange = maxRange;
        }

        private void OnDied(DeathContext ctx)
        {
            if (ctx.Victim != gameObject) return;

            // Finish 幂等：解抓取标记、解锁移动、清状态、销毁钩子，一次做完
            Finish();
            // 死亡期间 Update 被 IsDead 拦住，预览的显隐门控不会跑 —— 必须在这里显式藏掉
            SetPreviewShown(false);
        }

        private void OnRespawned(GameObject victim, Vector2 pos)
        {
            if (victim != gameObject) return;
            aimOffset = (PlayerBus.Face == FaceDirection.R ? Vector2.right : Vector2.left) * currentMaxRange * 0.6f;
            // 预览由 Update 的输入计时器决定：无输入保持隐藏
        }

        // ---- 运行时物件 ----

        private Transform CreateFx(string name, out SpriteRenderer sprite, Sprite fallback, int sortingOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            sprite = go.AddComponent<SpriteRenderer>();
            sprite.sprite = fallback;
            sprite.sortingOrder = sortingOrder;
            return go.transform;
        }

        private LineRenderer CreateLine(string name, Material material, float width, int sortingOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.alignment = LineAlignment.TransformZ;
            line.loop = false;
            line.widthMultiplier = width;
            line.sortingOrder = sortingOrder;
            line.material = material;   // null = 默认纯色
            line.positionCount = 0;
            line.enabled = false;
            return line;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.9f, 0.2f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, maxRange);
        }

        // 钩子自身的碰撞回调：excludeLayers 保证只碰地形（Terrain|Breakable），不碰玩家/炸弹
        private class HookHit : MonoBehaviour
        {
            private RopeGun owner;

            public void Init(RopeGun ropeGun) => owner = ropeGun;

            void OnCollisionEnter2D(Collision2D collision) => owner.OnHookTerrainHit(collision);
        }
    }
}
