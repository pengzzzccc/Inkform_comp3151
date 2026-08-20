using System.Collections.Generic;

namespace Inkform.LevelGraph
{
    /// <summary>
    /// Graph-level validation (the "A" rules): everything that can be decided from the topology alone,
    /// with no AssetDatabase and no scene loading. Cheap enough to re-run on every keystroke in the
    /// blueprint window, and — because it is a pure function of a document — the part of the tool that
    /// unit tests can cover completely.
    ///
    /// Project-level (build settings) and scene-level (C) rules live in the editor assembly, because
    /// they need LevelExit / Checkpoint and therefore the predefined Assembly-CSharp, which an
    /// assembly definition cannot reference.
    /// </summary>
    public static class LevelGraphValidator
    {
        /// <summary>How many doors RoomBuilder's greybox template can physically place (right-ground,
        /// right-high, left-ground, top). Exceeding it is legal in the graph but leaves the extra
        /// doors unbuilt in a generated room, which is a silent dead end.</summary>
        public const int GreyboxDoorSlots = 4;

        public static List<Finding> Validate(LevelGraphDocument doc)
        {
            var findings = new List<Finding>();
            if (doc == null) return findings;

            var directed = new List<DirectedLink>(doc.EnumerateDirected());

            CheckEntry(doc, findings);
            CheckLinkEndpoints(doc, directed, findings);
            CheckDuplicateExitIds(doc, directed, findings);
            CheckDeadEnds(doc, directed, findings);
            CheckDoorSlots(doc, directed, findings);
            CheckOneWay(doc, directed, findings);
            CheckReachability(doc, directed, findings);

            return findings;
        }

        // A1 — without a valid entry, StartNewGame has nothing to load.
        private static void CheckEntry(LevelGraphDocument doc, List<Finding> findings)
        {
            if (string.IsNullOrEmpty(doc.EntryRoom))
            {
                findings.Add(new Finding
                {
                    Severity = Severity.Error,
                    Code = "A1",
                    Message = "No entry room set — New Game would have nowhere to go.",
                });
                return;
            }

            if (!doc.HasRoom(doc.EntryRoom))
            {
                findings.Add(new Finding
                {
                    Severity = Severity.Error,
                    Code = "A1",
                    Room = doc.EntryRoom,
                    Message = $"Entry room '{doc.EntryRoom}' is not declared as a room.",
                });
            }
        }

        // A4 — a link naming a room that does not exist becomes a door that drops the player back to
        // the main menu at runtime (SceneDirector's dead-end fallback).
        private static void CheckLinkEndpoints(LevelGraphDocument doc, List<DirectedLink> directed, List<Finding> findings)
        {
            foreach (LinkEntry link in doc.Links)
            {
                if (!doc.HasRoom(link.From))
                {
                    findings.Add(new Finding
                    {
                        Severity = Severity.Error,
                        Code = "A4",
                        Room = link.From,
                        Other = link.To,
                        Message = $"Link starts at '{link.From}', which is not declared as a room.",
                    });
                }

                if (!doc.HasRoom(link.To))
                {
                    findings.Add(new Finding
                    {
                        Severity = Severity.Error,
                        Code = "A4",
                        Room = link.From,
                        Other = link.To,
                        Message = $"Link from '{link.From}' leads to '{link.To}', which is not declared as a room.",
                    });
                }
            }
        }

        // A5 — the runtime graph's TargetOf takes the first id that matches, so a duplicate silently
        // makes one of the two doors unreachable no matter which trigger the player walks into.
        private static void CheckDuplicateExitIds(LevelGraphDocument doc, List<DirectedLink> directed, List<Finding> findings)
        {
            var seen = new Dictionary<string, HashSet<string>>();

            foreach (DirectedLink link in directed)
            {
                if (!seen.TryGetValue(link.From, out HashSet<string> ids))
                {
                    ids = new HashSet<string>();
                    seen[link.From] = ids;
                }

                if (!ids.Add(link.ExitId))
                {
                    findings.Add(new Finding
                    {
                        Severity = Severity.Error,
                        Code = "A5",
                        Room = link.From,
                        Other = link.To,
                        ExitId = link.ExitId,
                        Message = $"'{link.From}' has two exits with id '{link.ExitId}' — only the first can ever fire.",
                    });
                }
            }
        }

