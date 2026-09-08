using Inkform.Tool;
using UnityEngine;

namespace Inkform.Player
{
    /// <summary>
    /// The player's kinematics: velocity, jump, non-linear gravity, dash and knockback move lockout.
    /// The second layer split from PlayerHandler — touches only the rigidbody, neither animation nor items.
    ///
    /// All [SerializeField] defaults are written as Player.prefab's actual values rather than the old
    /// code defaults: Unity does not migrate serialized data when splitting components, and a wrong
    /// default silently changes the feel (e.g. speed 10 back to 7).
    /// No own Update; PlayerHandler calls Tick() in order.
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
        [SerializeField][Range(1, 5)] private float attackMultiplier = 1f;
        [SerializeField] private float attackTime = 0.22f;      // dash / move-lockout duration

        [Header("Knockback")]
        [SerializeField] private float knockbackTime = 0.35f;   // move-input lockout after being blasted

        private Rigidbody2D body;
        private ContactSensor contact;

        private Timer wallJumpBuffer;
        private Timer attackTimer;
        private Timer knockbackTimer;
        private Timer updateBuffer;     // brief window after leaving the ground, during which jump count is not refilled

        private int jumpLeft;
        private float requestTime = -999f;
        private bool jumpCutQueued;

        // Platform follow state: standing on a moving platform carries the player along. Zero
        // "platform awareness" on the player side — it only reads physical facts (how far the contacted
        // rigidbody moved this frame); static platforms move zero, naturally unaffected
        private Collider2D groundPlatform;     // the ground collider from the previous frame
        private Vector2 groundPrevPos;         // the platform's previous-frame position

        // The jump happened this frame — handed to the animation layer by PlayerHandler.
        // A take-once-then-clear flag rather than an event: a one-shot notice within the same object,
        // an event would not pay for itself
        private bool jumpStarted;

        void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            contact = GetComponent<ContactSensor>();

