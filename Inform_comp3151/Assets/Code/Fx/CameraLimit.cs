using UnityEngine;

namespace Inkform.Fx
{
    /// <summary>
    /// One-directional camera limit wall, finite and transform-driven: the wall segment runs along
    /// this object's own up axis, its centre is the object's position, and `length` is the wall's
    /// reach — rotate the object to slant the wall, move it to place the wall. The screen edge
    /// stops on the wall line and never shows beyond, on exactly one side (blockPositive picks
    /// which, along the object's own right axis); past either end of the segment the camera follows
    /// freely. Multiple walls compose; a scene without any costs nothing.
    /// The line's coordinate is where the SCREEN edge stops — CamHandler offsets it by the view's
    /// half-size along the wall normal internally.
    /// </summary>
    public class CameraLimit : MonoBehaviour
    {
        [Tooltip("Reach of the wall along this object's up axis, centred on the object's position")]
        [SerializeField, Min(0f)] private float length = 20f;

        [Tooltip("On: the camera may not pass the wall toward this object's positive right side. Off: toward the negative side")]
        [SerializeField] private bool blockPositive = true;

        public float Length => length;
        public bool BlockPositive => blockPositive;

        // Always drawn: a wall you cannot see is a wall you cannot place. The segment itself plus
        // chevrons marching toward the blocked side — direction and reach read at a glance
        private void OnDrawGizmos()
        {
            Vector2 centre = transform.position;
            Vector2 wallDir = transform.up;
            Vector2 normal = (Vector2)transform.right * (blockPositive ? 1f : -1f);
            Vector2 half = wallDir * (length * 0.5f);

            Gizmos.color = new Color(1f, 0.45f, 0.2f, 0.9f);
            Gizmos.DrawLine(centre - half, centre + half);

            Gizmos.color = new Color(1f, 0.45f, 0.2f, 0.5f);
            for (int i = 1; i <= 3; i++)
            {
                Vector3 tip = centre + normal * (i * 0.8f);
                Vector3 wingBack = wallDir * 0.4f;
                Gizmos.DrawLine(tip - wingBack, tip);
                Gizmos.DrawLine(tip, tip + wingBack);
            }
        }
    }
}
