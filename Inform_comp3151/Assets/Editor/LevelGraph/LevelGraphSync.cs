using System.Collections.Generic;
using Inkform.Level;
using UnityEditor;
using UnityEngine;

namespace Inkform.LevelGraph.EditorTools
{
    /// <summary>
    /// Generates the LevelScene / LevelFlow assets from the graph document. One direction only: the
    /// text file is the source of truth, these assets are its build product. Hand edits to them are
    /// overwritten on the next Apply, which is why the serializer stamps a "do not hand-edit" header
    /// into the file and the assets are the thing being generated rather than the thing being edited.
    ///
    /// Idempotent: running it twice changes nothing the second time, and running it on an unmodified
    /// graph leaves every asset byte-identical. That property is what makes "export the current 22
    /// rooms, Apply, and check git shows no diff" a meaningful test of the whole tool.
    /// </summary>
    public static class LevelGraphSync
    {
        /// <summary>
        /// Writes the document out to assets. Returns how many assets were touched.
        ///
        /// All field writes go through SerializedObject rather than plain assignment + SetDirty:
        /// RoomBuilder learned the hard way that assigning LevelScene.sceneName directly did not
        /// persist, and every asset saved with an empty sceneName, which silently killed
        /// LevelFlow.FindBySceneName at runtime. Same pipeline here, same reason.
        /// </summary>
        public static int Apply(LevelGraphDocument doc)
        {
            EnsureFolder(LevelGraphFile.LevelsDir);

            // Pass 1: create-or-load every asset first, so connection targets all exist before wiring.
            var assets = new Dictionary<string, LevelScene>();
            int touched = 0;

            foreach (RoomEntry room in doc.Rooms)
            {
                string path = LevelGraphFile.AssetPathFor(room.Name);
                LevelScene level = AssetDatabase.LoadAssetAtPath<LevelScene>(path);

                if (level == null)
                {
                    level = ScriptableObject.CreateInstance<LevelScene>();
                    level.name = room.Name;
                    level.sceneName = room.Name;    // correct on the very first write
                    AssetDatabase.CreateAsset(level, path);
                    touched++;
                }

                var so = new SerializedObject(level);
                bool changed = SetString(so, "sceneName", room.Name);
                changed |= SetString(so, "displayName", room.DisplayName);
                if (changed) { so.ApplyModifiedPropertiesWithoutUndo(); touched++; }

                assets[room.Name] = level;
            }

            // Pass 2: rebuild every connection list from the directed expansion of the links.
            var connections = new Dictionary<string, List<LevelConnection>>();
            foreach (RoomEntry room in doc.Rooms) connections[room.Name] = new List<LevelConnection>();

            foreach (DirectedLink link in doc.EnumerateDirected())
            {
                if (!connections.TryGetValue(link.From, out List<LevelConnection> list)) continue;   // A4, already reported
                if (!assets.TryGetValue(link.To, out LevelScene target)) continue;

                list.Add(new LevelConnection { id = link.ExitId, target = target });
            }

            foreach (RoomEntry room in doc.Rooms)
            {
                LevelScene level = assets[room.Name];
                LevelConnection[] next = connections[room.Name].ToArray();

                if (SameConnections(level.connections, next)) continue;

                level.connections = next;
                EditorUtility.SetDirty(level);
                touched++;
            }

            // Pass 3: the flow root.
            LevelFlow flow = AssetDatabase.LoadAssetAtPath<LevelFlow>(LevelGraphFile.FlowAssetPath);
            if (flow == null)
            {
                flow = ScriptableObject.CreateInstance<LevelFlow>();
                AssetDatabase.CreateAsset(flow, LevelGraphFile.FlowAssetPath);
                touched++;
            }

            LevelScene entry = null;
            if (!string.IsNullOrEmpty(doc.EntryRoom)) assets.TryGetValue(doc.EntryRoom, out entry);

            var ordered = new List<LevelScene>(doc.Rooms.Count);
            foreach (RoomEntry room in doc.Rooms) ordered.Add(assets[room.Name]);

            if (flow.mainMenuSceneName != doc.MenuScene || flow.entryLevel != entry || !SameLevels(flow.levels, ordered))
            {
                flow.mainMenuSceneName = doc.MenuScene;
                flow.entryLevel = entry;
                flow.levels = ordered.ToArray();
                EditorUtility.SetDirty(flow);
                touched++;
            }

            if (touched > 0) AssetDatabase.SaveAssets();
            return touched;
        }

