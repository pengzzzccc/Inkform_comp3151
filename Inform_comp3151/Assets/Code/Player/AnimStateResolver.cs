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
    /// [SerializeField] 默认值同样取自 Player.prefab 的实配值（attackAnimTime / landAnimTime 都是 1）。
    /// 不自带 Update，由 PlayerHandler 最后调 Tick()。
    /// </summary>
    [RequireComponent(typeof(ContactSensor))]
    [RequireComponent(typeof(PlayerMotor))]
    public class AnimStateResolver : MonoBehaviour
    {
        [Header("One-shot anim durations")]
        [SerializeField] private float attackAnimTime = 1f;      // Eat/Release 保持时长（与冲刺时长解耦）
        [SerializeField] private float landAnimTime = 1f;
        [SerializeField] private float jumpUpAnimTime = 0.3f;
        [SerializeField] private float ceilingAttachTime = 0.25f;

        private ContactSensor contact;
        private PlayerMotor motor;

        private Timer landAnimTimer;
        private Timer jumpUpTimer;
        private Timer ceilingAttachTimer;
        private Timer attackAnimTimer;

        private Vector2 moveInput;
        private bool prevOnGround;
        private bool prevOnCeiling;
        private bool swinging;          // 绳索枪悬挂中：状态压过一切接触推导

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

        /// <summary>绳索枪悬挂中：每帧由 PlayerHandler 在 Tick 前刷新，悬挂态压过接触推导。</summary>
        public void SetSwinging(bool value) => swinging = value;

        /// <summary>起跳一次性动画。由 PlayerHandler 从 PlayerMotor 取到起跳信号后转交。</summary>
        public void OnJumpStarted() => jumpUpTimer.Set(jumpUpAnimTime);

        /// <summary>吃 / 吐的一次性动画，松键不打断，到期后由 Tick 恢复移动动画。</summary>
        public void PlayAttack(bool releasing)
        {
            SetState(releasing ? PlayerState.Release : PlayerState.Eat);
            attackAnimTimer.Set(attackAnimTime);
        }

        /// <summary>复活时清掉死前攒下的一次性动画。</summary>
        public void ResetForRespawn() => attackAnimTimer.Clear();

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

            if (attackAnimTimer.IsRunning) return;   // Eat/Release 动画保持期间不打断

            if (swinging) { SetState(PlayerState.Swing); return; }   // 悬挂中：绳子主导，不推导接触

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
