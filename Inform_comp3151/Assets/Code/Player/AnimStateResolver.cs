using Inkform.Bus;
using Inkform.Tool;
using UnityEngine;

namespace Inkform.Player
{
    /// <summary>
    /// Animation state derivation: every frame, computes "which animation should play now" by priority
    /// from contact and motion state, broadcasting to PlayerBus. The third layer split from
    /// PlayerHandler — it only decides WHAT plays; how to play it (clip selection, flipping) belongs
    /// to AniHandler.
    ///
    /// Note this is not a transition state machine but a **derivative** one: everything recomputed
    /// from scratch each frame, no "from A only to B" edges. So a priority chain fits here rather
    /// than the State pattern — splitting the 13 states into 13 classes would only scatter one chain
    /// across 13 files, making the priority harder to read.
    ///
    /// [SerializeField] defaults likewise come from Player.prefab's actual values (landAnimTime etc.).
    /// No own Update; PlayerHandler calls Tick() last.
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

        /// <summary>Facing. Dedup is PlayerBus's job; raise directly here.</summary>
        public void SetFace(FaceDirection face) => PlayerBus.RaiseFace(face);

        /// <summary>Move input drives animation only, not physics — it must follow the input during
        /// lockout too, or landing would play the wrong Move.</summary>
        public void SetMoveInput(Vector2 input) => moveInput = input;

        /// <summary>One-shot jump animation. Handed over by PlayerHandler after taking the jump signal
        /// from PlayerMotor.</summary>
        public void OnJumpStarted() => jumpUpTimer.Set(jumpUpAnimTime);

        /// <summary>Clears one-shot animations accumulated before death on respawn.
        /// With dash no longer playing an animation, only land/ceiling-stick one-shots remain; the call
        /// site stays in case more are added later.</summary>
        public void ResetForRespawn()
        {
            landAnimTimer.Clear();
            jumpUpTimer.Clear();
            ceilingAttachTimer.Clear();
            moveInput = Vector2.zero;
            prevOnGround = false;
            prevOnCeiling = false;
        }

        /// <summary>Aligns the "previous frame" baselines for land/ceiling to the current contact state.
        /// Must be called once after ContactSensor.Tick() on respawn — otherwise respawning on the
        /// ground counts as "just landed" and plays a Land animation and landing sound for nothing.</summary>
        public void SyncContactBaseline()
        {
            prevOnGround = contact.OnGround;
            prevOnCeiling = contact.OnCeiling;
        }

        // Priority: land/ceiling-stick one-shots > ceiling (moving/static) > wall slide
        //           > airborne (JumpUp one-shot → Rise/Fall) > ground (Move/Idle)
        public void Tick()
        {
            // One-shot animations at the land/ceiling moments (the jump one-shot is triggered by OnJumpStarted)
            if (!prevOnGround && contact.OnGround) landAnimTimer.Set(landAnimTime);
            prevOnGround = contact.OnGround;
            if (!prevOnCeiling && contact.OnCeiling) ceilingAttachTimer.Set(ceilingAttachTime);
            prevOnCeiling = contact.OnCeiling;

            if (contact.OnCeiling)
            {
                if (ceilingAttachTimer.IsRunning)
                    SetState(PlayerState.CeilingStick);              // just stuck: attach one-shot
                else if (Mathf.Abs(moveInput.x) > 0.01f)
                    SetState(PlayerState.CeilingMove);               // ceiling moving
                else
                    SetState(PlayerState.CeilingIdle);               // still = Idle flipped upside down
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
                // JumpUp only plays on the jump moment with horizontal velocity; a pure vertical jump
                // goes straight to Rise
                if (jumpUpTimer.IsRunning && (motor.VelocityX > 0.3f || motor.VelocityX < -0.3f))
                    SetState(PlayerState.JumpUp);
                else
                    SetState(motor.VelocityY > 0.1f
                        ? PlayerState.Rise               // rising
                        : PlayerState.Fall);             // falling
                return;
            }

            if (landAnimTimer.IsRunning) { SetState(PlayerState.Land); return; }  // landing moment

            SetState(Mathf.Abs(moveInput.x) > 0.2f
                ? PlayerState.Move
                : PlayerState.Idle);
        }

        // Dedup (only broadcast on change) is PlayerBus's job; raise directly here
        private void SetState(PlayerState state) => PlayerBus.RaiseState(state);
    }
}
