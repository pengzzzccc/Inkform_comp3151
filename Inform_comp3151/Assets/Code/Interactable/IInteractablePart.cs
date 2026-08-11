using UnityEngine;

namespace Inkform.Interactable
{
    /// <summary>
    /// Interactable behavior component (part): attach to the same object as the Interactable node or
    /// to a child; collected automatically by the node in Awake via GetComponentsInChildren and
    /// Attach-ed one by one. Each part owns one behavior (touch-death / carriable / grabbable...);
    /// when the node receives a physics callback it dispatches in declaration order — returning true
    /// means "handled" and later parts do not receive this contact.
    ///
    /// Only currently-used members live here (see the IDeathBody precedent): callback dispatch,
    /// per-frame driving and such extension points get added when actually needed — empty methods
    /// now would just be dead code.
    /// </summary>
    public interface IInteractablePart
    {
        /// <summary>Mount callback: called once when collected by the node, replaces GetComponentInParent.
        /// At this point the node's Awake has finished, so component references on the root can be cached.</summary>
        void Attach(Interactable root);

        /// <summary>One player/object contact. true = handled, short-circuits later parts.</summary>
        bool HandleContact(ContactPhase phase, Collider2D other);
    }
}
