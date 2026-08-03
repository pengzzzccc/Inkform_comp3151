using Inkform.Bus;
using Inkform.Life;
using Inkform.Tool;
using UnityEngine;

namespace Inkform.Player
{
    /// <summary>
    /// 玩家协调者：接收输入、按固定顺序驱动各子系统、响应死亡与复活。
    /// 具体职责已拆给同物体上的四个组件 ——
    /// ContactSensor（四向接触）、PlayerMotor（运动学）、AnimStateResolver（动画推导）、ItemCarrier（叼东西）。
    /// 绳索枪（RopeGun）是可选的第五个组件：没挂也能正常玩，挂了才接线（TryGetComponent）。
    ///
    /// 之所以保留本类而不让 InputHandler 直接找 PlayerMotor：
    /// ① InputHandler.player 是在 GameManager 预制体的场景实例覆盖里接线的，改**类名**会静默断线
    ///    （那是按组件类型序列化的字段引用）；下面那批输入入口只是普通 C# 调用，改方法名
    ///    两边一起改就行，编译器会兜住；
    /// ② 各子系统的 Update 顺序必须是「感知 → 运动 → 动画」，而同物体上组件的 Update
    ///    顺序 Unity 不保证 —— 只能由一个驱动者显式排好，那就是本类。
    /// </summary>
    // Rigidbody2D 不用在这里声明：PlayerMotor 已经 RequireComponent 了它
    [RequireComponent(typeof(ContactSensor))]
    [RequireComponent(typeof(PlayerMotor))]
    [RequireComponent(typeof(AnimStateResolver))]
    public class PlayerHandler : MonoBehaviour
    {
        private ContactSensor contact;
        private PlayerMotor motor;
        private AnimStateResolver anim;
        private ItemCarrier items;      // 允许为 null：不带道具玩法的关卡可以不挂
        private RopeGun ropeGun;        // 允许为 null：没挂绳索枪的关卡照常玩

        private Vector2 lastMoveInput;  // 最近一帧的移动输入（WASD / 左摇杆）：无绳索枪时吐炸弹的方向兜底

        void Awake()
        {
            contact = GetComponent<ContactSensor>();
            motor = GetComponent<PlayerMotor>();
            anim = GetComponent<AnimStateResolver>();
            TryGetComponent(out items);
            TryGetComponent(out ropeGun);

            // 初始广播一次，让总线快照从一开始就是正确的
            PlayerBus.RaiseState(PlayerState.Idle);
            PlayerBus.RaiseFace(FaceDirection.R);
        }

        void OnEnable()
        {
            HazardBus.Exploded += OnExploded;
            LifeBus.Died += OnDied;
            LifeBus.Respawned += OnRespawned;
        }

        void OnDisable()
        {
            HazardBus.Exploded -= OnExploded;
            LifeBus.Died -= OnDied;
            LifeBus.Respawned -= OnRespawned;
        }

        void Update()
        {
            if (LifeBus.IsDead) return;     // 死亡期间彻底停摆：接触检测、重力、跳跃、动画全停

            // 顺序不能动：动画要读的接触与速度都得是本帧最新的，
            // 否则会慢一帧、在落地和起跳的瞬间闪错动画
            contact.Tick();
            motor.Tick();
            if (motor.ConsumeJumpStarted()) anim.OnJumpStarted();
            anim.Tick();
        }

        // ---- 输入入口。名字与 InputSystem_Actions 资产里的动作一一对应，改动作名时一起改 ----

        /// <summary>Move 动作（WASD / 左摇杆）。</summary>
        public void Move(Vector2 input)
        {
            if (LifeBus.IsDead) return;     // 死了不改朝向也不给速度

            lastMoveInput = input;          // 吐炸弹方向兜底来源
            ropeGun?.SetMoveInput(input);   // 绳索枪：无瞄准输入时的发射方向来源

            // 朝向：输入永远最高优先级（移动锁定期间也生效）；无输入则保持当前朝向
            if (input.x > 0.01f) anim.SetFace(FaceDirection.R);
            else if (input.x < -0.01f) anim.SetFace(FaceDirection.L);

            anim.SetMoveInput(input);       // 只驱动动画，锁定期间也跟着输入走
            motor.Move(input);              // 是否被锁由 motor 自己判
        }

