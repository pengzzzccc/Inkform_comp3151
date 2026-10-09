namespace Inkform.Ability
{
    /// <summary>
    /// Stable ability ids. A separate class rather than string literals so AbilityStore's default
    /// set, the retired card pickups (AbilityPickupPart) and the v3 save migration all spell the
    /// same id.
    /// </summary>
    public static class AbilityIds
    {
        /// <summary>Using checkpoint machines. A built-in default (AbilityStore).</summary>
        public const string Checkpoint = "checkpoint";

        /// <summary>Firing the rope gun. A built-in default (AbilityStore).</summary>
        public const string RopeGun = "ropegun";
    }
}
