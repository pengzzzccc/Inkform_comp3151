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
        private bool jumpCutquest = false;


        [Header("Player setting")]
        [SerializeField] private float movingSpeed = 7f;
        [SerializeField] private float jumpSpeed = 20.5f;
        [SerializeField][Range(0, 1)] private float fallCutmultiper = 0.1f;
        [SerializeField] private float Gravity = 4f;
        [SerializeField] private float fallGravityMultiper = 2.2f;

        [Header("Ground Check")]
        [SerializeField] private Transform groundCheck;
        [SerializeField] private float CheckRadius = 0.5f;
        [SerializeField] private LayerMask mask;


        // jumpBuffer
        [SerializeField] private float jumpBuffer = 0.3f;
        private float requestTime = -999f;

        void Awake()
        {
            controller = GetComponent<Rigidbody2D>();
            controller.gravityScale = Gravity;

        }

        void Update()
        {
            OnGround = Physics2D.OverlapCircle(groundCheck.position, CheckRadius, mask);

            ActiveNoneLinerGrivay();
            playerJumping();


            if (jumpCutquest && controller.linearVelocityY > 0f)
            {
                controller.linearVelocityY *= fallCutmultiper;
                jumpCutquest = false;
            }
            else if (controller.linearVelocityY <= 0f)
            {
                jumpCutquest = false;
            }

        }

        public void ActiveNoneLinerGrivay()
        {
            if (controller.linearVelocityY < 0f)
            {
                controller.gravityScale = Gravity * fallGravityMultiper;
            }
            else
            {
                controller.gravityScale = Gravity;
            }
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
            if (OnGround && (Time.time - requestTime) < jumpBuffer)
            {
                SetState(PlayerState.Jump);
                controller.linearVelocityY = jumpSpeed;
                requestTime = -999f;
            }
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
        }

    }
}
