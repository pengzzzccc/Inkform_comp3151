using UnityEngine;
using System;
using System.Collections.Generic;

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
        public event Action<PlayerState, FaceDirection> OnPlayerAction;

        // player controller
        private Rigidbody2D controller;
        private bool OnGround = false;
        private bool OnLeftWall = false;
        private bool OnRightWall = false;
        private bool OnCeiling = false;
        private bool jumpCutquest = false;
        private Timer WallJumpBuffer;
        private Timer AttackTimer;


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
        private int jumpLeft = 0;

        [Header("Ground Check")]
        [SerializeField] private Transform groundCheck;
        [SerializeField] private Transform LeftWallCheck;
        [SerializeField] private Transform RightWallCheck;
        [SerializeField] private Transform CeilingCheck;
        [SerializeField] private float CheckRadius = 0.5f;
        [SerializeField] private LayerMask mask;


        // jumpBuffer
        [SerializeField] private float jumpBuffer = 0.2f;
        private float requestTime = -999f;
        private Timer updateBuffer;
        private PlayerState lastState;
        private FaceDirection lastFace;

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
            if (WallJumpBuffer.IsRunning || AttackTimer.IsRunning) return;

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
                // SetFace(faceDirection);
                SetState(PlayerState.Idle);
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
            SetState(PlayerState.Attack);

            float dir = faceDirection == FaceDirection.R ? 1f : -1f;
            controller.linearVelocity = new Vector2(dir * movingSpeed * attackMultiper, controller.linearVelocityY);
            AttackTimer.Set(attackTime);
        }

        public void playerAttackCancel()
        {
            SetState(PlayerState.Idle);
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
            Debug.Log(jumpLeft);
            if (canJump && jumpLeft > 0)
            {

                SetState(PlayerState.Jump);
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
                if(OnGround)
                {
                    updateBuffer.Set(0.1f);
                }
            }

            if (OnGround && !updateBuffer.IsRunning) jumpLeft = jumpTimes;
        }

        private void SetState(PlayerState state)
        {
            playerState = state;
            if (state != lastState)
            {
                lastState = state;
                OnPlayerAction?.Invoke(state, faceDirection);

                Debug.Log("PlayerState: " + playerState);
            }

            
        }

        private void SetFace(FaceDirection face)
        {
            faceDirection = face;
            if (face != lastFace)
            {
                lastFace = face;
                OnPlayerAction?.Invoke(playerState, face);
                Debug.Log("FaceDirection: " + faceDirection);
            }

            
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
