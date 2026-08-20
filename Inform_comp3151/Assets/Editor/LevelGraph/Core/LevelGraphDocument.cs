using System.Collections.Generic;
using UnityEngine;

namespace Inkform.LevelGraph
{
    /// <summary>
    /// The level topology as data: which rooms exist, which doors connect them, where the entry is.
    /// This is the in-memory form of LevelGraph.txt, which is the single source of truth — the editor
    /// window edits it, and SceneDirector reads it at runtime through its own lightweight parser.
    ///
    /// Rooms and links keep their authored order so a round trip through the parser is byte-stable:
    /// re-serializing a document nobody touched must produce the file it came from, or every Apply
    /// would churn the diff and the file would stop being reviewable.
    ///
    /// Touches no AssetDatabase and opens no scenes — that is what makes it (and the parser and the
    /// graph-level validator) unit-testable without a project fixture.
    /// </summary>
    public class LevelGraphDocument
    {
        /// <summary>Menu scene name, mirrored into the runtime graph's MenuScene.</summary>
        public string MenuScene = "MainMenu";

        /// <summary>Room a fresh run starts in, mirrored into the runtime graph's EntryScene.</summary>
        public string EntryRoom = "";

        public readonly List<RoomEntry> Rooms = new List<RoomEntry>();
        public readonly List<LinkEntry> Links = new List<LinkEntry>();

        public RoomEntry FindRoom(string name)
        {
            foreach (RoomEntry r in Rooms)
            {
                if (r.Name == name) return r;
            }
            return null;
        }

        public bool HasRoom(string name) => FindRoom(name) != null;

        /// <summary>
        /// Every link as a one-way edge: a bidirectional link yields two. Validation and asset
        /// generation both work on this expansion, while the document keeps the authored `&lt;-&gt;`
        /// form so serializing it back does not double the line count.
        /// </summary>
        public IEnumerable<DirectedLink> EnumerateDirected()
        {
            foreach (LinkEntry link in Links)
            {
                yield return new DirectedLink(link.From, link.To, link.EffectiveExitId, link);

                if (link.Bidirectional)
                {
                    // The reverse of a bidirectional link always takes the default exit id (the source
                    // room's name). A custom id cannot be authored on `<->` at all — see LinkEntry.
                    yield return new DirectedLink(link.To, link.From, link.From, link);
                }
            }
        }
    }

    /// <summary>One room: a scene, a name to show players, and where its node sits in the blueprint.</summary>
    public class RoomEntry
    {
        /// <summary>Scene file name. Doubles as the default exit id of every door leading here —
        /// the "one string, four places" convention RoomBuilder set.</summary>
        public string Name = "";

        /// <summary>Shown to players (the save menu's slot rows). Empty falls back to Name.</summary>
        public string DisplayName = "";

        /// <summary>How many outgoing doors this room may have. The node graph enforces it as the
        /// connection cap (a bidirectional link counts against both ends), and it mirrors the four
        /// door slots RoomBuilder lays out per room. Defaults to 4; a room line without `door N`
        /// parses as 4, so existing graphs stay byte-stable.</summary>
        public int DoorCount = 4;

        /// <summary>Node position in the blueprint window. Persisted in the text file because a layout
        /// that lives only in the window is a layout the designer loses on every reopen.</summary>
        public Vector2 Position;

        public RoomEntry() { }

        public RoomEntry(string name, Vector2 position, string displayName = "", int doorCount = 4)
        {
            Name = name;
            Position = position;
            DisplayName = displayName;
            DoorCount = doorCount;
        }
    }

    /// <summary>
    /// One authored connection. `Bidirectional` records how it was written (`&lt;-&gt;` vs `-&gt;`) rather
    /// than being normalized away, for two reasons: the file round-trips unchanged, and a reader can
    /// tell an intentional one-way door from a forgotten return edge at a glance.
    /// </summary>
    public class LinkEntry
    {
        public string From = "";
        public string To = "";
        public bool Bidirectional;

        /// <summary>
        /// Explicit exit id; empty means "use the target room's name", which is the convention every
        /// generated door already follows. Only legal on a one-way link: on `&lt;-&gt;` it would be
        /// ambiguous which direction it applied to, so the parser rejects that combination outright
        /// rather than picking a rule nobody would remember.
        /// </summary>
        public string ExitId = "";

        public string EffectiveExitId => string.IsNullOrEmpty(ExitId) ? To : ExitId;

        public LinkEntry() { }

        public LinkEntry(string from, string to, bool bidirectional = true, string exitId = "")
        {
            From = from;
            To = to;
            Bidirectional = bidirectional;
            ExitId = exitId;
        }
    }

    /// <summary>One direction of a link, flattened for validation and asset generation.</summary>
    public readonly struct DirectedLink
    {
        public readonly string From;
        public readonly string To;
        public readonly string ExitId;

        /// <summary>The authored line this came from, so a finding can say whether the one-way-ness
        /// was deliberate (`-&gt;`) or is one half of a `&lt;-&gt;`.</summary>
        public readonly LinkEntry Source;

        public DirectedLink(string from, string to, string exitId, LinkEntry source)
        {
            From = from;
            To = to;
            ExitId = exitId;
            Source = source;
        }
    }
}
