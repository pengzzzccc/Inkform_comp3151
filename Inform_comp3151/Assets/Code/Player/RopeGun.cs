using Inkform.Bus;
using Inkform.Item;
using Inkform.Life;
using Inkform.Tool;
using UnityEngine;

namespace Inkform.Player
{
    /// <summary>绳索枪模式。右键 / 左肩键切换，切换音效由 RopeGunBus.ModeChanged 驱动。</summary>
    public enum GrappleMode { Retract, Hang }

    /// <summary>
    /// 绳索枪：替换 dash 成为主武器（dash 移到 Shift）。
    ///
    /// 瞄准：鼠标 delta / 右摇杆（Look 动作）漂移瞄准游标 → 瞄准方向；准星显示预测落点 ——
    /// 用与子弹完全一致的参数解析抛物线并逐段射线检测（Terrain|Breakable），射程内命中则
    /// 准星在命中点（绿色命中提示），否则在射程末端（红色）。虚线 = 抛物线采样，准星必在线上。
    ///
    /// 飞行：子弹是真实刚体（重力、初速沿瞄准方向、阻尼 0 与预览一致，includeLayers 只碰地形）。
    /// 命中：收缩模式把玩家拉向锚点；悬挂模式把玩家挂在锚点上荡（绳长 = 命中时距离，锁移动，
    /// 跳跃脱离）。未命中 / 取消：收绳 —— 锚点 = 枪口，末端附子弹刚体把它拉回来（和炸弹悬挂链
    /// 同一套 Verlet 解算）。
    ///
    /// 特殊命中：
    /// ① 炸弹：把玩家拉向炸弹，到达后吞下（Bomb.TrySwallowByRope）；
    /// ② 炸弹悬挂链：在命中点切断（Chain.CutAt，靠 Chain 静态注册表逐段检测）。
    ///
    /// 射程：默认 maxRange，特殊区域（RopeRangeZone）通过 RopeGunBus 覆盖/恢复。
    /// 挂在 Player 上；没挂时游戏照常玩（PlayerHandler 用 TryGetComponent 探测）。
    /// </summary>
    public class RopeGun : MonoBehaviour
    {
        private enum RopePhase { Idle, Flying, ReelIn, Pulling, Swinging, PullingBomb }

        [Header("Range")]
        [SerializeField] private float maxRange = 4.2f;                     // 默认最大射程（也是绳索长度上限）
        [SerializeField] private LayerMask hitMask = (1 << 6) | (1 << 11);  // Terrain | Breakable

        [Header("Projectile")]
        [SerializeField] private float launchSpeed = 22f;
        [SerializeField] private float bulletGravityScale = 1f;
        [SerializeField] private float bulletRadius = 0.12f;
        [SerializeField] private Sprite bulletSprite;                       // 可替换
        [SerializeField] private float bombDetectRadius = 0.55f;            // 钩子飞行中探测炸弹的半径

        [Header("Aim")]
        [SerializeField] private float mouseAimSensitivity = 1f;
        [SerializeField] private float stickAimSpeed = 5f;
        [SerializeField] private float aimMaxOffset = 7f;                   // 瞄准游标离玩家的最大距离
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
        [SerializeField] private Material ropeMaterial;
        [SerializeField] private float ropeWidth = 0.06f;
        [SerializeField] private int ropeSortingOrder = -5;
        [SerializeField] private float chainCutRadius = 0.35f;              // 钩子离炸弹链段多近算切断

        [Header("Pull")]
        [SerializeField] private float pullAccel = 40f;                     // 拉向目标的速度加速度
        [SerializeField] private float pullSpeed = 16f;                     // 拉取 / 收绳的目标速度
        [SerializeField] private float swallowDistance = 0.75f;             // 距炸弹多近触发吞下
        [SerializeField] private float detachDistance = 0.5f;               // 收缩模式拉到多近松绳

        private RopePhase phase = RopePhase.Idle;
        private GrappleMode mode = GrappleMode.Retract;
        private float currentMaxRange;

        private Rigidbody2D playerBody;
        private PlayerMotor motor;

        private Vector2 aimOffset;      // 瞄准游标相对玩家的偏移（世界单位），只决定方向
        private Vector2 aimDir = Vector2.right;

        private GameObject hookGo;
        private Rigidbody2D hookBody;
        private HookHit hookHit;

        private Bomb grappleBomb;       // PullingBomb 的目标
        private Vector2 pullTarget;     // 锚点：地形命中点 / 炸弹当前位置

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

        public bool IsSwinging => phase == RopePhase.Swinging;

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
        /// 否则是右摇杆模拟量（速度 × dt）。</summary>
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
            }
            else
            {
                aimOffset += delta * (stickAimSpeed * Time.deltaTime);
            }

