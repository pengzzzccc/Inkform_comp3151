using Inkform.Bus;
using Inkform.Tool;
using UnityEngine;

namespace Inkform.Level
{
    /// <summary>
    /// Checkpoint: touching it records the respawn position here. The one with isStartPoint checked also
    /// serves as the level spawn point — at startup RespawnDirector teleports the player there, so
    /// "spawn point" and "checkpoint" are the same kind of object.
    /// Needs an Is-Trigger collider.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class Checkpoint : MonoBehaviour
    {
        [Header("Checkpoint setting")]
        [SerializeField] private bool isStartPoint = false;                     // checked = also the level spawn point; only one per level
        [SerializeField] private Vector2 spawnOffset = new Vector2(0f, 0.5f);   // raised a bit so respawn feet do not sink into the ground and get pushed out

        private bool active;    // passing through the same point repeatedly must not re-raise feedback

        public bool IsStartPoint => isStartPoint;
        public Vector2 SpawnPos => (Vector2)transform.position + spawnOffset;

        void OnTriggerEnter2D(Collider2D other)
        {
            if (active) return;
            if (!other.CompareTag(Tags.Player)) return;

            active = true;
            LifeBus.RaiseCheckpointSet(SpawnPos);
        }

        // OnDrawGizmos rather than ...Selected: while placing levels you must be able to scan all
        // respawn points at a glance and spot a missing spawn point — clicking each one is too slow
        void OnDrawGizmos()
        {
            bool touched = Application.isPlaying && active;
            Color c = isStartPoint ? new Color(0.35f, 1f, 0.45f) : new Color(0.35f, 0.85f, 1f);

            // Trigger range: the player must enter this box to count as touching. Read the collider
            // directly, so the gizmo and the actual check never diverge — tuning one and forgetting the other
            Collider2D box = GetComponent<Collider2D>();
            if (box != null && box.bounds.size.sqrMagnitude > 0f)
            {
                Gizmos.color = touched ? c : c * 0.55f;
                Gizmos.DrawWireCube(box.bounds.center, box.bounds.size);
            }

            // The true respawn coordinate. A line to it shows at a glance how high spawnOffset is —
            // if this point is buried in the ground, the player gets physically pushed out at respawn
            Gizmos.color = c;
            Gizmos.DrawLine(transform.position, SpawnPos);

            if (touched) Gizmos.DrawSphere(SpawnPos, 0.14f);        // solid = stepped on this run
            else Gizmos.DrawWireSphere(SpawnPos, 0.14f);

            // The spawn point gets an extra cross to distinguish it from plain checkpoints (only one per level)
            if (!isStartPoint) return;
            Gizmos.DrawLine(SpawnPos + Vector2.left * 0.3f, SpawnPos + Vector2.right * 0.3f);
            Gizmos.DrawLine(SpawnPos + Vector2.down * 0.3f, SpawnPos + Vector2.up * 0.3f);
        }
    }
}
