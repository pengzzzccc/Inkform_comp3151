using Inkform.Bus;
using Inkform.Fx;
using Inkform.Life;
using UnityEngine;

namespace Inkform.Level
{
    /// <summary>
    /// Breakable wall: when caught in a blast, shatters into grid pieces, each flung along the 8-way
    /// "blast center → cell center" direction. One shot destroys it; no durability. Shard size/position
    /// derive from the collider bounds, so any wall scale adapts automatically.
    ///
    /// After shattering it does **not destroy itself**, only disables collider and rendering — when the
    /// player dies at this section, LevelMemento must restore the wall, and a destroyed object cannot
    /// be brought back. Same approach as PlayerHandler using simulated = false on death and
    /// Bomb.SetVisible(false) when swallowed: all deliberately avoid true destruction/deactivation.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    [RequireComponent(typeof(SpriteRenderer))]
    public class BreakableWall : MonoBehaviour, IRestorable
    {
        [Header("Break setting")]
        [SerializeField] private FragmentCue breakCue;          // what it shatters into is all written in this asset

        private Collider2D box;
        private SpriteRenderer sprite;
        private bool broken = false;    // two bombs blasting in the same frame must not shatter it twice

        void Awake()
        {
            box = GetComponent<Collider2D>();
            sprite = GetComponent<SpriteRenderer>();
        }

        void OnEnable()
        {
            HazardBus.Exploded += OnExploded;
        }

        void OnDisable()
        {
            HazardBus.Exploded -= OnExploded;
        }

        // Called by HazardBus on explosion: shatter into pieces, then hide the body (no destroy, kept
        // for restoration)
        private void OnExploded(GameObject victim, Vector2 center, float force)
        {
            if (victim != gameObject) return;
            if (broken) return;

            // Bounds must be captured before disabling the collider: once a Collider2D is disabled its
            // physics shape is removed and bounds degenerate to zero size at the origin
            Bounds bounds = box.bounds;

            SetBroken(true);

            // bounds was captured before the collider was disabled; this is that snapshot
            Shatter.Burst(breakCue, bounds, center, force);

            // Shatter signal: sound and such are driven by it. Uses bounds.center rather than
            // transform.position — bounds is the pre-disable snapshot, the wall's true geometric center
            HazardBus.RaiseBroken(bounds.center);
        }

        // The single broken/whole switch, shared by breaking and restoring — maintaining the enabled
        // toggles separately on both paths would eventually miss one
        private void SetBroken(bool value)
        {
            broken = value;
            box.enabled = !value;
            sprite.enabled = !value;
        }

        // ---- IRestorable ----

        public IMemento Capture() => new WallMemento(this, broken);

        /// <summary>Wall snapshot. Opaque to LevelMemento; only this class knows it stores "broken or not".</summary>
        private class WallMemento : IMemento
        {
            private readonly BreakableWall wall;
            private readonly bool broken;

            public WallMemento(BreakableWall wall, bool broken)
            {
                this.wall = wall;
                this.broken = broken;
            }

            public void Restore()
            {
                if (wall == null) return;   // wall gone (scene change etc.), skip silently
                wall.SetBroken(broken);
            }
        }

        // Draws the slicing grid in the Scene view for tuning cellsX / cellsY in the Cue
        void OnDrawGizmosSelected()
        {
            Collider2D c = GetComponent<Collider2D>();
            if (c == null || breakCue == null) return;

            Gizmos.color = Color.yellow;
            Shatter.DrawGrid(c.bounds, breakCue.cellsX, breakCue.cellsY);
        }
    }
}
