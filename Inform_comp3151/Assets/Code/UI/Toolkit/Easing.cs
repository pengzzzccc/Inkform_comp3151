using UnityEngine;

namespace Inkform.UI
{
    /// <summary>The easing curves the menus use, after Celeste's (Monocle.Ease): Cube for the
    /// slides, BackOut for the press-release overshoot.</summary>
    public static class Easing
    {
        public static float CubeIn(float t) => t * t * t;

        public static float CubeOut(float t) => 1f - CubeIn(1f - t);

        /// <summary>Monocle Ease.BackOut (s = 1.70158): rises past 1 and settles back.</summary>
        public static float BackOut(float t)
        {
            const float s = 1.70158f;
            float u = t - 1f;
            return u * u * ((s + 1f) * u + s) + 1f;
        }
    }
}
