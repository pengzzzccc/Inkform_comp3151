using Inkform.Bus;
using Inkform.Tool;
using UnityEngine;

namespace Inkform.Player
{
    /// <summary>
    /// 动画状态推导：每帧按优先级从接触状态和运动状态里算出「现在该播哪个动画」，并广播到 PlayerBus。
    /// 从 PlayerHandler 拆出来的第三层 —— 它只决定播什么，具体怎么播（选 clip、翻转）归 AniHandler。
    ///
    /// 注意这不是转移式状态机而是**推导式**的：每帧从头重算一遍，没有「从 A 只能到 B」的边。
    /// 所以这里适合的是优先级链而不是 State 模式 —— 13 个状态各拆一个类，
    /// 换来的只是把同一条链拆散到 13 个文件里，读起来反而更难看出优先级。
    ///
    /// [SerializeField] 默认值同样取自 Player.prefab 的实配值（landAnimTime 等）。
    /// 不自带 Update，由 PlayerHandler 最后调 Tick()。
    /// </summary>
    [RequireComponent(typeof(ContactSensor))]
    [RequireComponent(typeof(PlayerMotor))]
    public class AnimStateResolver : MonoBehaviour
    {
        [Header("One-shot anim durations")]
        [SerializeField] private float landAnimTime = 1f;
        [SerializeField] private float jumpUpAnimTime = 0.3f;
        [SerializeField] private float ceilingAttachTime = 0.25f;

        private ContactSensor contact;
        private PlayerMotor motor;

        private Timer landAnimTimer;
        private Timer jumpUpTimer;
        private Timer ceilingAttachTimer;

        private Vector2 moveInput;
        private bool prevOnGround;
        private bool prevOnCeiling;

        void Awake()
        {
            contact = GetComponent<ContactSensor>();
            motor = GetComponent<PlayerMotor>();
        }

        /// <summary>朝向。去重由 PlayerBus 负责，这里直接 Raise 即可。</summary>
        public void SetFace(FaceDirection face) => PlayerBus.RaiseFace(face);

        /// <summary>移动输入只驱动动画不参与物理 —— 锁定期间也要跟着输入走，
        /// 否则落地会错放 Move。</summary>
        public void SetMoveInput(Vector2 input) => moveInput = input;

        /// <summary>起跳一次性动画。由 PlayerHandler 从 PlayerMotor 取到起跳信号后转交。</summary>
        public void OnJumpStarted() => jumpUpTimer.Set(jumpUpAnimTime);

        /// <summary>复活时清掉死前攒下的一次性动画。
        /// 冲刺不再播动画后只剩落地/贴顶一次性，这里保留调用点以防将来再加。</summary>
        public void ResetForRespawn() { }

        /// <summary>把落地/贴顶的「上一帧」基准对齐到当前接触状态。
        /// 复活时必须在 ContactSensor.Tick() 之后调一次 —— 不然复活在地上会被判成
        /// 「刚落地」，白播一次 Land 动画和落地音。</summary>
        public void SyncContactBaseline()
        {
            prevOnGround = contact.OnGround;
            prevOnCeiling = contact.OnCeiling;
        }

        // 优先级：落地/贴顶一次性检测 > 吃/吐 > 天花板(动/静) > 墙侧下滑
        //         > 空中(JumpUp 一次性 → Rise/Fall) > 地面(Move/Idle)
        public void Tick()
        {
            // 落地/贴顶瞬间的一次性动画（起跳一次性由 OnJumpStarted 触发）
            if (!prevOnGround && contact.OnGround) landAnimTimer.Set(landAnimTime);
            prevOnGround = contact.OnGround;
            if (!prevOnCeiling && contact.OnCeiling) ceilingAttachTimer.Set(ceilingAttachTime);
            prevOnCeiling = contact.OnCeiling;

            if (contact.OnCeiling)
            {
                if (ceilingAttachTimer.IsRunning)
                    SetState(PlayerState.CeilingStick);              // 刚贴上：附着一次性
                else if (Mathf.Abs(moveInput.x) > 0.01f)
                    SetState(PlayerState.CeilingMove);               // 天花板移动
                else
                    SetState(PlayerState.CeilingIdle);               // 静止 = 上下翻转的 Idle
                return;
            }

            if (contact.OnWall && !contact.OnGround && motor.VelocityY < 1f)
            {
                if (contact.OnLeftWall) SetState(PlayerState.WallSlideL);
                if (contact.OnRightWall) SetState(PlayerState.WallSlideR);
                return;
            }

            if (!contact.OnGround)
            {
                // 起跳瞬间且有横向速度才播 JumpUp，纯垂直起跳直接进 Rise
                if (jumpUpTimer.IsRunning && (motor.VelocityX > 0.3f || motor.VelocityX < -0.3f))
                    SetState(PlayerState.JumpUp);
                else
                    SetState(motor.VelocityY > 0.1f
                        ? PlayerState.Rise               // 上升
                        : PlayerState.Fall);             // 下落
                return;
            }

            if (landAnimTimer.IsRunning) { SetState(PlayerState.Land); return; }  // 落地瞬间

            SetState(Mathf.Abs(moveInput.x) > 0.2f
                ? PlayerState.Move
                : PlayerState.Idle);
        }

        // 去重（只在变化时广播）由 PlayerBus 负责，这里直接 Raise 即可
        private void SetState(PlayerState state) => PlayerBus.RaiseState(state);
    }
}
