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
    ///
    /// 之所以保留本类而不让 InputHandler 直接找 PlayerMotor：
    /// ① InputHandler.player 是在 GameManager 预制体的场景实例覆盖里接线的，改类名会静默断线；
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

        void Awake()
        {
            contact = GetComponent<ContactSensor>();
            motor = GetComponent<PlayerMotor>();
            anim = GetComponent<AnimStateResolver>();
            TryGetComponent(out items);

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

        // ---- 输入入口。方法名是 InputHandler 直接调的，改名会断线 ----

        public void playerMoving(Vector2 input)
        {
            if (LifeBus.IsDead) return;     // 死了不改朝向也不给速度

            // 朝向：输入永远最高优先级（移动锁定期间也生效）；无输入则保持当前朝向
            if (input.x > 0.01f) anim.SetFace(FaceDirection.R);
            else if (input.x < -0.01f) anim.SetFace(FaceDirection.L);

            anim.SetMoveInput(input);       // 只驱动动画，锁定期间也跟着输入走
            motor.Move(input);              // 是否被锁由 motor 自己判
        }

        public void RequestJump()
        {
            if (LifeBus.IsDead) return;

            motor.RequestJump();
        }

        public void playerFalling()
        {
            if (LifeBus.IsDead) return;

            motor.CutJump();
        }

        public void playerAttack()
        {
            if (LifeBus.IsDead) return;

            // 朝向直接读总线快照，和 CamHandler / Bomb 的做法一致
            float dir = PlayerBus.Face == FaceDirection.R ? 1f : -1f;

            // 嘴里有东西就吐出去（Release），没有就是扑咬（Eat）—— 两种都吃同一段冲刺
            bool released = items != null && items.TryRelease(dir);
            anim.PlayAttack(released);
            motor.Dash(dir);
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
