namespace Inkform.Interactable
{
    /// <summary>
    /// Contact phase: Enter = just entered / Stay = lingering / Exit = just left.
    /// The node dispatches OnTrigger*/OnCollision* callbacks by this phase — parts only handle the
    /// phases they care about (e.g. Checkpoint only looks at Enter; lethal hazards must handle both
    /// Enter and Stay, since Enter is not re-raised while the player sits still inside the trigger).
    /// </summary>
    public enum ContactPhase
    {
        Enter,
        Stay,
        Exit,
    }
}
