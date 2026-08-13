using Inkform.Settings;
using Inkform.Tool;
using UnityEngine;

namespace Inkform.Fx
{
    /// <summary>
    /// Grid shatter: slices a bounds into cellsX × cellsY pieces, each flung along the 8-way
    /// "blast center → cell center" direction. Breakable walls and bombs share this one shatter
    /// presentation; holds no state itself — how many slices, how fast, what it looks like are all
    /// written in the FragmentCue passed in by the caller.
    /// </summary>
    public static class Shatter
    {
        /// <summary>
        /// Spawns shards along the grid. bounds must be captured by the caller before disabling the
        /// collider — once a Collider2D is disabled its physics shape is removed and bounds degenerate
        /// to zero size at the origin.
        /// </summary>
        public static void Burst(FragmentCue cue, Bounds bounds, Vector2 center, float force)
        {
            if (cue == null || cue.prefab == null) return;      // slot unconfigured, skip silently
            if (cue.cellsX < 1 || cue.cellsY < 1) return;

            Vector2 cell = new Vector2(bounds.size.x / cue.cellsX, bounds.size.y / cue.cellsY);

            for (int ix = 0; ix < cue.cellsX; ix++)
            {
                for (int iy = 0; iy < cue.cellsY; iy++)
                {
                    // cell center = bounds bottom-left + (cell index + 0.5) × cell size
                    Vector2 pos = new Vector2(
                        bounds.min.x + (ix + 0.5f) * cell.x,
                        bounds.min.y + (iy + 0.5f) * cell.y);

                    SpawnFragment(cue, pos, cell, center, force);
                }
            }
        }

        private static void SpawnFragment(FragmentCue cue, Vector2 pos, Vector2 cell,
                                          Vector2 center, float force)
        {
            GameObject frag = Object.Instantiate(cue.prefab, pos, Quaternion.identity);

            // Look and scale are left to Fragment itself per the Cue: only it knows the final randomly
            // picked sprite and its original size
            if (frag.TryGetComponent(out Fragment fragment)) fragment.Apply(cue, cell);
            else frag.transform.localScale = cell;      // prefab without Fragment: fall back to "cell size = scale"

            if (!frag.TryGetComponent(out Rigidbody2D body)) return;

            // The project sets m_AutoSyncTransforms = 0, so sync the rigidbody after touching the
            // transform — same reason as Bomb.OnItemReleased
            body.position = pos;
            // FX intensity scales the launch speed (0 = shards drop in place); spin stays natural
            body.linearVelocity = Dir8.Snap(pos - center) * (force * cue.forceMultiplier * SettingsStore.FxIntensity);
            body.angularVelocity = Random.Range(-cue.spinSpeed, cue.spinSpeed);
        }

        /// <summary>Draws the slicing grid in the Scene view for tuning block counts. The caller sets Gizmos.color first.</summary>
        public static void DrawGrid(Bounds bounds, int cellsX, int cellsY)
        {
            if (cellsX < 1 || cellsY < 1) return;

            for (int ix = 1; ix < cellsX; ix++)
            {
                float x = bounds.min.x + bounds.size.x * ix / cellsX;
                Gizmos.DrawLine(new Vector3(x, bounds.min.y), new Vector3(x, bounds.max.y));
            }
            for (int iy = 1; iy < cellsY; iy++)
            {
                float y = bounds.min.y + bounds.size.y * iy / cellsY;
                Gizmos.DrawLine(new Vector3(bounds.min.x, y), new Vector3(bounds.max.x, y));
            }
        }
    }
}
