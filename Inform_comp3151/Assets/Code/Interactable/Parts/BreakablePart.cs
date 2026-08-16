using Inkform.Bus;
using Inkform.Fx;
using UnityEngine;

namespace Inkform.Interactable.Parts
{
    /// <summary>
    /// Breakable behavior: when caught in a blast, shatters into grid pieces, then hides (collider +
    /// all renderers off). Deliberately **not** IRestorable: this object does not come back on respawn,
    /// LevelMemento never collects it. Set pieces that must come back go through ExplodePart +
    /// RestorablePart instead — that is the only restorable path in the framework.
    ///
    /// Purely a listener: HandleContact never consumes contacts; blast resolution arrives via
    /// HazardBus.Exploded self-claiming (the blast's victim is the hit collider's GameObject, which
    /// must be the node itself). Layering: the object must sit on a layer covered by the exploder's
    /// blastMask; if it also needs to be standable, use Terrain(6)/Breakable(11) so the player's
    /// four-way contact probe recognizes it.
    /// </summary>
    public class BreakablePart : MonoBehaviour, IInteractablePart
    {
        [Header("Break setting")]
        [Tooltip("What it shatters into is all written in this asset")]
        [SerializeField] private FragmentCue breakCue;

        private Interactable root;
        private Collider2D box;         // collider on the node: disabled when broken
        private Renderer[] renderers;   // every renderer under the node (children included): a framework object may draw with several
        private bool broken;            // two blasts in the same frame must not shatter it twice

        public void Attach(Interactable root)
        {
            this.root = root;

            // RequireComponent guarantees a collider on the node; not getting one means misconfiguration — complain
            box = root.GetComponent<Collider2D>();
            if (box == null)
                Debug.LogWarning($"{root.name}'s Interactable has no Collider2D; the breakable will not work", root);

            renderers = root.GetComponentsInChildren<Renderer>(true);
        }

        // Pure listener: never consumes, later parts (SolidSurface etc.) still receive the contact
        public bool HandleContact(ContactPhase phase, Collider2D other) => false;

        void OnEnable()
        {
            HazardBus.Exploded += OnExploded;
        }

        void OnDisable()
        {
            HazardBus.Exploded -= OnExploded;
        }

        // Called by HazardBus on explosion: shatter into pieces, then hide the body. No restore
        // support (deliberate): once broken the object stays hidden for the rest of the run.
        private void OnExploded(GameObject victim, Vector2 center, float force)
        {
            if (root == null || box == null) return;
            if (victim != root.gameObject) return;      // self-claiming: blast's victim is the hit collider's GameObject
            if (broken) return;

            // Bounds must be captured before disabling the collider: once a Collider2D is disabled its
            // physics shape is removed and bounds degenerate to zero size at the origin
            Bounds bounds = box.bounds;

            SetBroken(true);

            Shatter.Burst(breakCue, bounds, center, force);

            // Shatter signal: sound and such are driven by it. Uses bounds.center (the pre-disable
            // snapshot) rather than transform.position — bounds is the true geometric center
            HazardBus.RaiseBroken(bounds.center);
        }

        // The single broken/whole switch: every object state toggle lives here, never scattered
        // across call sites (same shape as RestorablePart.SetGone)
        private void SetBroken(bool value)
        {
            broken = value;
            if (box != null) box.enabled = !value;
            foreach (Renderer r in renderers) r.enabled = !value;
        }

        // Draws the slicing grid in the Scene view for tuning cellsX / cellsY in the Cue
        void OnDrawGizmosSelected()
        {
            if (box == null || breakCue == null) return;

            Gizmos.color = Color.yellow;
            Shatter.DrawGrid(box.bounds, breakCue.cellsX, breakCue.cellsY);
        }
    }
}
