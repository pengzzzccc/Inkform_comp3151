using Inkform.Life;
using System.Collections.Generic;
using UnityEngine;

namespace Inkform.Interactable.Parts
{
    /// <summary>
    /// Restorable behavior: a set-piece item that comes back every time the player dies and respawns.
    /// Implemented as an IRestorable originator: LevelMemento scans for IRestorable at startup,
    /// captures it on checkpoint touch, and restores it on death respawn, so a restorable item needs
    /// zero changes to LevelMemento.
    ///
    /// This is why a restorable item must never destroy itself: a destroyed object cannot be brought
    /// back. ExplodePart calls HideForRestore() instead of Destroy when this part is present — the
    /// collider, renderers and physics go quiet, and respawn brings the item back where it stood.
    ///
    /// Also the node's snapshot collector: the capture covers this part's own state **and** every
    /// IRestorablePart sharing the node, so a part keeps its state private and is still rolled back.
    /// Before that, this class snapshotted only its own two fields, and ExplodePart.exploded — living
    /// one component over — survived every respawn: the bomb came back visible and solid, and
    /// permanently inert.
    /// </summary>
    public class RestorablePart : MonoBehaviour, IInteractablePart, IRestorable
    {
        private Interactable root;
        private Collider2D box;
        private Rigidbody2D body;
        private readonly List<Renderer> renderers = new List<Renderer>();
        private bool gone;      // hidden (destroyed in spirit), waiting for respawn to bring it back

        // Reused across captures instead of allocating a list per checkpoint touch (LevelMemento
        // captures every originator in the scene on each one). Only ever live inside Capture().
        private readonly List<IRestorablePart> statefulParts = new List<IRestorablePart>();

        public void Attach(Interactable root)
        {
            this.root = root;

            box = root.GetComponent<Collider2D>();
            if (box == null)
                Debug.LogWarning($"{root.name}'s Interactable has no Collider2D; it cannot be hidden for restore", root);

            body = root.GetComponent<Rigidbody2D>();
            renderers.Clear();
            renderers.AddRange(root.GetComponentsInChildren<Renderer>(true));
        }

        // Pure state holder: consumes no contact, later parts receive it normally
        public bool HandleContact(ContactPhase phase, Collider2D other) => false;

        /// <summary>Called by ExplodePart instead of Destroy: marks the item gone and hides it
        /// (collider + renderers + physics off). Never deactivates — this component must stay alive to
        /// be captured and restored on respawn.</summary>
        public void HideForRestore() => SetGone(true);

        // ---- IRestorable ----

        /// <summary>
        /// Snapshots the whole node: this part's own gone flag, the transform, and one memento per
        /// IRestorablePart on the node. Collecting the parts here rather than listing fields by hand is
        /// the point — a part added later is covered without anyone having to remember this method.
        ///
        /// localScale is captured alongside position and rotation. It was missing, which meant a
        /// restore could not undo the scale drift a swallow/spit cycle used to bake in (see
        /// CarriablePart's SetParent notes) — the item came back at whatever size it had grown to.
        /// </summary>
        public IMemento Capture()
        {
            statefulParts.Clear();

            // root is null only when this component sits on an object with no Interactable — a
            // misconfiguration that already left Attach unrun and every field below unset. Degrade to
            // "transform only" rather than adding a new crash to an already-broken setup.
            if (root != null) root.GetParts(statefulParts);

            var partStates = new List<IMemento>(statefulParts.Count);
            foreach (IRestorablePart p in statefulParts) partStates.Add(p.Capture());

            return new RestorableMemento(this, gone, transform.position, transform.rotation, transform.localScale, partStates);
        }

        // The single gone/whole switch, shared by hiding and restoring — maintaining the enabled
        // toggles separately on both paths would eventually miss one (same shape as
        // BreakablePart.SetBroken)
        private void SetGone(bool value)
        {
            gone = value;
            if (box != null) box.enabled = !value;
            foreach (Renderer r in renderers) r.enabled = !value;
            if (body != null) body.simulated = !value;
        }

        /// <summary>Node snapshot. Opaque to LevelMemento; only this class knows it holds "gone or
        /// not", where the item stood, and the state of the node's other parts.</summary>
        private class RestorableMemento : IMemento
        {
            private readonly RestorablePart part;
            private readonly bool gone;
            private readonly Vector3 position;
            private readonly Quaternion rotation;
            private readonly Vector3 scale;
            private readonly List<IMemento> partStates;

            public RestorableMemento(RestorablePart part, bool gone, Vector3 position, Quaternion rotation,
                Vector3 scale, List<IMemento> partStates)
            {
                this.part = part;
                this.gone = gone;
                this.position = position;
                this.rotation = rotation;
                this.scale = scale;
                this.partStates = partStates;
            }

            public void Restore()
            {
                if (part == null) return;   // item gone (scene change etc.), skip silently

                // A swallowed item may still be parented to the player at death; detach so the
                // position restore is authoritative. worldPositionStays: false to match how
                // CarriablePart parents it — the default true makes Unity rewrite localScale to
                // preserve world scale, which is precisely the drift the scale restore below undoes.
                if (!gone && part.transform.parent != null) part.transform.SetParent(null, false);

                // Runs even for a gone item, where this used to early-return. Moving a hidden item is
                // harmless (nothing renders or simulates it), and dropping the early return is what
                // guarantees the part snapshots below are applied on every path — "the snapshot is the
                // snapshot", with no branch deciding which half of it counts.
                //
                // The project sets m_AutoSyncTransforms = 0: transform and rigidbody positions do not
                // sync, write both
                part.transform.position = position;
                part.transform.rotation = rotation;
                part.transform.localScale = scale;
                if (part.body != null)
                {
                    part.body.position = position;
                    part.body.linearVelocity = Vector2.zero;
                    part.body.angularVelocity = 0f;
                }

                foreach (IMemento m in partStates) m.Restore();

                // Last on purpose: SetGone is the final authority on collider / renderers / simulated,
                // so it must not be overwritten by a part rolling back its own state above.
                part.SetGone(gone);
            }
        }
    }
}
