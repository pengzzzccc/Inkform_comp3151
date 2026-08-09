namespace Inkform.Life
{
    /// <summary>
    /// Death cause. Decides which DeathStrategy performs this death — so "how you died" and "how the
    /// death plays out" are fully separated: Spike only needs to say it is a spike, never knowing what
    /// shards, screen shake, or respawn delay look like.
    ///
    /// Blast / Void currently have no publisher; they are reserved extension points: adding "bombs can
    /// kill" / "falling out of the world kills" only needs one strategy asset each, this enum stays unchanged.
    /// </summary>
    public enum DeathCause
    {
        Spike,      // spikes: the only cause actually triggered today
        Blast,      // death by explosion (reserved; Bomb currently only knocks back, never kills)
        Void,       // falling out of the world / abyss (reserved; no DeathZone exists yet)
    }
}
