using UnityEngine;

namespace Inkform.Tool
{
    /// <summary>
    /// Snaps an arbitrary direction to one of 8 directions (4 cardinal + 4 diagonal).
    /// Reconstructs the unit vector from cos/sin so all 8 directions have the same length —
    /// multiplying by the same force yields equally strong knockback.
    /// </summary>
    public static class Dir8
    {
        public static Vector2 Snap(Vector2 v)
        {
            if (v.sqrMagnitude < 0.0001f) return Vector2.up;   // fallback when the two points coincide: push up

            float deg = Mathf.Round(Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg / 45f) * 45f;
            float rad = deg * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
        }
    }
}