            body.gravityScale = gravity;
            jumpLeft = jumpTimes;
        }

        /// <summary>Whether a jump just started this frame. Cleared on take — consumed exactly once.</summary>
        public bool ConsumeJumpStarted()
        {
            bool v = jumpStarted;
            jumpStarted = false;
            return v;
        }

        // Move is locked (wall-jump recovery / dash / knockback / rope-gun attached). The animation
        // layer is unaffected and still follows input
        private bool MoveLocked =>
            wallJumpBuffer.IsRunning || attackTimer.IsRunning || knockbackTimer.IsRunning || grappleLocked;

        private bool grappleLocked;     // input lockout while the rope gun hangs/pulls; the rope dominates motion

        /// <summary>Locks move input while the rope gun is attached; unlocks on release. Locks input,
        /// never touches the rigidbody.</summary>
        public void SetMoveLocked(bool locked) => grappleLocked = locked;

        public float VelocityX => body.linearVelocityX;
        public float VelocityY => body.linearVelocityY;

        /// <summary>Advances gravity and jump each frame. Called by PlayerHandler after ContactSensor.Tick().</summary>
        public void Tick()
        {
            ApplyNonLinearGravity();
            StepJump();
            StepPlatform();
        }

        // Platform follow: adds the contacted rigidbody's movement this frame to the player, carrying
        // the player on a moving platform. Switching platforms resets the baseline (never applies the
        // cross-platform jump delta); jumping / walking off the edge stops the follow automatically
        // when Ground disappears
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

            // The project sets m_AutoSyncTransforms = 0: transform and rigidbody positions do not sync,
            // write both
            transform.position += (Vector3)delta;
            body.position += delta;
        }

        // Platform position reads the rigidbody (true physical position, synced by PatrolMover's
        // double-write); without a rigidbody, falls back to the transform
        private Vector2 PlatformPos()
        {
            Rigidbody2D rb = groundPlatform.attachedRigidbody;
            return rb != null ? rb.position : (Vector2)groundPlatform.transform.position;
        }

        public void Move(Vector2 input)
        {
            // Also rejects move input while blasted/dashing, or the next frame would wipe the knockback velocity
            if (MoveLocked) return;

#if UNITY_EDITOR
            // F2 flight: both axes at full move speed, gravity zeroed in Tick. Rope/dash lockouts
            // still win (the check above)
            if (DebugCheats.Flight)
            {
                body.linearVelocity = input * movingSpeed;
                return;
            }
#endif

            body.linearVelocityX = input.x * movingSpeed;
        }

        public void RequestJump()
        {
            requestTime = Time.time;

            // On a wall with the jump count spent, grant one extra so wall jumps never eat the
            // double-jump budget
            if (contact.OnWall && jumpLeft == 0 && !wallJumpBuffer.IsRunning)
            {
                jumpLeft++;
                wallJumpBuffer.Set(wallJumpTime);
            }
        }

        /// <summary>Jump released: cuts a chunk of the upward velocity, holding longer jumps higher.</summary>
        public void CutJump() => jumpCutQueued = true;

        /// <summary>Attack dash: gives a horizontal velocity in dir and locks move input.</summary>
        public void Dash(float dir)
        {
            body.linearVelocity = new Vector2(dir * movingSpeed * attackMultiplier, body.linearVelocityY);
            attackTimer.Set(attackTime);
        }

        /// <summary>Blasted away: rewrites velocity directly and locks a short move input.</summary>
        public void Knockback(Vector2 velocity)
        {
            body.linearVelocity = velocity;
            knockbackTimer.Set(knockbackTime);
        }

        /// <summary>Death: stops velocity and physics.
        /// Disabling physics stops all collision callbacks, so the corpse is not repeatedly judged by
        /// spikes. Must NOT SetActive(false) — OnDisable would unsubscribe the buses and "respawn"
        /// would never arrive.</summary>
        public void StopForDeath()
        {
            body.linearVelocity = Vector2.zero;
            body.simulated = false;
        }

        /// <summary>Respawn: teleports to the checkpoint and zeros all transient state.</summary>
        public void RespawnAt(Vector2 pos)
        {
            // The project sets m_AutoSyncTransforms = 0: transform and rigidbody positions do not sync,
            // write both
            transform.position = pos;
            body.position = pos;
            body.simulated = true;
            body.linearVelocity = Vector2.zero;

            // Lockouts and jump count accumulated before death must not carry into the next life
            jumpLeft = jumpTimes;
            jumpCutQueued = false;
            jumpStarted = false;
            requestTime = -999f;
            wallJumpBuffer.Clear();
            attackTimer.Clear();
            knockbackTimer.Clear();
            updateBuffer.Clear();
            grappleLocked = false;
            groundPlatform = null;
            groundPrevPos = Vector2.zero;
        }

        // The branch order matters, relying on ContactSensor's inverted semantics: CeilingStickActive
        // is always true while away from the ceiling, so the second branch (stick time exhausted) can
        // only hit while actually stuck — wall sliding and fall acceleration only run otherwise.
        // Read the comment on ContactSensor.ceilingStickTimer before reordering
        private void ApplyNonLinearGravity()
        {
#if UNITY_EDITOR
            // F2 flight: gravity never touches the body — Move drives both axes directly. Toggling
            // off self-heals: the normal path below assigns gravityScale every frame anyway
            if (DebugCheats.Flight)
            {
                body.gravityScale = 0f;
                return;
            }
#endif
            bool wallSliding = contact.OnWall && !contact.OnGround && body.linearVelocityY < 0f;

            if (contact.OnCeiling && contact.CeilingStickActive)    {body.gravityScale = -5f;}                                  // stuck to the ceiling, gravity points up        
            else if (!contact.CeilingStickActive)                   {body.gravityScale = gravity;}                              // stick time exhausted, fall off
            else if (wallSliding)                                   {body.gravityScale = gravity * onWallGravityMultiplier;}    // wall slide slowdown
            else if (body.linearVelocityY < 0f)                     {body.gravityScale = gravity * fallGravityMultiplier;}      // falling acceleration, snappier feel
            else                                                    {body.gravityScale = gravity;}
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
                jumpStarted = true;     // ground/double/wall jumps all count; the animation layer plays JumpUp

                // On the jump frame the feet have not left the ground; without a guard the line below
                // would immediately refill the count
                if (contact.OnGround) updateBuffer.Set(0.1f);
            }

            if ((contact.OnGround || contact.OnCeiling) && !updateBuffer.IsRunning) jumpLeft = jumpTimes;
        }
    }
}
