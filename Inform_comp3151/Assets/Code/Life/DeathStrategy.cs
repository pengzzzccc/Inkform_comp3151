using UnityEngine;

namespace Inkform.Life
{
    /// <summary>
    /// Death strategy (abstract): the complete presentation algorithm of one death method — how the
    /// body is handled, what the screen does, which sound plays, how long until respawn.
    ///
    /// Made a ScriptableObject rather than a plain C# class, following the SoundCue / FragmentCue
    /// approach: parameters are configured in the Inspector, one asset can be referenced in many
    /// places, and adding a death method is just creating a new asset — no code references to change.
    ///
    /// Why a strategy and not a "parameter table": death methods differ in more than numbers.
    /// Shattering slices by a grid, fading tweaks alpha per frame, sinking moves positions — three
    /// different code paths that only polymorphism can select; otherwise you write a switch.
    /// </summary>
    public abstract class DeathStrategy : ScriptableObject
    {
        [Header("Dispatch")]
        [SerializeField] private DeathCause cause = DeathCause.Spike;

        [Header("Respawn")]
        // The death-to-respawn pause. Lives on the strategy rather than RespawnDirector: falling
        // should respawn faster than being spiked — a property of the "death method", not the
        // "respawn system"
        [SerializeField] private float respawnDelay = 0.9f;

        public DeathCause Cause => cause;
        public float RespawnDelay => respawnDelay;

        /// <summary>
        /// Performs this death. Called by DeathDirector on LifeBus.Died.
        ///
        /// Implementors note: body.VisualBounds must be captured before body.Hide() — once rendering
        /// is off, bounds degenerate to zero size at the origin.
        /// </summary>
        public abstract void Execute(in DeathContext ctx, IDeathBody body);
    }
}
