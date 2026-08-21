namespace Inkform.LevelGraph
{
    public enum Severity
    {
        Info,
        Warning,
        Error,
    }

    /// <summary>
    /// What a finding can be repaired with. Expressed as data rather than as a delegate carried on the
    /// finding, so the validators stay pure functions of their input and remain testable without an
    /// editor fixture — the fixer is the only thing that knows how to touch assets and scenes.
    ///
    /// Every action here is **additive**. There is deliberately no Delete/Rebuild member: the tool may
    /// create a door the graph says is missing, but an extra door the graph does not know about is
    /// very likely a secret passage the designer placed on purpose, and no tool has the standing to
    /// decide that. Reporting it is the whole contribution.
    /// </summary>
    public enum FixAction
    {
        None = 0,

        // Build settings
        AddSceneToBuild,
        MoveMenuSceneFirst,

        // Scenes
        CreateLevelExit,
        CreateDoorSpawn,
        CreateStartPoint,
        MakeColliderTrigger,
    }

    /// <summary>
    /// One thing that is wrong, or worth a second look, about the level graph. Codes are stable
    /// ("A2", "C15") so they can be referenced in review comments and in the plan's rule tables
    /// without quoting the whole message.
    /// </summary>
    public class Finding
    {
        public Severity Severity = Severity.Warning;

        /// <summary>Stable rule id: A = graph, B = assets, C = scenes.</summary>
        public string Code = "";

        /// <summary>Room the finding is about — what the window selects when you click it.</summary>
        public string Room = "";

        /// <summary>The far side, for findings about a connection. Empty for room-level findings.</summary>
        public string Other = "";

        /// <summary>Exit id involved, for door findings.</summary>
        public string ExitId = "";

        public string Message = "";

        public FixAction Fix = FixAction.None;

        public bool CanFix => Fix != FixAction.None;

        public override string ToString() => $"[{Code}] {Message}";
    }
}
