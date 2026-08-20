using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Inkform.LevelGraph
{
    /// <summary>
    /// Text ⇄ LevelGraphDocument. The file format is the source of truth for the level topology, so it
    /// is optimised for review rather than for compactness:
    ///
    /// <code>
    /// menu   MainMenu
    /// entry  L1_Player
    ///
    /// room   L1_Player  @0,0       "a laboratory in a cave"
    /// room   L1_S1      @-260,140  "a laboratory in a cave"
    ///
    /// link   L1_Player  &lt;-&gt; L1_S1
    /// link   L1_B3      -&gt;  L1_Boss  id:secret_door
    /// </code>
    ///
    /// Rooms and links are separate sections and every line is self-contained, so adding a door is a
    /// pure insertion — one new line, nothing else moves. That is the whole point: a topology change
    /// has to be readable in a diff, which it is not when it lives in 22 YAML assets full of GUIDs.
    ///
    /// `&lt;-&gt;` is sugar for both directions. Since the normal case is symmetric, an author who
    /// writes `-&gt;` is saying "one-way on purpose" — and the validator can then treat a missing return
    /// edge as intentional rather than nagging about it.
    ///
    /// Parsing never throws on malformed input: everything it cannot use becomes a ParseError with a
    /// line number and the document keeps whatever did parse. A designer with a typo on line 30 should
    /// still see the other 60 rooms in the window.
    /// </summary>
    public static class LevelGraphParser
    {
        public const string DefaultMenuScene = "MainMenu";

        /// <summary>One thing that could not be parsed. Line numbers are 1-based to match editors.</summary>
        public readonly struct ParseError
        {
            public readonly int Line;
            public readonly string Message;

            public ParseError(int line, string message)
            {
                Line = line;
                Message = message;
            }

            public override string ToString() => $"line {Line}: {Message}";
        }

        // ---- Parse ----

        public static LevelGraphDocument Parse(string text) => Parse(text, out _);

        public static LevelGraphDocument Parse(string text, out List<ParseError> errors)
        {
            var doc = new LevelGraphDocument();
            errors = new List<ParseError>();

            if (string.IsNullOrEmpty(text)) return doc;

            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                int lineNumber = i + 1;
                string line = StripComment(lines[i]).Trim();
                if (line.Length == 0) continue;

                List<string> tokens = Tokenize(line);
                if (tokens.Count == 0) continue;

                switch (tokens[0])
                {
                    case "menu":
                        if (tokens.Count < 2) { errors.Add(new ParseError(lineNumber, "menu needs a scene name")); break; }
                        doc.MenuScene = tokens[1];
                        break;

                    case "entry":
                        if (tokens.Count < 2) { errors.Add(new ParseError(lineNumber, "entry needs a room name")); break; }
                        doc.EntryRoom = tokens[1];
                        break;

                    case "room":
                        ParseRoom(doc, tokens, lineNumber, errors);
                        break;

                    case "link":
                        ParseLink(doc, tokens, lineNumber, errors);
                        break;

                    default:
                        errors.Add(new ParseError(lineNumber, $"unknown keyword '{tokens[0]}' (expected menu / entry / room / link)"));
                        break;
                }
            }

            return doc;
        }

        // room <name> [@x,y] ["display name"] [door N] — the optional parts may appear in any order,
        // because telling a designer their annotations have a required sequence buys nothing.
        private static void ParseRoom(LevelGraphDocument doc, List<string> tokens, int line, List<ParseError> errors)
        {
            if (tokens.Count < 2)
            {
                errors.Add(new ParseError(line, "room needs a name"));
                return;
            }

            string name = tokens[1];
            if (doc.HasRoom(name))
            {
                errors.Add(new ParseError(line, $"duplicate room '{name}'"));
                return;
            }

            var room = new RoomEntry { Name = name };

            for (int t = 2; t < tokens.Count; t++)
            {
                string token = tokens[t];

                if (token.StartsWith("@", StringComparison.Ordinal))
                {
                    if (TryParsePosition(token, out Vector2 pos)) room.Position = pos;
                    else errors.Add(new ParseError(line, $"could not read position '{token}' (expected @x,y)"));
                    continue;
                }

                if (token == "door")
                {
                    if (t + 1 >= tokens.Count)
                    {
                        errors.Add(new ParseError(line, "door needs a number (expected door N, 1..4)"));
                        continue;
                    }

                    if (!int.TryParse(tokens[++t], NumberStyles.Integer, CultureInfo.InvariantCulture, out int count)
                        || count < 1 || count > 4)
                    {
                        errors.Add(new ParseError(line, $"door must be a number from 1 to 4, got '{tokens[t]}'"));
                        continue;
                    }

                    room.DoorCount = count;
                    continue;
                }

                // Quoted text survives tokenization with its quotes stripped; anything else here is
                // a stray word the author probably meant to quote.
                if (string.IsNullOrEmpty(room.DisplayName)) room.DisplayName = token;
                else errors.Add(new ParseError(line, $"unexpected extra token '{token}' on a room line"));
            }

            doc.Rooms.Add(room);
        }

        // link <from> (<-> | ->) <to> [id:<exitId>]
        private static void ParseLink(LevelGraphDocument doc, List<string> tokens, int line, List<ParseError> errors)
        {
            if (tokens.Count < 4)
            {
                errors.Add(new ParseError(line, "link needs: <from> <-> <to>  (or -> for one-way)"));
                return;
            }

            string arrow = tokens[2];
            bool bidirectional;
            if (arrow == "<->") bidirectional = true;
            else if (arrow == "->") bidirectional = false;
            else
            {
                errors.Add(new ParseError(line, $"'{arrow}' is not a link arrow (expected <-> or ->)"));
                return;
            }

            var link = new LinkEntry(tokens[1], tokens[3], bidirectional);

            for (int t = 4; t < tokens.Count; t++)
            {
                string token = tokens[t];
                if (!token.StartsWith("id:", StringComparison.Ordinal))
                {
                    errors.Add(new ParseError(line, $"unexpected extra token '{token}' on a link line"));
                    continue;
                }

                // A custom id on a two-way link cannot say which direction it means. Rejected outright
                // rather than resolved by a rule nobody would remember — write two -> lines instead.
                if (bidirectional)
                {
                    errors.Add(new ParseError(line, "id: is only allowed on a one-way (->) link; write two -> lines to give each direction its own id"));
                    continue;
                }

                link.ExitId = token.Substring(3);
            }

            doc.Links.Add(link);
        }

        private static bool TryParsePosition(string token, out Vector2 position)
        {
            position = Vector2.zero;

            string body = token.Substring(1);
            int comma = body.IndexOf(',');
            if (comma <= 0) return false;

            if (!float.TryParse(body.Substring(0, comma), NumberStyles.Float, CultureInfo.InvariantCulture, out float x)) return false;
            if (!float.TryParse(body.Substring(comma + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float y)) return false;

            position = new Vector2(x, y);
            return true;
        }

        /// <summary>Drops a trailing comment, ignoring '#' inside quotes so a display name may contain one.</summary>
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

        /// <summary>Splits on whitespace, keeping quoted runs together and stripping their quotes.</summary>
        private static List<string> Tokenize(string line)
        {
            var tokens = new List<string>();
            var current = new StringBuilder();
            bool inQuotes = false;
            bool hasToken = false;   // distinguishes an empty quoted string from no token at all

            foreach (char c in line)
            {
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                    hasToken = true;
                    continue;
                }

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

        // ---- Serialize ----

        /// <summary>
        /// Writes the document back out. Column alignment is computed from the longest name so the
        /// arrows line up — a wall of links is only scannable when they do.
        ///
        /// Always emits '\n'. The .gitattributes marks the repo text=auto, and a serializer that
        /// emitted the platform separator would make every Apply on Windows look like a whole-file
        /// rewrite to anyone on another OS.
        /// </summary>
        public static string Serialize(LevelGraphDocument doc)
        {
            var sb = new StringBuilder();

            sb.Append("# Inkform level graph — the single source of truth for the level topology.\n");
            sb.Append("# Edit here or in Tools > Inkform > Level Graph; the window writes this file back.\n");
            sb.Append("# The LevelScene / LevelFlow assets are generated from this — do not hand-edit them.\n");
            sb.Append('\n');
            sb.Append("menu   ").Append(doc.MenuScene).Append('\n');
            sb.Append("entry  ").Append(doc.EntryRoom).Append('\n');

            if (doc.Rooms.Count > 0)
            {
                sb.Append('\n');

                int nameWidth = 0;
                int posWidth = 0;
                foreach (RoomEntry r in doc.Rooms)
                {
                    nameWidth = Mathf.Max(nameWidth, r.Name.Length);
                    posWidth = Mathf.Max(posWidth, FormatPosition(r.Position).Length);
                }

                foreach (RoomEntry r in doc.Rooms)
                {
                    sb.Append("room   ").Append(r.Name.PadRight(nameWidth));
                    sb.Append("  ").Append(FormatPosition(r.Position).PadRight(posWidth));

                    if (!string.IsNullOrEmpty(r.DisplayName))
                        sb.Append("  \"").Append(r.DisplayName).Append('"');

                    // Only rooms the designer has capped write their count: the default of 4 matches
                    // what every generated room already has, and a silent line per room would turn an
                    // untouched graph into a whole-file diff on first Apply.
                    if (r.DoorCount != 4)
                        sb.Append("  door ").Append(r.DoorCount);

                    sb.Append('\n');
                }
            }

            if (doc.Links.Count > 0)
            {
                sb.Append('\n');

                int fromWidth = 0;
                foreach (LinkEntry l in doc.Links) fromWidth = Mathf.Max(fromWidth, l.From.Length);

                foreach (LinkEntry l in doc.Links)
                {
                    sb.Append("link   ").Append(l.From.PadRight(fromWidth));
                    sb.Append(l.Bidirectional ? "  <-> " : "  ->  ").Append(l.To);

                    if (!string.IsNullOrEmpty(l.ExitId)) sb.Append("  id:").Append(l.ExitId);

                    sb.Append('\n');
                }
            }

            return sb.ToString();
        }

        // Rounded to whole units: blueprint coordinates carry no meaning below a pixel, and letting
        // float noise into the file would make a nudge of 0.0001 show up as a diff.
        private static string FormatPosition(Vector2 p) =>
            "@" + Mathf.RoundToInt(p.x).ToString(CultureInfo.InvariantCulture)
                + "," + Mathf.RoundToInt(p.y).ToString(CultureInfo.InvariantCulture);
    }
}
