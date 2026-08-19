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
    /// </summary>
    public class RestorablePart : MonoBehaviour, IInteractablePart, IRestorable
    {
        private Collider2D box;
        private Rigidbody2D body;
        private readonly List<Renderer> renderers = new List<Renderer>();
        private bool gone;      // hidden (destroyed in spirit), waiting for respawn to bring it back

        public void Attach(Interactable root)
        {
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

        public IMemento Capture() => new RestorableMemento(gameObject, gone, transform.position, transform.rotation);

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

        /// <summary>Item snapshot. Opaque to LevelMemento; only this class knows it stores "gone or
        /// not" and where the item stood.</summary>
        private class RestorableMemento : IMemento
        {
            private readonly GameObject part;
            private readonly RestorablePart restorablePart;
            private readonly bool gone;
            private readonly Vector3 position;
            private readonly Quaternion rotation;

            public RestorableMemento(GameObject part, bool gone, Vector3 position, Quaternion rotation)
            {
                this.part = part;
                this.restorablePart = part.GetComponent<RestorablePart>();
                this.gone = gone;
                this.position = position;
                this.rotation = rotation;
            }

            public void Restore()
            {
                if (restorablePart == null) return;   // item gone (scene change etc.), skip silently

                // A swallowed item may still be parented to the player at death; detach so the
                // position restore is authoritative
                if (!gone && restorablePart.transform.parent != null) restorablePart.transform.SetParent(null);

                restorablePart.SetGone(gone);
                if (gone) return;

                // The project sets m_AutoSyncTransforms = 0: transform and rigidbody positions do not
                // sync, write both
                restorablePart.transform.position = position;
                restorablePart.transform.rotation = rotation;
                if (restorablePart.body != null)
                {
                    restorablePart.body.position = position;
                    restorablePart.body.linearVelocity = Vector2.zero;
                    restorablePart.body.angularVelocity = 0f;
                }
            }
        }
    }
}