        // A6 — a room nobody can leave. Legal for a final room, so this is a warning, not an error.
        private static void CheckDeadEnds(LevelGraphDocument doc, List<DirectedLink> directed, List<Finding> findings)
        {
            var hasExit = new HashSet<string>();
            foreach (DirectedLink link in directed) hasExit.Add(link.From);

            foreach (RoomEntry room in doc.Rooms)
            {
                if (hasExit.Contains(room.Name)) continue;

                findings.Add(new Finding
                {
                    Severity = Severity.Warning,
                    Code = "A6",
                    Room = room.Name,
                    Message = $"'{room.Name}' has no exits — the player can enter but never leave.",
                });
            }
        }

        // A7 — the greybox template has four door slots; a fifth exit has nowhere to be placed.
        private static void CheckDoorSlots(LevelGraphDocument doc, List<DirectedLink> directed, List<Finding> findings)
        {
            var exitCount = new Dictionary<string, int>();
            foreach (DirectedLink link in directed)
            {
                exitCount.TryGetValue(link.From, out int n);
                exitCount[link.From] = n + 1;
            }

            foreach (RoomEntry room in doc.Rooms)
            {
                if (!exitCount.TryGetValue(room.Name, out int n) || n <= GreyboxDoorSlots) continue;

                findings.Add(new Finding
                {
                    Severity = Severity.Warning,
                    Code = "A7",
                    Room = room.Name,
                    Message = $"'{room.Name}' has {n} exits but a generated room only has {GreyboxDoorSlots} door slots — "
                            + "the extra doors must be placed by hand.",
                });
            }
        }

        // A3 — reported at Info, not Warning: writing `->` is how an author says "one-way on purpose".
        // The case actually worth catching (a forgotten return edge) shows up as A8 instead, because a
        // one-way door only hurts when it strands the player.
        private static void CheckOneWay(LevelGraphDocument doc, List<DirectedLink> directed, List<Finding> findings)
        {
            var edges = new HashSet<(string, string)>();
            foreach (DirectedLink link in directed) edges.Add((link.From, link.To));

            foreach (LinkEntry link in doc.Links)
            {
                if (link.Bidirectional) continue;
                if (edges.Contains((link.To, link.From))) continue;

                findings.Add(new Finding
                {
                    Severity = Severity.Info,
                    Code = "A3",
                    Room = link.From,
                    Other = link.To,
                    Message = $"'{link.From}' → '{link.To}' is one-way (no return door).",
                });
            }
        }

        // A2 / A8 — the two halves of "can the player actually get around".
        // A2: rooms the player can never reach. A8: rooms the player can reach but not come back from,
        // which is a softlock unless it is the intended final room.
        private static void CheckReachability(LevelGraphDocument doc, List<DirectedLink> directed, List<Finding> findings)
        {
            if (string.IsNullOrEmpty(doc.EntryRoom) || !doc.HasRoom(doc.EntryRoom)) return;

            HashSet<string> forward = Reachable(doc.EntryRoom, directed, reverse: false);
            HashSet<string> backward = Reachable(doc.EntryRoom, directed, reverse: true);

            foreach (RoomEntry room in doc.Rooms)
            {
                if (!forward.Contains(room.Name))
                {
                    findings.Add(new Finding
                    {
                        Severity = Severity.Error,
                        Code = "A2",
                        Room = room.Name,
                        Message = $"'{room.Name}' is unreachable from the entry room '{doc.EntryRoom}'.",
                    });
                    continue;   // no point also reporting it cannot get back
                }

                if (!backward.Contains(room.Name))
                {
                    findings.Add(new Finding
                    {
                        Severity = Severity.Warning,
                        Code = "A8",
                        Room = room.Name,
                        Message = $"'{room.Name}' can be entered but there is no route back to '{doc.EntryRoom}' — "
                                + "the player would be stranded there.",
                    });
                }
            }
        }

        /// <summary>Breadth-first walk from a room. reverse=true follows edges backwards, which answers
        /// "which rooms can get *to* the start" rather than "which rooms can be reached from it".</summary>
        private static HashSet<string> Reachable(string start, List<DirectedLink> directed, bool reverse)
        {
            var adjacency = new Dictionary<string, List<string>>();
            foreach (DirectedLink link in directed)
            {
                string from = reverse ? link.To : link.From;
                string to = reverse ? link.From : link.To;

                if (!adjacency.TryGetValue(from, out List<string> list))
                {
                    list = new List<string>();
                    adjacency[from] = list;
                }
                list.Add(to);
            }

            var seen = new HashSet<string> { start };
            var queue = new Queue<string>();
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                string current = queue.Dequeue();
                if (!adjacency.TryGetValue(current, out List<string> neighbours)) continue;

                foreach (string next in neighbours)
                {
                    if (seen.Add(next)) queue.Enqueue(next);
                }
            }

            return seen;
        }
    }
}
