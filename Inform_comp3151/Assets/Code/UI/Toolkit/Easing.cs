using UnityEngine;

namespace Inkform.UI
{
    /// <summary>The easing curves Celeste's UI actually uses (Monocle.Ease). Its menus live on
    /// Cube/Sine; BackOut is kept for the press-release overshoot.</summary>
    public static class Easing
    {
        public static float SineIn(float t) => 1f - Mathf.Cos(t * Mathf.PI / 2f);

        public static float SineOut(float t) => Mathf.Sin(t * Mathf.PI / 2f);

        public static float CubeIn(float t) => t * t * t;

        public static float CubeOut(float t) => 1f - CubeIn(1f - t);

        public static float CubeInOut(float t) =>
            t < 0.5f ? 4f * t * t * t : 1f - CubeIn(-2f * t + 2f) / 2f;

        /// <summary>Monocle Ease.BackOut (s = 1.70158): rises past 1 and settles back.</summary>
        public static float BackOut(float t)
        {
            const float s = 1.70158f;
            float u = t - 1f;
            return u * u * ((s + 1f) * u + s) + 1f;
        }
    }
}
