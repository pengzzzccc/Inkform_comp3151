using Inkform.Bus;
using Inkform.player;
using UnityEngine;

/// <summary>
/// 程序化挤压拉伸：起跳拉长、落地压扁、空中随垂直速度形变，用弹簧回弹。
/// 精灵动画是逐帧换图的，Animator 没法在帧之间插值（CrossFade 对 PPtr 曲线不生效），
/// 空中那一段又只有一张静图 —— 靠这个跑在满帧率上的形变把它撑起来，是本类存在的全部理由。
/// 必须挂在只管显示的 Visual 子物体上：写根物体的 localScale 会连碰撞体和四个探测点一起缩，
/// 落地压得最扁那一瞬正好把 CheckGround 抬高，地面判定会闪。
/// </summary>
public class PlayerSquash : MonoBehaviour
{
    [Header("Airborne stretch")]
    [SerializeField] private float stretchRefSpeed = 16f;   // 垂直速度到这个值时拉伸到满
    [SerializeField] private float maxStretch = 0.18f;      // 满拉伸时 y 的增量比例

    [Header("Impulse")]
    [SerializeField] private float jumpStretch = 0.22f;     // 起跳瞬间额外拉长
    [SerializeField] private float landSquash = 0.28f;      // 落地瞬间压扁

    [Header("Spring")]
    [SerializeField] private float stiffness = 220f;
    [SerializeField] private float damping = 18f;
    // 安全上限：offset 到 -1 时 1+offset 为 0，体积守恒的那个除法会炸成无穷大
    [SerializeField][Range(0.1f, 0.9f)] private float maxOffset = 0.6f;

    private Rigidbody2D body;
    private PlayerState prevState;

    private float offset;   // 当前形变量：正 = 拉长，负 = 压扁
    private float vel;      // 形变量的变化速度，脉冲直接加在它上面

    void Awake()
    {
        // 刚体在根物体上，本组件在 Visual 子物体上
        body = GetComponentInParent<Rigidbody2D>();
        prevState = PlayerBus.State;
    }

    void OnEnable()
    {
        PlayerBus.StateChanged += OnState;
        LifeBus.Died += OnReset;
        LifeBus.Respawned += OnReset;
    }

    void OnDisable()
    {
        PlayerBus.StateChanged -= OnState;
        LifeBus.Died -= OnReset;
        LifeBus.Respawned -= OnReset;

        // 保险：被禁用时把形变还原，免得停在压扁的姿势上
        ResetNow();
    }

    // 用 LateUpdate：PlayerHandler 在 Update 里已经把本帧速度定好了，这里读到的才是最终值
    void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;       // hitstop 期间 timeScale = 0，形变跟着冻住

        float target = 0f;
        if (body != null && IsAirborne(PlayerBus.State))
            target = Mathf.Clamp(body.linearVelocityY / stretchRefSpeed, -1f, 1f) * maxStretch;

        // 半隐式弹簧：先按劲度加速，再按阻尼衰减速度，最后积分位移。
        // 阻尼用 Exp 而不是 (1 - damping*dt)，后者在 dt 偏大时会算出负数、直接发散
        vel += (target - offset) * stiffness * dt;
        vel *= Mathf.Exp(-damping * dt);
        offset = Mathf.Clamp(offset + vel * dt, -maxOffset, maxOffset);

        Apply();
    }

    private void Apply()
    {
        // 体积守恒：拉长的同时变窄。用 1 - offset 当宽度会显得像单纯放大，
        // 只有反比例的这个写法读起来才是「被拉扯」而不是「变大了」
        float sy = 1f + offset;
        transform.localScale = new Vector3(1f / sy, sy, 1f);
    }

    private void OnState(PlayerState state)
    {
        // 落地：往下砸一下
        if (state == PlayerState.Land) vel -= landSquash;

        // 起跳：离地那一下往上抽。JumpUp 和 Rise 都要接 —— PlayerHandler 里
        // 进 JumpUp 还要求 |linearVelocityX| > 0.3，原地直跳根本不经过 JumpUp
        else if ((state == PlayerState.JumpUp || state == PlayerState.Rise) && IsGrounded(prevState))
            vel += jumpStretch;

        prevState = state;
    }

    // 死亡/复活时复位：死亡瞬间若正压扁着，PlayerDeathFx 拿 sprite.bounds
    // 切出来的碎块网格会跟着变形；复活后也不该顶着上一条命的姿势出现
    private void OnReset(GameObject victim, Vector2 pos) => ResetNow();

    private void ResetNow()
    {
        offset = 0f;
        vel = 0f;
        transform.localScale = Vector3.one;
    }

    private static bool IsAirborne(PlayerState s) =>
        s == PlayerState.JumpUp || s == PlayerState.Rise || s == PlayerState.Fall
        || s == PlayerState.WallSlideL || s == PlayerState.WallSlideR;

    private static bool IsGrounded(PlayerState s) =>
        s == PlayerState.Idle || s == PlayerState.Move || s == PlayerState.Land;
}
