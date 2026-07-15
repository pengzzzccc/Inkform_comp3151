using UnityEngine;
using System;

namespace Inkform.player
{
    /// <summary>
    /// this class is made for handling player, it contain's OnGrand check, player mti-FSM, player state publisher.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class PlayerHandler : MonoBehaviour
    {
        // PlayerState
        private PlayerState playerState;
        private FaceDirection faceDirection;

        // player event
        public event Action<FaceDirection> OnDirection;
        public event Action<PlayerState> OnPlayerAction;

        // player controller
        private Rigidbody2D controller;
        private bool OnGround = false;
        private bool OnLeftWall = false;
        private bool OnRightWall = false;
        private bool OnCeiling = false;
        private bool jumpCutquest = false;
        private Timer coyTimer;


        [Header("Player setting")]
        [SerializeField] private float movingSpeed = 7f;
        [SerializeField] private float jumpSpeed = 20.5f;
        [SerializeField][Range(0, 1)] private float fallCutmultiper = 0.1f;
        [SerializeField] private float Gravity = 4f;
        [SerializeField] private float fallGravityMultiper = 2.2f;
        [SerializeField][Range(0, 1)] private float OnWallGravityMultiper = 0.1f;
        [SerializeField] private float coyoteTime = 0.1f;
        [SerializeField] private int jumpTimes = 2;
        private int jumpLeft = 0;
        [Header("Ground Check")]
        [SerializeField] private Transform groundCheck;
        [SerializeField] private Transform LeftWallCheck;
        [SerializeField] private Transform RightWallCheck;
        [SerializeField] private Transform CeilingCheck;
        [SerializeField] private float CheckRadius = 0.5f;
        [SerializeField] private LayerMask mask;


        // jumpBuffer
        [SerializeField] private float jumpBuffer = 0.3f;
        private float requestTime = -999f;

        private Timer updateBuffer;

        void Awake()
        {
            controller = GetComponent<Rigidbody2D>();
            controller.gravityScale = Gravity;

            jumpLeft = jumpTimes;

        }

        void Update()
        {
            ContactCheck();

            ActiveNoneLinerGrivay();
            playerJumping();


        }

        private void ContactCheck()
        {
            OnGround = Physics2D.OverlapCircle(groundCheck.position, CheckRadius, mask);
            OnLeftWall = Physics2D.OverlapCircle(LeftWallCheck.position, CheckRadius, mask);
            OnRightWall = Physics2D.OverlapCircle(RightWallCheck.position, CheckRadius, mask);
            OnCeiling = Physics2D.OverlapCircle(CeilingCheck.position, CheckRadius, mask);
        }

        private void ActiveNoneLinerGrivay()
        {
            bool onWall = OnLeftWall || OnRightWall;
            bool wallSliding = onWall && !OnGround && controller.linearVelocityY < 0f;

            if (wallSliding)
                controller.gravityScale = Gravity * OnWallGravityMultiper;
            else if (controller.linearVelocityY < 0f)
                controller.gravityScale = Gravity * fallGravityMultiper;
            else
                controller.gravityScale = Gravity;
        }

        public void playerMoving(Vector2 input)
        {
            if (input.x > 0.01f)
            {
                SetFace(FaceDirection.R);
                SetState(PlayerState.moving);
            }
            else if (input.x < -0.01f)
            {
                SetFace(FaceDirection.L);
                SetState(PlayerState.moving);
            }
            else if (input.x == 0)
            {
                SetFace(faceDirection);
                SetState(PlayerState.Idle);
            }

            controller.linearVelocityX = input.x * movingSpeed;
        }

        public void RequestJump()
        {
            requestTime = Time.time;

        }

        public void playerJumping()
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
            Debug.Log(jumpLeft);
            if (canJump && jumpLeft > 0)
            {

                SetState(PlayerState.Jump);
                controller.linearVelocityY = jumpSpeed;
                requestTime = -999f;
                coyTimer.Clear();
                jumpLeft--;
                if(OnGround)
                {
                    updateBuffer.Set(0.1f);
                }
            }

            if (OnGround && !updateBuffer.IsRunning) jumpLeft = jumpTimes;
        }

        public void playerFalling()
        {
            jumpCutquest = true;
        }

        private void SetState(PlayerState state)
        {
            playerState = state;
            OnPlayerAction?.Invoke(state);
        }

        private void SetFace(FaceDirection face)
        {
            faceDirection = face;
            OnDirection?.Invoke(face);
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
