using Inkform.Life;

namespace Inkform.Interactable
{
    /// <summary>
    /// A part that carries state worth rolling back when the level restores. The node's RestorablePart
    /// collects every one of these on capture and rolls them all back on respawn, so a part keeps its
    /// own state private and still gets restored — nobody has to remember to add it to a list somewhere
    /// else. That forgetting is exactly what left ExplodePart.exploded latched forever: a bomb came back
    /// from a respawn visible and solid, and permanently inert.
    ///
    /// Implementing this does **not** make an object restorable. Whether a node comes back at all is
    /// still decided by a RestorablePart being on it; without one, nothing here is ever captured.
    ///
    /// Deliberately a separate interface rather than two more members on IInteractablePart: that
    /// interface's own docs rule out members most parts would stub out, and only a handful of the parts
    /// in the project carry any state. Opting in keeps the other implementations honest — a part that
    /// does not implement this is asserting it has nothing to roll back.
    ///
    /// Not to be confused with Inkform.Life.IRestorable, which is the **node**-level interface
    /// LevelMemento scans for. This one is **part**-level and is only ever read by RestorablePart.
    /// </summary>
    public interface IRestorablePart
    {
        /// <summary>Snapshot of this part's state. Same opaque-memento contract as IRestorable: the
        /// collector never learns what is inside.</summary>
        IMemento Capture();
    }
}
