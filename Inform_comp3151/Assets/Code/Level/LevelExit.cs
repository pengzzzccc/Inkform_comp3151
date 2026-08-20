using Inkform.Bus;
using Inkform.Tool;
using UnityEngine;

namespace Inkform.Level
{
    /// <summary>
    /// Level exit: the trigger that completes a level. When the player enters it, LevelBus.Completed is
    /// raised with this trigger's exitId; SceneDirector then resolves where that exit leads through the
    /// level graph. The trigger itself knows nothing about the next level — it only names the exit, so
    /// the graph stays in the text file and the scene stays dumb.
    ///
    /// exitId must match an edge in the current scene's topology exactly (the same "two places, one
    /// string" contract as Tags, but per-level); by convention it defaults to the target scene's name.
    /// `destination` is display-only info written by the level graph tools: the scene this door leads
    /// to, so the Scene-view gizmo can say it outright instead of making you cross-reference the graph
    /// window. A level with one exit can leave exitId "".
    /// Needs an Is-Trigger collider.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class LevelExit : MonoBehaviour
    {
        [Header("Level exit")]
        [Tooltip("Matches an edge in this scene's level-graph topology. Empty is fine for single-exit levels")]
        [SerializeField] private string exitId = "";

        [Tooltip("Display only, written by the level graph tools: the scene this door leads to. Shown in the Scene-view gizmo")]
        [SerializeField] private string destination = "";

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

#if UNITY_EDITOR
            // The label says where this door leads. `destination` is the tools' answer; when it is
            // empty (older scenes, hand-placed doors) the exitId is the fallback — with the default
            // id convention they are the same string anyway.
            string target = string.IsNullOrEmpty(destination) ? exitId : destination;
            if (!string.IsNullOrEmpty(target))
            {
                UnityEditor.Handles.color = c;
                UnityEditor.Handles.Label(p + Vector2.up * 0.8f, $"→ {target}");
            }
#endif
        }
    }
}
