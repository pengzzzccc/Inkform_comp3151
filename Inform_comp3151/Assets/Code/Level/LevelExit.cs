using Inkform.Bus;
using Inkform.Tool;
using UnityEngine;

namespace Inkform.Level
{
    /// <summary>
    /// Level exit: the door. When the player enters it, LevelBus.ExitReached is raised with the
    /// door's own destination — a direct RoomDefinition reference plus the id of the checkpoint to
    /// arrive at. The topology lives on the doors themselves (WorldDefinition only holds
    /// menu/entry/registry), so a door is two Inspector references instead of the old five-way
    /// string contract between the text graph, exitIds, Spawn_ object names and Build Settings.
    ///
    /// targetSpawnId "" arrives at the room's isStartPoint checkpoint. Convention carried over from
    /// the Spawn_&lt;source scene&gt; days: a door names the checkpoint beside the door it pairs
    /// with in the destination room, so walking back lands you next to where you came in.
    /// Needs an Is-Trigger collider.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class LevelExit : MonoBehaviour
    {
        [Header("Level exit")]
        [Tooltip("The room this door leads to")]
        [SerializeField] private RoomDefinition destination;

        [Tooltip("Id of the arrival checkpoint (Checkpoint.spawnId) in the destination room. Empty = the room's start point")]
        [SerializeField] private string targetSpawnId = "";

        // The pre-migration exit id: FormerlySerializedAs keeps the old serialized values readable so
        // the one-time migration tool can resolve them into destination/targetSpawnId. It clears the
        // field as it rewires each door; once no scene carries a value, the field can be deleted.
        [SerializeField, UnityEngine.Serialization.FormerlySerializedAs("exitId"), HideInInspector]
        private string legacyExitId = "";

        // Read side for the World validator and tests
        public RoomDefinition Destination => destination;
        public string TargetSpawnId => targetSpawnId;
        public string LegacyExitId => legacyExitId;

        void OnTriggerEnter2D(Collider2D other)
        {
            if (!other.CompareTag(Tags.Player)) return;

            if (destination == null)
            {
                // A door to nowhere is content the World validator flags; ignoring it here keeps a
                // wiring mistake from stranding the player mid-run
                Debug.LogWarning($"{name}: LevelExit has no destination assigned — ignoring", this);
                return;
            }

            LevelBus.RaiseExitReached(destination, targetSpawnId);
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
            // The label says where this door leads and where it drops the player
            UnityEditor.Handles.color = c;
            string label = destination != null
                ? $"→ {destination.DisplayName} ({(string.IsNullOrEmpty(targetSpawnId) ? "start" : targetSpawnId)})"
                : "→ <no destination>";
            UnityEditor.Handles.Label(p + Vector2.up * 0.8f, label);
#endif
        }
    }
}
