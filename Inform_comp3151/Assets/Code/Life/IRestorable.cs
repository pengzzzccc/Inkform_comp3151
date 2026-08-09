namespace Inkform.Life
{
    /// <summary>
    /// Snapshot (memento): a level object's state at one moment. Its content is opaque to the holder —
    /// LevelMemento only knows "there is a snapshot that can restore", not whether it stores a broken
    /// wall or something else. Adding a new restorable object type thus changes LevelMemento not at all.
    /// </summary>
    public interface IMemento
    {
        /// <summary>Restores the originator to the state captured in this snapshot.</summary>
        void Restore();
    }

    /// <summary>
    /// Restorable object (originator): can capture a snapshot of itself.
    /// LevelMemento captures all of them on checkpoint touch and restores them all on death respawn —
    /// otherwise shattered walls would not return and retrying the same section would leave it emptier
    /// and emptier.
    ///
    /// Implementors note: restoration presupposes the object stays alive. **Never Destroy yourself on
    /// destruction** — disable the collider + rendering instead (BreakableWall does exactly this).
    /// </summary>
    public interface IRestorable
    {
        IMemento Capture();
    }
}