        /// <summary>Jump 动作按下（Space / A）。</summary>
        public void JumpPressed()
        {
            if (LifeBus.IsDead) return;

            // 绳索枪悬挂中：跳跃 = 松绳 + 正常起跳
            if (ropeGun != null) ropeGun.DetachOnJump();

            motor.RequestJump();
        }

        /// <summary>Jump 动作松开：切掉上升速度，按住越久跳越高。</summary>
        public void JumpReleased()
        {
            if (LifeBus.IsDead) return;

            motor.CutJump();
        }

        /// <summary>Dash 动作（LeftShift / X）。</summary>
        public void Dash()
        {
            if (LifeBus.IsDead) return;

            // 纯冲刺：不播吃动画、不碰道具（dash 碰到炸弹只会爆，吐炸弹走 Q / 右扳机）
            float dir = PlayerBus.Face == FaceDirection.R ? 1f : -1f;
            motor.Dash(dir);
        }

        // ---- 绳索枪输入入口 ----

        /// <summary>Aim 动作（鼠标 delta / 右摇杆）：驱动绳索枪准星。</summary>
        public void Aim(Vector2 delta, bool pixelDelta) => ropeGun?.Aim(delta, pixelDelta);

        /// <summary>RopeFire 动作（鼠标左键 / RB）：发绳，再按一次取消。</summary>
        public void RopeFire()
        {
            if (LifeBus.IsDead) return;
            ropeGun?.TryFire();
        }

        /// <summary>SpitBomb 动作（Q / 右扳机）：吐炸弹，方向 = 绳索枪有效发射方向
        /// （有瞄准输入朝准星，否则移动方向上倾 30°）；无绳索枪时退回移动输入 8 向，再退回面朝方向。</summary>
        public void SpitBomb()
        {
            if (LifeBus.IsDead) return;
            if (items == null || items.IsEmpty) return;

            Vector2 dir;
            if (ropeGun != null)
                dir = ropeGun.EffectiveFireDir;
            else if (lastMoveInput.sqrMagnitude > 0.0001f)
                dir = Dir8.Snap(lastMoveInput);
            else
                dir = PlayerBus.Face == FaceDirection.R ? Vector2.right : Vector2.left;

            if (items.TryRelease(dir))
            {
                anim.SetFace(dir.x >= 0f ? FaceDirection.R : FaceDirection.L);
                // 吐不播动画（Release 动画已清理）
            }
        }

        // ---- 总线回调 ----

        // 由 HazardBus 在爆炸时回调：沿「爆心 → 自己」的 8 向之一弹开，并锁一小段移动输入
        private void OnExploded(GameObject victim, Vector2 center, float force)
        {
            if (victim != gameObject) return;

            motor.Knockback(Dir8.Snap((Vector2)transform.position - center) * force);
        }

        // 由 LifeBus 在自己死掉时回调：只停玩法，本体消失和碎块爆裂归死亡策略管
        private void OnDied(DeathContext ctx)
        {
            if (ctx.Victim != gameObject) return;

            motor.StopForDeath();
        }

        // 由 LifeBus 在复活时回调：放回检查点并把所有瞬时状态归零
        private void OnRespawned(GameObject victim, Vector2 pos)
        {
            if (victim != gameObject) return;

            motor.RespawnAt(pos);
            anim.ResetForRespawn();

            // 先探一次地面再对齐基准：不然复活在地上会被判成「刚落地」，
            // 白播一次 Land 动画和落地音
            contact.Tick();
            anim.SyncContactBaseline();
        }
    }
}
