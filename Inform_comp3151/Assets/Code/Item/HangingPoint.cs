using UnityEngine;

namespace Inkform.Item
{
    /// <summary>
    /// Hanging point: the world-fixed anchor of a chain in the bomb's hanging mode.
    /// Generated in edit mode by Bomb's [ContextMenu], placed as a bomb child object and freely
    /// draggable in the scene; at runtime Bomb takes its world position in Awake as the chain anchor
    /// (fixed, does not move with the bomb). Pure marker component: no collider, no rigidbody, only a
    /// Scene-view icon for positioning.
    /// </summary>
    public class HangingPoint : MonoBehaviour
    {
        [Tooltip("Anchor icon size in the Scene view, purely an editor aid")]
        [SerializeField] private float gizmoSize = 0.25f;

        void OnDrawGizmos()
        {
            Vector3 pos = transform.position;
            Gizmos.color = new Color(0.2f, 0.7f, 1f, 0.9f);

            float s = gizmoSize;
            Gizmos.DrawLine(pos + Vector3.left * s, pos + Vector3.right * s);
            Gizmos.DrawLine(pos + Vector3.up * s, pos + Vector3.down * s);
            Gizmos.DrawWireSphere(pos, s * 0.6f);
        }
    }
}
