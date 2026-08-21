using UnityEngine;

namespace Inkform.Interactable.Parts
{
    /// <summary>
    /// Hanging anchor marker: attach to a child of an object carrying HangingChain to mark a chain
    /// anchor position. Pure marker component — no logic; HangingChain auto-collects via
    /// GetComponentsInChildren at Attach, so an anchor **must be a child node of that object**.
    /// No collider, no rigidbody — only a Scene-view icon for positioning.
    ///
    /// Also used by the former Bomb's hanging mode, which collected anchors the same way — one marker
    /// type serves both the framework and the legacy bomb.
    /// </summary>
    public class HangAnchor : MonoBehaviour
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
