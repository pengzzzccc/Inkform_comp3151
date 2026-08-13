using Inkform.Bus;
using Inkform.Tool;
using UnityEngine;

namespace Inkform.Level
{
    /// <summary>
    /// Level exit: the trigger that completes a level. When the player enters it, LevelBus.Completed is
    /// raised with this trigger's exitId; SceneDirector then resolves where that exit leads through the
    /// LevelScene asset's topology. The trigger itself knows nothing about the next level — it only names
    /// the exit, so the graph stays in the assets and the scene stays dumb.
    ///
    /// exitId must spell a LevelConnection.id in the current level's LevelScene asset exactly (the same
    /// "two places, one string" contract as Tags, but per-level). A level with one exit can leave it "".
    /// Needs an Is-Trigger collider.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class LevelExit : MonoBehaviour
    {
        [Header("Level exit")]
        [Tooltip("Matches a LevelConnection.id in this level's LevelScene asset. Empty is fine for single-exit levels")]
        [SerializeField] private string exitId = "";

        void OnTriggerEnter2D(Collider2D other)
        {
            if (!other.CompareTag(Tags.Player)) return;

            LevelBus.RaiseCompleted(exitId);
        }

        // OnDrawGizmos (not ...Selected) like Checkpoint: while placing levels you must be able to scan
        // all exits at a glance and spot a missing/misplaced one.
        void OnDrawGizmos()
        {
            Color c = new Color(1f, 0.6f, 0.1f);   // orange — distinct from checkpoint blue/green

            Collider2D box = GetComponent<Collider2D>();
            if (box != null && box.bounds.size.sqrMagnitude > 0f)
            {
                Gizmos.color = c;
                Gizmos.DrawWireCube(box.bounds.center, box.bounds.size);
            }

            // An arrow pointing up marks "exit this way" and carries the id at a glance
            Vector2 p = transform.position;
            Gizmos.color = c;
            Gizmos.DrawLine(p, p + Vector2.up * 0.6f);
            Gizmos.DrawLine(p + Vector2.up * 0.6f, p + Vector2.up * 0.45f + Vector2.left * 0.15f);
            Gizmos.DrawLine(p + Vector2.up * 0.6f, p + Vector2.up * 0.45f + Vector2.right * 0.15f);
        }
    }
}
