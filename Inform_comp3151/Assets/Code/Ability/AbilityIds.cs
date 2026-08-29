namespace Inkform.Ability
{
    /// <summary>
    /// Stable ability ids shared by granters and gates. A separate class rather than string literals
    /// so the pickup (AbilityPickupPart), the gate (Checkpoint) and the v3 save migration all spell
    /// the same id the way TimeCardItemId used to — one typo away from a gate that never opens.
    /// </summary>
    public static class AbilityIds
    {
        /// <summary>Using checkpoint machines. Granted by picking up the timecard.</summary>
        public const string Checkpoint = "checkpoint";
    }
}