            if (aimOffset.sqrMagnitude > aimMaxOffset * aimMaxOffset)
                aimOffset = aimOffset.normalized * aimMaxOffset;
        }

        public void TryFire()
        {
            if (LifeBus.IsDead) return;
            if (phase != RopePhase.Idle)
            {
                Cancel();           // 再次按下 = 取消本次发射 / 松绳
                return;
            }
            if (ItemBus.Held != null) return;   // 嘴里叼着炸弹射不了

            aimDir = aimOffset.sqrMagnitude > 0.0001f ? aimOffset.normalized : Vector2.right;

            phase = RopePhase.Flying;
            SetPreviewShown(false);

            Vector2 origin = playerBody.position;
            Vector2 v0 = aimDir * launchSpeed;

            hookGo = new GameObject("GrappleHook");
            hookGo.transform.position = origin;
            hookBody = hookGo.AddComponent<Rigidbody2D>();
            hookBody.gravityScale = bulletGravityScale;
            hookBody.linearDamping = 0f;                // 与预览解析一致，抛物线才吻合
            hookBody.includeLayers = hitMask;           // 只碰地形：玩家 / 炸弹靠别的通道判定
            var col = hookGo.AddComponent<CircleCollider2D>();
            col.radius = bulletRadius;
            var sr = hookGo.AddComponent<SpriteRenderer>();
            sr.sprite = bulletSprite != null ? bulletSprite : DiscSprite;
            sr.sortingOrder = 15;
            hookBody.linearVelocity = v0;
            hookHit = hookGo.AddComponent<HookHit>();
            hookHit.Init(this);

            rope.Init(ropeSettings, origin, origin);
            ropeLine.enabled = true;
        }

        public void ToggleMode()
        {
            mode = mode == GrappleMode.Retract ? GrappleMode.Hang : GrappleMode.Retract;
            RopeGunBus.RaiseModeChanged(mode);
        }

        /// <summary>跳跃键按下时由 PlayerHandler 调：悬挂 / 拉取中松绳并让跳跃生效。</summary>
        public void DetachOnJump()
        {
            if (phase == RopePhase.Idle || phase == RopePhase.ReelIn || phase == RopePhase.Flying) return;
            Finish();
        }

        // ---- 内部流程 ----

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
                    Finish();                       // 悬挂 / 拉取中：直接松绳回收
                    return;
            }
        }

        private void OnHookTerrainHit(Collision2D collision)
        {
            if (phase != RopePhase.Flying) return;

            Vector2 hitPoint = collision.GetContact(0).point;
            if (Vector2.Distance(playerBody.position, hitPoint) > currentMaxRange)
            {
                StartReelIn();      // 超出射程：当未命中处理
                return;
            }

            pullTarget = hitPoint;
            hookBody.simulated = false;
            hookHit.enabled = false;
            if (hookGo.TryGetComponent(out CircleCollider2D col)) col.enabled = false;   // 锚点不再参与碰撞
            motor?.SetMoveLocked(true);

            if (mode == GrappleMode.Retract)
            {
                phase = RopePhase.Pulling;
            }
            else
            {
                phase = RopePhase.Swinging;
                rope.Init(ropeSettings, pullTarget, playerBody.position);    // 绳长 = 命中时距离
            }
        }

        private void StartReelIn()
        {
            if (phase == RopePhase.ReelIn) return;
            phase = RopePhase.ReelIn;
            // 收绳阶段关掉碰撞：避免钩子被角落卡住永远拉不回来（会穿墙拉回）
            if (hookGo != null && hookGo.TryGetComponent(out CircleCollider2D col)) col.enabled = false;
        }

        private void StartPullingBomb(Bomb bomb)
        {
            bomb.MarkRopeGrappled();
            grappleBomb = bomb;
            phase = RopePhase.PullingBomb;
            hookBody.simulated = false;
            hookHit.enabled = false;
            if (hookGo.TryGetComponent(out CircleCollider2D col)) col.enabled = false;
            motor?.SetMoveLocked(true);
        }

        private void Finish()
        {
            if (grappleBomb != null)
            {
                grappleBomb.ClearRopeGrappled();
                grappleBomb = null;
            }
            motor?.SetMoveLocked(false);
            phase = RopePhase.Idle;
            DespawnHook();
            ropeLine.enabled = false;
            SetPreviewShown(true);
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
            if (reticle != null) reticle.gameObject.SetActive(shown);
            dashLine.enabled = shown;
        }

        // ---- 帧循环 ----

        void Update()
        {
            if (LifeBus.IsDead) return;
            if (phase == RopePhase.Idle) UpdatePreview();
        }

        void FixedUpdate()
        {
            if (phase == RopePhase.Idle || LifeBus.IsDead) return;

            float dt = Time.fixedDeltaTime;
            Vector2 origin = playerBody.position;

            switch (phase)
            {
                case RopePhase.Flying:
                    // 绳索跟随子弹：每步重铺（锚点 = 枪口，末端钉在子弹上），拉直不产生绳段垃圾
                    rope.Init(ropeSettings, origin, hookBody.position);
                    rope.SolveFixed(dt, origin, rope.SegmentCount, null, hookBody.position);

                    CutChainsNearHook();
                    if (DetectBomb()) return;

                    if (Vector2.Distance(origin, hookBody.position) >= currentMaxRange)
                        StartReelIn();
                    break;

                case RopePhase.ReelIn:
                    // 收绳：锚点 = 枪口，末端附子弹刚体 —— 和炸弹悬挂链同款「拉回」解算
                    var reel = ropeSettings;
                    reel.maxCorrectionSpeed = pullSpeed;
                    rope.Init(reel, origin, hookBody.position);
                    rope.SolveFixed(dt, origin, rope.SegmentCount, hookBody, null);
                    if (Vector2.Distance(origin, hookBody.position) <= detachDistance)
                        Finish();
                    break;

                case RopePhase.Pulling:
                    PullPlayerToward(pullTarget, dt);
                    rope.Init(ropeSettings, pullTarget, playerBody.position);
                    rope.SolveFixed(dt, pullTarget, rope.SegmentCount, null, playerBody.position);
                    if (Vector2.Distance(origin, pullTarget) <= detachDistance)
                        Finish();
                    break;

                case RopePhase.PullingBomb:
                    if (grappleBomb == null) { Finish(); break; }   // 炸弹被炸没了
                    pullTarget = grappleBomb.transform.position;
                    PullPlayerToward(pullTarget, dt);
                    rope.Init(ropeSettings, pullTarget, playerBody.position);
                    rope.SolveFixed(dt, pullTarget, rope.SegmentCount, null, playerBody.position);
                    if (Vector2.Distance(origin, pullTarget) <= swallowDistance)
                    {
                        if (grappleBomb.TrySwallowByRope(transform)) Finish();
                        else Finish();      // 吞不下（嘴满等）：松绳，别卡着
                    }
                    break;

                case RopePhase.Swinging:
                    rope.SolveFixed(dt, pullTarget, rope.SegmentCount, playerBody, null);
                    break;
            }
        }

        // 飞行中逐段检测炸弹：钩子物理上不碰 Default 层，靠探测找炸弹
        private bool DetectBomb()
        {
            Collider2D[] hits = Physics2D.OverlapCircleAll(
                hookBody.position, bombDetectRadius + bulletRadius);
            foreach (Collider2D h in hits)
            {
                if (h.TryGetComponent(out Bomb bomb))
                {
                    StartPullingBomb(bomb);
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

        private void PullPlayerToward(Vector2 target, float dt)
        {
            Vector2 toTarget = target - playerBody.position;
            float dist = toTarget.magnitude;
            if (dist < 0.0001f) return;

            Vector2 desired = toTarget / dist * pullSpeed;
            playerBody.linearVelocity = Vector2.MoveTowards(playerBody.linearVelocity, desired, pullAccel * dt);
        }

        // 瞄准预览：用与子弹一致的参数解析抛物线，逐段射线检测地形，准星放预测落点
        private void UpdatePreview()
        {
            aimDir = aimOffset.sqrMagnitude > 0.0001f ? aimOffset.normalized : Vector2.right;

            Vector2 origin = playerBody.position;
            Vector2 v0 = aimDir * launchSpeed;
            Vector2 g = Physics2D.gravity * bulletGravityScale;

            arcPoints[0] = origin;
            arcCount = 1;
            Vector2 last = origin;
            bool hit = false;
            Vector2 end = origin;

            for (int i = 1; i < arcPoints.Length; i++)
            {
                float t = i * previewStepDt;
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
                        end = rh.point;
                        hit = true;
                        break;
                    }
                }

                if (Vector2.Distance(origin, p) >= currentMaxRange)
                {
                    end = p;
                    break;
                }
                last = p;
            }

            // 准星：命中绿色 / 未命中红色，命中时稍放大
            reticle.position = end;
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

        void LateUpdate()
        {
            if (phase == RopePhase.Idle) return;

            // 挂在炸弹上的钩子跟着炸弹走（PullingBomb 时钩子物理已关，手动同步）
            if (hookGo != null && hookBody != null && !hookBody.simulated && phase == RopePhase.PullingBomb)
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
            if (phase != RopePhase.Idle)
            {
                if (grappleBomb != null) { grappleBomb.ClearRopeGrappled(); grappleBomb = null; }
                motor?.SetMoveLocked(false);
                phase = RopePhase.Idle;
                DespawnHook();
                ropeLine.enabled = false;
            }
            SetPreviewShown(false);
        }

        private void OnRespawned(GameObject victim, Vector2 pos)
        {
            if (victim != gameObject) return;
            aimOffset = (PlayerBus.Face == FaceDirection.R ? Vector2.right : Vector2.left) * currentMaxRange * 0.6f;
            SetPreviewShown(true);
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

        // 钩子自身的碰撞回调：只对 includeLayers（Terrain|Breakable）生效
        private class HookHit : MonoBehaviour
        {
            private RopeGun owner;

            public void Init(RopeGun ropeGun) => owner = ropeGun;

            void OnCollisionEnter2D(Collision2D collision) => owner.OnHookTerrainHit(collision);
        }
    }
}
