using System.Collections.Generic;
using UnityEngine;

namespace Inkform.Level
{
    /// <summary>
    /// The level topology as runtime data: menu scene, entry scene, display names, and the directed
    /// door edges (scene + exitId → target scene). Parsed once at startup by LevelGraphReader from
    /// LevelGraph.txt — the same text file the editor window edits, so what the window shows is
    /// literally what the game runs. No generated assets in between to drift out of sync.
    ///
    /// Scene names are the identity everywhere: a room IS its scene file name, and an exitId defaults
    /// to the target room's name (the "one string, four places" convention the doors follow).
    /// </summary>
    public class LevelGraph
    {
        public string MenuScene = "MainMenu";
        public string EntryScene = "";

        // display name per room (may be empty — callers fall back to the scene name)
        private readonly Dictionary<string, string> displayNames = new Dictionary<string, string>();

        // outgoing doors per room: (exitId, target scene)
        private readonly Dictionary<string, List<(string exitId, string to)>> edges =
            new Dictionary<string, List<(string exitId, string to)>>();

        public IReadOnlyDictionary<string, string> DisplayNames => displayNames;

        public void AddRoom(string sceneName, string displayName)
        {
            displayNames[sceneName] = displayName ?? "";
            if (!edges.ContainsKey(sceneName)) edges[sceneName] = new List<(string, string)>();
        }

        public void AddEdge(string from, string exitId, string to)
        {
            if (!edges.TryGetValue(from, out List<(string, string)> list))
            {
                list = new List<(string, string)>();
                edges[from] = list;
            }
            list.Add((exitId ?? "", to));
        }

        public bool HasRoom(string sceneName) => displayNames.ContainsKey(sceneName);

        public bool IsMenuScene(string sceneName) => sceneName == MenuScene;

        /// <summary>Where the door with this exitId in this scene leads, or null when the topology has
        /// no such edge (SceneDirector decides what a dead end does).</summary>
        public string TargetOf(string fromScene, string exitId)
        {
            if (fromScene == null || !edges.TryGetValue(fromScene, out List<(string, string)> list)) return null;

            foreach ((string id, string to) in list)
            {
                if (id == exitId) return to;
            }
            return null;
        }

        /// <summary>The player-facing name for a room, falling back to the scene name.</summary>
        public string DisplayNameOf(string sceneName)
        {
            if (sceneName != null
                && displayNames.TryGetValue(sceneName, out string name)
                && !string.IsNullOrEmpty(name)) return name;
            return sceneName;
        }
    }

    /// <summary>
    /// Parses LevelGraph.txt into a LevelGraph. A deliberately small subset of the editor parser:
    /// it reads menu / entry / room (name + display name) / link lines and ignores the editor-only
    /// annotations (node positions, door counts). Malformed lines are skipped, never thrown — a typo
    /// in the file must not take the whole boot down, and the editor window reports the line anyway.
    ///
    /// The editor's own parser (Editor/LevelGraph/Core) cannot be reused here: it lives in an
    /// editor-only assembly, and moving it into the player build to share ~60 lines would couple the
    /// whole tooling namespace into the game.
    /// </summary>
    public static class LevelGraphReader
    {
        public static LevelGraph Parse(TextAsset asset)
        {
            var graph = new LevelGraph();
            if (asset == null || string.IsNullOrEmpty(asset.text)) return graph;

            foreach (string rawLine in asset.text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                string line = StripComment(rawLine).Trim();
                if (line.Length == 0) continue;

                List<string> tokens = Tokenize(line);
                if (tokens.Count == 0) continue;

                switch (tokens[0])
                {
                    case "menu":
                        if (tokens.Count >= 2) graph.MenuScene = tokens[1];
                        break;

                    case "entry":
                        if (tokens.Count >= 2) graph.EntryScene = tokens[1];
                        break;

                    case "room":
                        // room <name> [@x,y] ["display"] [door N] — runtime keeps name + display only
                        if (tokens.Count >= 2) graph.AddRoom(tokens[1], ReadDisplayName(tokens));
                        break;

                    case "link":
                        ParseLink(graph, tokens);
                        break;
                }
            }

            return graph;
        }

        // link <from> (<-> | ->) <to> [id:<exitId>] — a two-way link is sugar for both directions,
        // and the reverse direction always takes the default exit id (the source room's name).
        private static void ParseLink(LevelGraph graph, List<string> tokens)
        {
            if (tokens.Count < 4) return;

            string from = tokens[1];
            string arrow = tokens[2];
            string to = tokens[3];

            string exitId = "";
            for (int t = 4; t < tokens.Count; t++)
            {
                if (tokens[t].StartsWith("id:") && tokens[t].Length > 3) exitId = tokens[t].Substring(3);
            }

            if (arrow == "<->")
            {
                graph.AddEdge(from, to, to);          // forward: default id = target name
                graph.AddEdge(to, from, from);        // reverse: default id = source name
            }
            else if (arrow == "->")
            {
                graph.AddEdge(from, string.IsNullOrEmpty(exitId) ? to : exitId, to);
            }
        }

        // The display name is the first quoted token after the room name; positions (@x,y) and door
        // counts are editor annotations the runtime does not need.
        private static string ReadDisplayName(List<string> tokens)
        {
            for (int t = 2; t < tokens.Count; t++)
            {
                if (tokens[t].StartsWith("@", System.StringComparison.Ordinal)) continue;
                if (tokens[t] == "door") { t++; continue; }    // skip the number after "door"
                return tokens[t];
            }
            return "";
        }

        private static string StripComment(string line)
        {
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"') inQuotes = !inQuotes;
                else if (c == '#' && !inQuotes) return line.Substring(0, i);
            }
            return line;
        }

        private static List<string> Tokenize(string line)
        {
            var tokens = new List<string>();
            var current = new System.Text.StringBuilder();
            bool inQuotes = false;
            bool hasToken = false;

            foreach (char c in line)
            {
                if (c == '"') { inQuotes = !inQuotes; hasToken = true; continue; }

                if (!inQuotes && char.IsWhiteSpace(c))
                {
                    if (hasToken) { tokens.Add(current.ToString()); current.Clear(); hasToken = false; }
                    continue;
                }

                current.Append(c);
                hasToken = true;
            }

            if (hasToken) tokens.Add(current.ToString());
            return tokens;
        }
    }
}
