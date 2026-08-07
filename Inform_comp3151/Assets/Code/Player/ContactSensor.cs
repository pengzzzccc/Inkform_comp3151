using Inkform.Bus;
using Inkform.Tool;
using UnityEngine;

namespace Inkform.Player
{
    /// <summary>
    /// Four-way contact detection: whether the player currently touches ground / left wall / right wall
    /// / ceiling. The first layer split from PlayerHandler — motion and animation both read these
    /// flags, and neither should probe again on its own.
    ///
    /// No own Update: probing must strictly precede motion and animation, and Unity does not guarantee
    /// Update order among components on one object. PlayerHandler calls Tick() in the
    /// "sense → move → animate" order. Attach to the Player.
    /// </summary>
    public class ContactSensor : MonoBehaviour
    {
        [Header("Terrain Check")]
        [SerializeField] private Transform groundCheck;
        [SerializeField] private Transform leftWallCheck;
        [SerializeField] private Transform rightWallCheck;
        [SerializeField] private Transform ceilingCheck;

        // All four directions share this one layer mask: Terrain(6) | Breakable(11).
        // The default is hardcoded to the prefab's original config, so a newly added component cannot
        // silently drop the player out of the world
        [SerializeField] private LayerMask terrainMask = (1 << 6) | (1 << 11);
        [SerializeField] private float checkRadius = 0.1f;

        [Header("Ceiling")]
        [SerializeField] private float ceilingStickTime = 0.5f;

        // Allowed ceiling-stick duration. Note its driving is inverted: **refreshed every frame while
        // NOT stuck**, so "IsRunning" is always true while away from the ceiling, and the real
        // countdown starts only after sticking. Gravity relies on this inverted semantics to
        // distinguish "can still stick" from "time to fall"
        private Timer ceilingStickTimer;

        public bool OnGround { get; private set; }
        public bool OnLeftWall { get; private set; }
        public bool OnRightWall { get; private set; }
        public bool OnCeiling { get; private set; }

        public bool OnWall => OnLeftWall || OnRightWall;

        /// <summary>The collider hit underfoot (layers 6/11). Platform following reads the contacted
        /// object's movement through it.</summary>
        public Collider2D Ground { get; private set; }

        /// <summary>Ceiling-stick time not yet used up — see the inverted semantics on ceilingStickTimer above.</summary>
        public bool CeilingStickActive => ceilingStickTimer.IsRunning;

        /// <summary>Probes four-way contact. Called by PlayerHandler at the front of every frame.</summary>
        public void Tick()
        {
            Ground = Physics2D.OverlapCircle(groundCheck.position, checkRadius, terrainMask);
            OnGround = Ground != null;
            OnLeftWall = Physics2D.OverlapCircle(leftWallCheck.position, checkRadius, terrainMask);
            OnRightWall = Physics2D.OverlapCircle(rightWallCheck.position, checkRadius, terrainMask);
            OnCeiling = Physics2D.OverlapCircle(ceilingCheck.position, checkRadius, terrainMask);

            // Touching a wall does not count as ceiling-stuck: both would trigger in a corner, and
            // without this exclusion the state would jitter between wall-sliding and ceiling-stick
            if (OnCeiling && OnLeftWall) OnCeiling = false;
            if (OnCeiling && OnRightWall) OnCeiling = false;

            if (!OnCeiling) ceilingStickTimer.Set(ceilingStickTime);
        }

        void OnDrawGizmosSelected()
        {
            if (groundCheck == null) return;
            Gizmos.color = OnGround ? Color.green : Color.red;
            Gizmos.DrawWireSphere(groundCheck.position, checkRadius);

            if (leftWallCheck == null) return;
            Gizmos.color = OnLeftWall ? Color.green : Color.red;
            Gizmos.DrawWireSphere(leftWallCheck.position, checkRadius);

            if (rightWallCheck == null) return;
            Gizmos.color = OnRightWall ? Color.green : Color.red;
            Gizmos.DrawWireSphere(rightWallCheck.position, checkRadius);

            if (ceilingCheck == null) return;
            Gizmos.color = OnCeiling ? Color.green : Color.red;
            Gizmos.DrawWireSphere(ceilingCheck.position, checkRadius);
        }
    }
}
