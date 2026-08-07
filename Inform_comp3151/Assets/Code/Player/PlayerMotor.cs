using Inkform.Tool;
using UnityEngine;

namespace Inkform.Player
{
    /// <summary>
    /// 玩家的运动学：速度、跳跃、非线性重力、冲刺与击退的移动锁定。
    /// 从 PlayerHandler 拆出来的第二层 —— 只碰刚体，不碰动画也不碰道具。
    ///
    /// 所有 [SerializeField] 的默认值都写成了 Player.prefab 上的实配值而非原先的代码默认值：
    /// 拆组件时 Unity 不会迁移序列化数据，默认值写错的话手感会静默改变（比如移速 10 变回 7）。
    /// 不自带 Update，由 PlayerHandler 按顺序调 Tick()。
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(ContactSensor))]
    public class PlayerMotor : MonoBehaviour
    {
        [Header("Move")]
        [SerializeField] private float movingSpeed = 10f;
        [SerializeField] private float jumpSpeed = 12f;
        [SerializeField][Range(0, 1)] private float fallCutMultiplier = 0.12f;
        [SerializeField] private float jumpBuffer = 0.15f;
        [SerializeField] private int jumpTimes = 1;

        [Header("Gravity")]
        [SerializeField] private float gravity = 3f;
        [SerializeField] private float fallGravityMultiplier = 2.2f;
        [SerializeField][Range(0, 1)] private float onWallGravityMultiplier = 0.2f;

        [Header("Wall jump")]
        [SerializeField] private float wallJumpTime = 0.15f;
        [SerializeField][Range(0, 1)] private float wallKickMultiplier = 0.3f;

        [Header("Attack dash")]
        [SerializeField][Range(1, 2)] private float attackMultiplier = 1f;
        [SerializeField] private float attackTime = 0.22f;      // 冲刺 / 移动锁定时长

        [Header("Knockback")]
        [SerializeField] private float knockbackTime = 0.35f;   // 被炸飞后锁住移动输入的时长

        private Rigidbody2D body;
        private ContactSensor contact;

        private Timer wallJumpBuffer;
        private Timer attackTimer;
        private Timer knockbackTimer;
        private Timer updateBuffer;     // 刚从地面起跳的短暂窗口，期间不补跳跃次数

        private int jumpLeft;
        private float requestTime = -999f;
        private bool jumpCutQueued;

        // 平台跟随状态：站在移动平台上跟着走。玩家侧零「平台认知」——
        // 只认物理事实（脚下刚体这一帧位移了多少），静态平台位移恒零、天然无影响
        private Collider2D groundPlatform;     // 上一帧的脚下碰撞体
        private Vector2 groundPrevPos;         // 平台上一帧位置

        // 起跳发生在本帧 —— 由 PlayerHandler 取走后转交给动画层。
        // 用「取一次即清」的标志而不是事件：同物体内的一次性通知，架个事件不划算
        private bool jumpStarted;

        void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            contact = GetComponent<ContactSensor>();

            body.gravityScale = gravity;
            jumpLeft = jumpTimes;
        }

        /// <summary>本帧是否刚起跳。取走即清 —— 只该被消费一次。</summary>
        public bool ConsumeJumpStarted()
        {
            bool v = jumpStarted;
            jumpStarted = false;
            return v;
        }

        // 移动被锁住（墙跳后摇 / 冲刺 / 击退 / 绳索枪附绳期间）。动画层不受此影响，仍跟着输入走
        private bool MoveLocked =>
            wallJumpBuffer.IsRunning || attackTimer.IsRunning || knockbackTimer.IsRunning || grappleLocked;

        private bool grappleLocked;     // 绳索枪悬挂/拉取期间的输入锁定，绳子主导运动

        /// <summary>绳索枪附绳时锁住移动输入，脱离后解锁。锁的是输入，不碰刚体。</summary>
        public void SetMoveLocked(bool locked) => grappleLocked = locked;

        public float VelocityX => body.linearVelocityX;
        public float VelocityY => body.linearVelocityY;

        /// <summary>每帧推进重力与跳跃。由 PlayerHandler 在 ContactSensor.Tick() 之后调。</summary>
        public void Tick()
        {
            ApplyNonLinearGravity();
            StepJump();
            StepPlatform();
        }

        // 平台跟随：脚下刚体本帧的位移叠加到玩家身上，让玩家站在移动平台上被带着走。
        // 换平台时重置基准（不应用跨平台的跳变位移）；起跳/走出边缘后 Ground 消失自动停止。
        private void StepPlatform()
        {
            if (contact.Ground == null)
            {
                groundPlatform = null;
                return;
            }

            if (contact.Ground != groundPlatform)
            {
                groundPlatform = contact.Ground;
                groundPrevPos = PlatformPos();
                return;
            }

            Vector2 now = PlatformPos();
            Vector2 delta = now - groundPrevPos;
            groundPrevPos = now;
            if (delta.sqrMagnitude < 1e-8f) return;

            // 工程里 m_AutoSyncTransforms = 0：transform 和刚体位置互不同步，两个都要写
            transform.position += (Vector3)delta;
            body.position += delta;
        }

        // 平台位置读刚体（物理真实位置，PatrolMover 已双写同步）；无刚体回落 transform
        private Vector2 PlatformPos()
        {
            Rigidbody2D rb = groundPlatform.attachedRigidbody;
            return rb != null ? rb.position : (Vector2)groundPlatform.transform.position;
        }

        public void Move(Vector2 input)
        {
            // 被炸飞/冲刺期间也不接受移动输入，否则下一帧就把击退速度抹掉了
            if (MoveLocked) return;

            body.linearVelocityX = input.x * movingSpeed;
        }

        public void RequestJump()
        {
            requestTime = Time.time;

            // 贴着墙且跳跃次数已用尽时补一次，让墙跳不吃二段跳的额度
            if (contact.OnWall && jumpLeft == 0 && !wallJumpBuffer.IsRunning)
            {
                jumpLeft++;
                wallJumpBuffer.Set(wallJumpTime);
            }
        }

        /// <summary>松开跳键：上升中就把纵向速度砍掉一截，实现按住越久跳越高。</summary>
        public void CutJump() => jumpCutQueued = true;

        /// <summary>攻击冲刺：朝 dir 方向给一段横向速度并锁住移动输入。</summary>
        public void Dash(float dir)
        {
            body.linearVelocity = new Vector2(dir * movingSpeed * attackMultiplier, body.linearVelocityY);
            attackTimer.Set(attackTime);
        }

        /// <summary>被爆炸推开：直接改速度并锁一小段移动输入。</summary>
        public void Knockback(Vector2 velocity)
        {
            body.linearVelocity = velocity;
            knockbackTimer.Set(knockbackTime);
        }

        /// <summary>死亡：停速度并停物理。
        /// 停物理即停掉一切碰撞回调，尸体不会再被刺反复判定。
        /// 不能 SetActive(false) —— OnDisable 会退订总线，就再也收不到「复活」了。</summary>
        public void StopForDeath()
        {
            body.linearVelocity = Vector2.zero;
            body.simulated = false;
        }

        /// <summary>复活：放回检查点并把所有瞬时状态归零。</summary>
        public void RespawnAt(Vector2 pos)
        {
            // 工程里 m_AutoSyncTransforms = 0：transform 和刚体位置互不同步，两个都要写
            transform.position = pos;
            body.position = pos;
            body.simulated = true;
            body.linearVelocity = Vector2.zero;

            // 死前攒下的锁定和跳跃次数不能带到下一条命里
            jumpLeft = jumpTimes;
            jumpCutQueued = false;
            jumpStarted = false;
            requestTime = -999f;
            wallJumpBuffer.Clear();
            attackTimer.Clear();
            knockbackTimer.Clear();
        }

        // 分支顺序有讲究，依赖 ContactSensor 那条反相语义：离开天花板期间 CeilingStickActive 恒为真，
        // 所以第二个分支（贴顶时间用完）只有真的贴着顶时才可能命中，贴墙下滑和下落加速才轮得到执行。
        // 调整顺序前先回去读 ContactSensor.ceilingStickTimer 上的注释
        private void ApplyNonLinearGravity()
        {
            bool wallSliding = contact.OnWall && !contact.OnGround && body.linearVelocityY < 0f;

            if (contact.OnCeiling && contact.CeilingStickActive)
                body.gravityScale = -5f;                            // 吸在天花板上，重力朝上
            else if (!contact.CeilingStickActive)
                body.gravityScale = gravity;                        // 贴顶时间用完，掉下来
            else if (wallSliding)
                body.gravityScale = gravity * onWallGravityMultiplier; // 贴墙下滑减速
            else if (body.linearVelocityY < 0f)
                body.gravityScale = gravity * fallGravityMultiplier;   // 下落加速，手感更利落
            else
                body.gravityScale = gravity;
        }

        private void StepJump()
        {
            if (jumpCutQueued && body.linearVelocityY > 0f)
            {
                body.linearVelocityY *= fallCutMultiplier;
                jumpCutQueued = false;
            }
            else if (body.linearVelocityY <= 0f)
            {
                jumpCutQueued = false;
            }

            bool canJump = (Time.time - requestTime) < jumpBuffer;
            if (canJump && jumpLeft > 0)
            {
                if (contact.OnLeftWall && !contact.OnGround)
                {
                    body.linearVelocity = new Vector2(jumpSpeed * wallKickMultiplier, jumpSpeed);
                    wallJumpBuffer.Set(wallJumpTime);
                }
                else if (contact.OnRightWall && !contact.OnGround)
                {
                    body.linearVelocity = new Vector2(-jumpSpeed * wallKickMultiplier, jumpSpeed);
                    wallJumpBuffer.Set(wallJumpTime);
                }

                body.linearVelocityY = jumpSpeed;
                requestTime = -999f;
                jumpLeft--;
                jumpStarted = true;     // 地面跳 / 二段跳 / 墙跳都算，动画层据此播 JumpUp

                // 起跳当帧脚还没离开地面，不挡一下的话下面那句会立刻把次数补满
                if (contact.OnGround) updateBuffer.Set(0.1f);
            }

            if (contact.OnGround && !updateBuffer.IsRunning) jumpLeft = jumpTimes;
        }
    }
}