        /// <summary>Reads the existing assets back into a document. Used once, to seed LevelGraph.txt
        /// from whatever is already wired, so adopting the tool does not start from a blank page.</summary>
        public static LevelGraphDocument ImportFromAssets()
        {
            var doc = new LevelGraphDocument();

            LevelFlow flow = AssetDatabase.LoadAssetAtPath<LevelFlow>(LevelGraphFile.FlowAssetPath);
            if (flow == null) return doc;

            doc.MenuScene = flow.mainMenuSceneName;
            doc.EntryRoom = flow.entryLevel != null ? flow.entryLevel.sceneName : "";

            if (flow.levels != null)
            {
                foreach (LevelScene level in flow.levels)
                {
                    if (level == null || string.IsNullOrEmpty(level.sceneName)) continue;
                    if (doc.HasRoom(level.sceneName)) continue;

                    doc.Rooms.Add(new RoomEntry(level.sceneName, Vector2.zero, level.displayName));
                }
            }

            // Collapse A->B plus B->A into one `<->` line, but only when both use the default exit id.
            // A pair with custom ids has to stay as two `->` lines, because `<->` cannot express them.
            var emitted = new HashSet<(string, string)>();

            foreach (LevelScene level in flow.levels ?? new LevelScene[0])
            {
                if (level == null || level.connections == null) continue;

                foreach (LevelConnection c in level.connections)
                {
                    if (c == null || c.target == null) continue;

                    string from = level.sceneName;
                    string to = c.target.sceneName;
                    if (emitted.Contains((from, to))) continue;

                    bool defaultId = c.id == to;
                    bool reverseIsDefault = defaultId && HasDefaultConnectionBack(c.target, from);

                    if (reverseIsDefault)
                    {
                        doc.Links.Add(new LinkEntry(from, to, bidirectional: true));
                        emitted.Add((from, to));
                        emitted.Add((to, from));
                    }
                    else
                    {
                        doc.Links.Add(new LinkEntry(from, to, bidirectional: false, exitId: defaultId ? "" : c.id));
                        emitted.Add((from, to));
                    }
                }
            }

            LayOut(doc);
            return doc;
        }

        private static bool HasDefaultConnectionBack(LevelScene target, string from)
        {
            if (target.connections == null) return false;

            foreach (LevelConnection c in target.connections)
            {
                if (c != null && c.target != null && c.target.sceneName == from && c.id == from) return true;
            }
            return false;
        }

        /// <summary>
        /// Seeds node positions by breadth-first depth from the entry room: distance from the start
        /// becomes the column, so the first thing a designer sees is roughly the shape of the run
        /// rather than 22 nodes stacked on the origin. It is a starting point only — positions are
        /// saved back to the file the moment anything is dragged.
        /// </summary>
        public static void LayOut(LevelGraphDocument doc)
        {
            const float columnWidth = 300f;
            const float rowHeight = 150f;

            var adjacency = new Dictionary<string, List<string>>();
            foreach (DirectedLink link in doc.EnumerateDirected())
            {
                if (!adjacency.TryGetValue(link.From, out List<string> list))
                {
                    list = new List<string>();
                    adjacency[link.From] = list;
                }
                list.Add(link.To);
            }

            var depth = new Dictionary<string, int>();
            var queue = new Queue<string>();

            string start = !string.IsNullOrEmpty(doc.EntryRoom) && doc.HasRoom(doc.EntryRoom)
                ? doc.EntryRoom
                : (doc.Rooms.Count > 0 ? doc.Rooms[0].Name : null);

            if (start != null)
            {
                depth[start] = 0;
                queue.Enqueue(start);
            }

            while (queue.Count > 0)
            {
                string current = queue.Dequeue();
                if (!adjacency.TryGetValue(current, out List<string> neighbours)) continue;

                foreach (string next in neighbours)
                {
                    if (depth.ContainsKey(next)) continue;
                    depth[next] = depth[current] + 1;
                    queue.Enqueue(next);
                }
            }

            // Rooms the walk never reached (which the validator reports as A2) go in a column of their
            // own past the end, so they are visible rather than piled on the origin.
            int maxDepth = 0;
            foreach (int d in depth.Values) maxDepth = Mathf.Max(maxDepth, d);

            var rowCursor = new Dictionary<int, int>();
            foreach (RoomEntry room in doc.Rooms)
            {
                int column = depth.TryGetValue(room.Name, out int d) ? d : maxDepth + 2;

                rowCursor.TryGetValue(column, out int row);
                rowCursor[column] = row + 1;

                room.Position = new Vector2(column * columnWidth, row * rowHeight);
            }
        }

        // ---- helpers ----

        private static bool SetString(SerializedObject so, string property, string value)
        {
            SerializedProperty p = so.FindProperty(property);
            if (p == null) return false;
            if (p.stringValue == value) return false;

            p.stringValue = value;
            return true;
        }

        private static bool SameConnections(LevelConnection[] a, LevelConnection[] b)
        {
            if (a == null) return b == null || b.Length == 0;
            if (a.Length != b.Length) return false;

            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] == null || b[i] == null) return false;
                if (a[i].id != b[i].id || a[i].target != b[i].target) return false;
            }
            return true;
        }

        private static bool SameLevels(LevelScene[] a, List<LevelScene> b)
        {
            if (a == null) return b.Count == 0;
            if (a.Length != b.Count) return false;

            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            string parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
            string leaf = System.IO.Path.GetFileName(path);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf)) return;

            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
