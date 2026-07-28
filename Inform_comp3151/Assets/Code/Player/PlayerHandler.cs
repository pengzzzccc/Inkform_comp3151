using UnityEngine;
using System.Collections.Generic;
using Inkform.Bus;

namespace Inkform.player
{
    /// <summary>
    /// this class is made for handling player, it contain's OnGrand check, player mti-FSM, player state publisher.
    /// 状态变化通过 PlayerBus 广播，订阅方不需要持有本对象的引用。
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class PlayerHandler : MonoBehaviour
    {
        // PlayerState
        private PlayerState playerState;
        private FaceDirection faceDirection;

        // player controller
        private Rigidbody2D controller;
        private bool OnGround = false;
        private bool OnLeftWall = false;
        private bool OnRightWall = false;
        private bool OnCeiling = false;
        private bool jumpCutquest = false;
        private Timer WallJumpBuffer;
        private Timer AttackTimer;

        // animation driving
        private Vector2 moveInput;
        private ItemSuper heldItem;    // 叼在嘴里的物品（null = 没叼东西）
        private bool prevOnGround = false;
        private bool prevOnCeiling = false;
        private Timer LandAnimTimer;   // 落地瞬间的一次性动画
        private Timer JumpUpTimer;     // 起跳瞬间的一次性动画（JumpUp，之后转 Rise）
        private Timer CeilingAttachTimer;  // 刚贴上天花板的附着一次性动画


        [Header("Player setting")]
        [SerializeField] private float movingSpeed = 7f;
        [SerializeField] private float jumpSpeed = 20.5f;
        [SerializeField][Range(0, 1)] private float fallCutmultiper = 0.1f;
        [SerializeField] private float Gravity = 4f;
        [SerializeField] private float fallGravityMultiper = 2.2f;
        [SerializeField][Range(0, 1)] private float OnWallGravityMultiper = 0.2f;
        [SerializeField] private int jumpTimes = 1;
        [SerializeField] private float wallJumpTime = 0.3f;
        [SerializeField][Range(0, 1)] private float wallKickMultiper = 0.3f;
        [SerializeField][Range(1, 2)] private float attackMultiper = 1.3f;
        [SerializeField] private float attackTime = 0.6f;
        [SerializeField] private float attackAnimTime = 0.4f;   // Eat/Release 动画保持时长（与冲刺时长解耦）
        private Timer AttackAnimTimer;
        [SerializeField] private float landAnimTime = 0.25f;
        [SerializeField] private float jumpUpAnimTime = 0.3f;   // JumpUp 起跳一次性动画时长
        [SerializeField] private float ceilingAttachTime = 0.25f;  // 天花板附着一次性动画时长
        [SerializeField] private float CeilingStickTime = 0.5f;
        private Timer CeilingStickTimer;
        private int jumpLeft = 0;

        [Header("Ground Check")]
        [SerializeField] private Transform groundCheck;
        [SerializeField] private LayerMask groundmask;
        [SerializeField] private Transform LeftWallCheck;
        [SerializeField] private Transform RightWallCheck;
        [SerializeField] private LayerMask Wallmask;
        [SerializeField] private Transform CeilingCheck;
        [SerializeField] private LayerMask Ceilingmask;
        [SerializeField] private float CheckRadius = 0.5f;

        // jumpBuffer
        [SerializeField] private float jumpBuffer = 0.2f;
        private float requestTime = -999f;
        private Timer updateBuffer;

        [Header("Item / Knockback")]
        // x 都会按朝向取反；spitOffset.x 必须大于「玩家碰撞体半宽 + 物品半径」，否则出生就重叠、会被物理弹开
        [SerializeField] private Vector2 spitOffset = new Vector2(0.9f, 0.15f);
        // spitSpeed.x 必须大于冲刺速度（movingSpeed * attackMultiper），否则吐出去就被自己追上、免疫期一过原地自爆
        [SerializeField] private Vector2 spitSpeed = new Vector2(24f, 6f);
        [SerializeField] private float knockbackTime = 0.35f;   // 被炸飞后锁住移动输入的时长
        private Timer KnockbackTimer;

        void Awake()
        {
            controller = GetComponent<Rigidbody2D>();
            controller.gravityScale = Gravity;

            jumpLeft = jumpTimes;

            // 初始广播一次，让总线快照从一开始就是正确的
            PlayerBus.RaiseState(playerState);
            PlayerBus.RaiseFace(faceDirection);
        }

        void OnEnable()
        {
            ItemBus.ItemEaten += OnItemEaten;
            HazardBus.Exploded += OnExploded;
        }

        void OnDisable()
        {
            ItemBus.ItemEaten -= OnItemEaten;
            HazardBus.Exploded -= OnExploded;
        }

        void Update()
        {

            ContactCheck();

            ActiveNoneLinerGrivay();
            playerJumping();

            UpdateAnimationState();


        }

        private void ContactCheck()
        {
            OnGround = Physics2D.OverlapCircle(groundCheck.position, CheckRadius, groundmask);
            OnLeftWall = Physics2D.OverlapCircle(LeftWallCheck.position, CheckRadius, Wallmask | Ceilingmask);
            OnRightWall = Physics2D.OverlapCircle(RightWallCheck.position, CheckRadius, Wallmask | Ceilingmask);
            OnCeiling = Physics2D.OverlapCircle(CeilingCheck.position, CheckRadius, Ceilingmask);

            if (OnCeiling && OnLeftWall) OnCeiling = false;
            if (OnCeiling && OnRightWall) OnCeiling = false;
            if (!OnCeiling) CeilingStickTimer.Set(CeilingStickTime);
        }

        private void ActiveNoneLinerGrivay()
        {
            bool onWall = OnLeftWall || OnRightWall;
            bool wallSliding = onWall && !OnGround && controller.linearVelocityY < 0f;

            if (OnCeiling && CeilingStickTimer.IsRunning)
                controller.gravityScale = -5;
            else if(!CeilingStickTimer.IsRunning)
                controller.gravityScale = Gravity;
            else if (wallSliding)
                controller.gravityScale = Gravity * OnWallGravityMultiper;
            else if (controller.linearVelocityY < 0f)
                controller.gravityScale = Gravity * fallGravityMultiper;
            else
                controller.gravityScale = Gravity;
        }

        public void playerMoving(Vector2 input)
        {
            // 被炸飞期间也不接受移动输入，否则下一帧就把击退速度抹掉了
            if (WallJumpBuffer.IsRunning || AttackTimer.IsRunning || KnockbackTimer.IsRunning) return;

            moveInput = input;

            if (input.x > 0.01f)
            {
                SetFace(FaceDirection.R);
            }
            else if (input.x < -0.01f)
            {
                SetFace(FaceDirection.L);
            }

            controller.linearVelocityX = input.x * movingSpeed;
        }

        public void RequestJump()
        {
            requestTime = Time.time;
            if ((OnLeftWall || OnRightWall) && jumpLeft == 0 && !WallJumpBuffer.IsRunning)
            {
                jumpLeft++;
                WallJumpBuffer.Set(wallJumpTime);
            }
        }

        public void playerFalling()
        {
            jumpCutquest = true;
        }

        public void playerAttack()
        {
            float dir = faceDirection == FaceDirection.R ? 1f : -1f;

            if (heldItem != null)
            {
                SetState(PlayerState.Release);   // 吐出叼着的物品
                Vector2 mouth = (Vector2)transform.position + new Vector2(dir * spitOffset.x, spitOffset.y);
                ItemBus.RaiseItemReleased(heldItem, mouth, new Vector2(dir * spitSpeed.x, spitSpeed.y));
                heldItem = null;
            }
            else
            {
                SetState(PlayerState.Eat);       // 吃 / Eat
            }

            controller.linearVelocity = new Vector2(dir * movingSpeed * attackMultiper, controller.linearVelocityY);
            AttackTimer.Set(attackTime);          // 冲刺 / 移动锁定
            // Eat/Release 动画保持：松键不打断，到期后由 UpdateAnimationState 恢复移动动画
            AttackAnimTimer.Set(attackAnimTime);
        }

        // 由 ItemBus 在物品被吃下时回调：只有真实吃到才进入叼着物品状态
        private void OnItemEaten(ItemSuper item)
        {
            heldItem = item;
        }

        // 由 HazardBus 在爆炸时回调：沿「爆心 → 自己」的 8 向之一弹开，并锁一小段移动输入
        private void OnExploded(GameObject victim, Vector2 center, float force)
        {
            if (victim != gameObject) return;

            controller.linearVelocity = Dir8.Snap((Vector2)transform.position - center) * force;
            KnockbackTimer.Set(knockbackTime);
        }

        private void playerJumping()
        {
            if (jumpCutquest && controller.linearVelocityY > 0f)
            {
                controller.linearVelocityY *= fallCutmultiper;
                jumpCutquest = false;
            }
            else if (controller.linearVelocityY <= 0f)
            {
                jumpCutquest = false;
            }

            bool canJump = (Time.time - requestTime) < jumpBuffer;
            if (canJump && jumpLeft > 0)
            {

                if (OnLeftWall && !OnGround)
                {
                    controller.linearVelocity = new Vector2(jumpSpeed * wallKickMultiper, jumpSpeed);
                    WallJumpBuffer.Set(wallJumpTime);
                }
                else if (OnRightWall && !OnGround)
                {
                    controller.linearVelocity = new Vector2(-jumpSpeed * wallKickMultiper, jumpSpeed);
                    WallJumpBuffer.Set(wallJumpTime);
                }
                controller.linearVelocityY = jumpSpeed;
                requestTime = -999f;
                jumpLeft--;
                JumpUpTimer.Set(jumpUpAnimTime);   // 起跳一次性动画（含地面跳/二段跳/墙跳）
                if(OnGround)
                {
                    updateBuffer.Set(0.1f);
                }
            }

            if (OnGround && !updateBuffer.IsRunning) jumpLeft = jumpTimes;
        }

        // 集中式动画状态机：每帧按优先级决定当前动画状态
        // 落地一次性检测 > 吃/吐 > 天花板(动/静) > 墙侧下滑 > 空中(JumpUp一次性→Rise/Fall) > 地面(Move/Idle)
        private void UpdateAnimationState()
        {
            // 落地/贴顶瞬间的一次性动画（起跳一次性在 playerJumping 里触发）
            if (!prevOnGround && OnGround) LandAnimTimer.Set(landAnimTime);
            prevOnGround = OnGround;
            if (!prevOnCeiling && OnCeiling) CeilingAttachTimer.Set(ceilingAttachTime);
            prevOnCeiling = OnCeiling;

            if (AttackAnimTimer.IsRunning) return;   // Eat/Release 动画保持期间不打断

            if (OnCeiling)
            {
                if (CeilingAttachTimer.IsRunning)
                    SetState(PlayerState.CeilingStick);              // 刚贴上：附着一次性
                else if (Mathf.Abs(moveInput.x) > 0.01f)
                    SetState(PlayerState.CeilingMove);               // 天花板移动
                else
                    SetState(PlayerState.CeilingIdle);               // 静止 = 上下翻转的 Idle
                return;
            }

            bool onWall = OnLeftWall || OnRightWall;
            if (onWall && !OnGround && controller.linearVelocityY < 0f)
            {
                SetState(faceDirection == FaceDirection.L
                    ? PlayerState.WallSlideL        // 贴左墙下滑
                    : PlayerState.WallSlideR);      // 贴右墙下滑
                return;
            }

            if (!OnGround)
            {
                if (JumpUpTimer.IsRunning && (controller.linearVelocityX > 0.3 | controller.linearVelocityX < -0.3)) SetState(PlayerState.JumpUp);   // 起跳瞬间（有方向）
                else SetState(controller.linearVelocityY > 0.1f
                    ? PlayerState.Rise               // 上升
                    : PlayerState.Fall);             // 下落
                return;
            }

            if (LandAnimTimer.IsRunning) { SetState(PlayerState.Land); return; }  // 落地瞬间

            SetState(Mathf.Abs(moveInput.x) > 0.01f
                ? PlayerState.Move
                : PlayerState.Idle);
        }

        // 去重（只在变化时广播）由 PlayerBus 负责，这里直接 Raise 即可
        private void SetState(PlayerState state)
        {
            playerState = state;
            PlayerBus.RaiseState(state);
        }

        private void SetFace(FaceDirection face)
        {
            faceDirection = face;
            PlayerBus.RaiseFace(face);
        }

        void OnDrawGizmosSelected()
        {
            if (groundCheck == null) return;
            Gizmos.color = OnGround ? Color.green : Color.red;
            Gizmos.DrawWireSphere(groundCheck.position, CheckRadius);

            if (LeftWallCheck == null) return;
            Gizmos.color = OnLeftWall ? Color.green : Color.red;
            Gizmos.DrawWireSphere(LeftWallCheck.position, CheckRadius);

            if (RightWallCheck == null) return;
            Gizmos.color = OnRightWall ? Color.green : Color.red;
            Gizmos.DrawWireSphere(RightWallCheck.position, CheckRadius);

            if (CeilingCheck == null) return;
            Gizmos.color = OnCeiling ? Color.green : Color.red;
            Gizmos.DrawWireSphere(CeilingCheck.position, CheckRadius);
        }

    }
}
