using System.Collections.Generic;

namespace Inkform.LevelGraph
{
    public enum RuleLayer
    {
        /// <summary>Decidable from the topology alone. Re-run on every edit.</summary>
        Graph,

        /// <summary>Needs the LevelScene / LevelFlow assets and Build Settings.</summary>
        Assets,

        /// <summary>Needs the room scenes opened.</summary>
        Scenes,
    }

    /// <summary>One validation rule, as the help panel describes it.</summary>
    public readonly struct RuleInfo
    {
        public readonly string Code;
        public readonly RuleLayer Layer;
        public readonly Severity Severity;
        public readonly string Title;

        /// <summary>Whether findings of this rule normally offer a Fix button.</summary>
        public readonly bool Fixable;

        public RuleInfo(string code, RuleLayer layer, Severity severity, string title, bool fixable)
        {
            Code = code;
            Layer = layer;
            Severity = severity;
            Title = title;
            Fixable = fixable;
        }
    }

    /// <summary>
    /// The catalogue of validation rules, for the help panel to render.
    ///
    /// It lives here, beside the rules themselves, rather than as strings inside the window. The rule
    /// codes only exist as literals scattered through the two validators — there is no other table of
    /// them — so a description hardcoded in the UI would be a second source of truth that silently
    /// drifts the first time a rule changes. Sitting in the same folder and the same assembly at least
    /// makes it impossible to edit the rules without seeing this.
    ///
    /// LevelGraphRulesTests keeps the Graph-layer entries honest: it asserts that every code the
    /// validator actually emits appears here with a matching severity, and that every Graph entry here
    /// is reachable by some fixture. The Assets and Scenes layers cannot be covered that way — they
    /// need a project on disk — so those entries are maintained by hand.
    /// </summary>
    public static class LevelGraphRules
    {
        /// <summary>Not a rule: the code the window uses for LevelGraph.txt parse errors.</summary>
        public const string ParseErrorCode = "P";

        public static readonly RuleInfo[] All =
        {
            new RuleInfo(ParseErrorCode, RuleLayer.Graph, Severity.Error,
                "LevelGraph.txt could not be read on this line.", false),

            // ---- Graph ----
            new RuleInfo("A1", RuleLayer.Graph, Severity.Error,
                "No entry room, or the entry names a room that is not declared.", false),
            new RuleInfo("A2", RuleLayer.Graph, Severity.Error,
                "Room is unreachable from the entry room.", false),
            new RuleInfo("A3", RuleLayer.Graph, Severity.Info,
                "One-way door: no return link. Written with -> so it reads as deliberate.", false),
            new RuleInfo("A4", RuleLayer.Graph, Severity.Error,
                "Link names a room that is not declared.", false),
            new RuleInfo("A5", RuleLayer.Graph, Severity.Error,
                "Two exits out of one room share an id — only the first can ever fire.", false),
            new RuleInfo("A6", RuleLayer.Graph, Severity.Warning,
                "Room has no exits: the player can enter but never leave.", false),
            new RuleInfo("A7", RuleLayer.Graph, Severity.Warning,
                "More exits than a generated room has door slots (4) — place the extras by hand.", false),
            new RuleInfo("A8", RuleLayer.Graph, Severity.Warning,
                "No route back to the entry: reaching this room strands the player.", false),

            // ---- Assets ----
            new RuleInfo("B8", RuleLayer.Assets, Severity.Error,
                "No LevelScene asset for this room.", true),
            new RuleInfo("B9", RuleLayer.Assets, Severity.Error,
                "Asset sceneName does not match — FindBySceneName will never resolve it.", true),
            new RuleInfo("B9b", RuleLayer.Assets, Severity.Info,
                "Asset display name differs from the graph.", true),
            new RuleInfo("B10", RuleLayer.Assets, Severity.Warning,
                "LevelScene asset exists but is not in the graph. Reported only, never deleted.", false),
            new RuleInfo("B11", RuleLayer.Assets, Severity.Error,
                "LevelFlow does not match the graph (entry / menu / room list).", true),

            // ---- Scenes ----
            new RuleInfo("C12", RuleLayer.Scenes, Severity.Error,
                "No scene file with this name. Room Builder can bootstrap a greybox.", false),
            new RuleInfo("C13", RuleLayer.Scenes, Severity.Error,
                "Scene is not in Build Settings — loading it would silently do nothing.", true),
            new RuleInfo("C14", RuleLayer.Scenes, Severity.Warning,
                "The menu scene is not the first enabled entry in Build Settings.", true),
            new RuleInfo("C15", RuleLayer.Scenes, Severity.Error,
                "The graph promises a door this scene does not have.", true),
            new RuleInfo("C16", RuleLayer.Scenes, Severity.Warning,
                "Scene has a LevelExit the graph has no link for. Reported only, never deleted.", false),
            new RuleInfo("C17", RuleLayer.Scenes, Severity.Error,
                "LevelExit collider is not a trigger, so the door can never fire.", true),
            new RuleInfo("C18", RuleLayer.Scenes, Severity.Warning,
                "No Spawn_<source> checkpoint: arriving through that door lands at the level start.", true),
            new RuleInfo("C19", RuleLayer.Scenes, Severity.Warning,
                "No checkpoint marked isStartPoint; respawns fall back to the player's placed position.", true),
            new RuleInfo("C20", RuleLayer.Scenes, Severity.Error,
                "Checkpoint collider is not a trigger, so it can never fire.", true),
        };

        public static bool TryGet(string code, out RuleInfo info)
        {
            foreach (RuleInfo rule in All)
            {
                if (rule.Code == code) { info = rule; return true; }
            }

            info = default;
            return false;
        }

        public static IEnumerable<RuleInfo> InLayer(RuleLayer layer)
        {
            foreach (RuleInfo rule in All)
            {
                if (rule.Layer == layer) yield return rule;
            }
        }
    }
}
