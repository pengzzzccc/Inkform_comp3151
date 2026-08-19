using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace Inkform.LevelGraph.EditorTools
{
    /// <summary>
    /// The blueprint canvas. Draws a LevelGraphDocument as nodes and edges, and writes every
    /// interaction straight back into that document — the window then serialises it to LevelGraph.txt
    /// on Apply. The document is the model at all times; nothing about the topology is stored in the
    /// view.
    ///
    /// GraphView lives in UnityEditor.Experimental.GraphView, so its API can move between Unity
    /// versions. Everything that touches it is confined to this file and LevelRoomNode: the parser,
    /// validators, sync and fixer never reference it, so swapping the canvas out later is a two-file
    /// job rather than a rewrite.
    /// </summary>
    public class LevelGraphView : GraphView
    {
        private LevelGraphDocument doc;
        private readonly Dictionary<string, LevelRoomNode> roomNodes = new Dictionary<string, LevelRoomNode>();

        /// <summary>Raised whenever the graph is edited, so the window can mark itself dirty and
        /// re-run the cheap validation rules.</summary>
        public event Action Changed;

        public LevelGraphDocument Document => doc;

        public LevelGraphView()
        {
            style.flexGrow = 1f;

            SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());

            var grid = new GridBackground();
            Insert(0, grid);
            grid.StretchToParentSize();

            graphViewChanged = OnGraphViewChanged;
        }

        // ---- population ----

        public void Populate(LevelGraphDocument document)
        {
            doc = document;

            graphViewChanged = null;        // rebuilding is not an edit; do not echo it back
            DeleteElements(graphElements.ToList());
            roomNodes.Clear();
            graphViewChanged = OnGraphViewChanged;

            if (doc == null) return;

            foreach (RoomEntry room in doc.Rooms) AddRoomNode(room);

            foreach (LinkEntry link in doc.Links)
            {
                if (!roomNodes.TryGetValue(link.From, out LevelRoomNode from)) continue;
                if (!roomNodes.TryGetValue(link.To, out LevelRoomNode to)) continue;

                Edge edge = from.Output.ConnectTo(to.Input);
                StyleEdge(edge, link);
                AddElement(edge);
            }

            RefreshEntryBadges();
        }

        private LevelRoomNode AddRoomNode(RoomEntry room)
        {
            var node = new LevelRoomNode(room);
            node.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.clickCount == 2) OpenRoomScene(room.Name);
            });

            roomNodes[room.Name] = node;
            AddElement(node);
            return node;
        }

        private static void StyleEdge(Edge edge, LinkEntry link)
        {
            edge.userData = link;

            // One-way doors are drawn warm and thick so they stand out from the symmetric majority —
            // an accidental one-way is the failure this format is meant to make visible.
            if (link.Bidirectional) return;

            edge.edgeControl.inputColor = new Color(0.95f, 0.65f, 0.25f);
            edge.edgeControl.outputColor = new Color(0.95f, 0.65f, 0.25f);
            edge.edgeControl.edgeWidth = 3;
        }

        // ---- status ----

        /// <summary>Colours each node by the worst finding attached to it.</summary>
        public void ApplyFindings(IEnumerable<Finding> findings)
        {
            var worst = new Dictionary<string, Severity>();

            foreach (Finding f in findings)
            {
                if (string.IsNullOrEmpty(f.Room)) continue;

                if (!worst.TryGetValue(f.Room, out Severity current) || f.Severity > current)
                    worst[f.Room] = f.Severity;
            }

            foreach (KeyValuePair<string, LevelRoomNode> pair in roomNodes)
            {
                pair.Value.SetStatus(worst.TryGetValue(pair.Key, out Severity s) ? s : (Severity?)null);
            }
        }

        public void FrameRoom(string roomName)
        {
            if (!roomNodes.TryGetValue(roomName, out LevelRoomNode node)) return;

            ClearSelection();
            AddToSelection(node);
            FrameSelection();
        }

        private void RefreshEntryBadges()
        {
            foreach (KeyValuePair<string, LevelRoomNode> pair in roomNodes)
                pair.Value.SetIsEntry(doc != null && pair.Key == doc.EntryRoom);
        }

        /// <summary>Pulls every node's current rect into the document. Called before serialising, so a
        /// drag that never triggered a change event still ends up in the file.</summary>
        public void WriteBackPositions()
        {
            foreach (LevelRoomNode node in roomNodes.Values) node.WriteBackPosition();
        }

        // ---- editing ----

        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
        {
            var compatible = new List<Port>();

            foreach (Port port in ports.ToList())
            {
                if (port == startPort) continue;
                if (port.direction == startPort.direction) continue;
                if (port.node == startPort.node) continue;      // no self-links: a door back into the same room
                compatible.Add(port);
            }

            return compatible;
        }

        private GraphViewChange OnGraphViewChanged(GraphViewChange change)
        {
            if (doc == null) return change;

            bool dirty = false;

            if (change.movedElements != null)
            {
                foreach (GraphElement element in change.movedElements)
                {
                    if (element is LevelRoomNode node) { node.WriteBackPosition(); dirty = true; }
                }
            }

            if (change.edgesToCreate != null)
            {
                foreach (Edge edge in change.edgesToCreate)
                {
                    var from = edge.output?.node as LevelRoomNode;
                    var to = edge.input?.node as LevelRoomNode;
                    if (from == null || to == null) continue;

                    // New connections default to two-way: the overwhelming majority of doors are, and
                    // a designer who wants one-way can say so from the edge's context menu.
                    var link = new LinkEntry(from.Room.Name, to.Room.Name, bidirectional: true);
                    doc.Links.Add(link);
                    StyleEdge(edge, link);
                    dirty = true;
                }
            }

            if (change.elementsToRemove != null)
            {
                foreach (GraphElement element in change.elementsToRemove)
                {
                    if (element is Edge edge && edge.userData is LinkEntry link)
                    {
                        doc.Links.Remove(link);
                        dirty = true;
                    }
                    else if (element is LevelRoomNode node)
                    {
                        RemoveRoom(node);
                        dirty = true;
                    }
                }
            }

            if (dirty)
            {
                RefreshEntryBadges();
                Changed?.Invoke();
            }

            return change;
        }

        // Removing a room drops its links too, or the document would keep edges pointing at a room
        // that no longer exists (which the validator would then report as A4 forever).
        private void RemoveRoom(LevelRoomNode node)
        {
            string name = node.Room.Name;

            doc.Rooms.Remove(node.Room);
            doc.Links.RemoveAll(l => l.From == name || l.To == name);
            roomNodes.Remove(name);

            if (doc.EntryRoom == name) doc.EntryRoom = "";
        }

        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            Vector2 mouse = contentViewContainer.WorldToLocal(evt.mousePosition);

            evt.menu.AppendAction("Create Room", _ => CreateRoom(mouse));
            evt.menu.AppendSeparator();

            var selectedNode = selection.OfType<LevelRoomNode>().FirstOrDefault();
            if (selectedNode != null)
            {
                evt.menu.AppendAction("Set As Entry", _ =>
                {
                    doc.EntryRoom = selectedNode.Room.Name;
                    RefreshEntryBadges();
                    Changed?.Invoke();
                });

                evt.menu.AppendAction("Rename Display Name…", _ => PromptDisplayName(selectedNode));
                evt.menu.AppendAction("Open Scene", _ => OpenRoomScene(selectedNode.Room.Name));
            }

            var selectedEdge = selection.OfType<Edge>().FirstOrDefault();
            if (selectedEdge?.userData is LinkEntry link)
            {
                string label = link.Bidirectional ? "Make One-Way (->)" : "Make Two-Way (<->)";
                evt.menu.AppendAction(label, _ =>
                {
                    link.Bidirectional = !link.Bidirectional;
                    if (link.Bidirectional) link.ExitId = "";   // a custom id cannot survive on <->
                    Changed?.Invoke();
                    Populate(doc);                              // restyle the edge
                });
            }

            base.BuildContextualMenu(evt);
        }

        private void CreateRoom(Vector2 position)
        {
            string name = "NewRoom";
            int suffix = 1;
            while (doc.HasRoom(name)) name = "NewRoom" + suffix++;

            var room = new RoomEntry(name, position);
            doc.Rooms.Add(room);

            LevelRoomNode node = AddRoomNode(room);
            ClearSelection();
            AddToSelection(node);

            Changed?.Invoke();
        }

        private void PromptDisplayName(LevelRoomNode node)
        {
            // No modal text-entry primitive exists in the editor API, so reuse the object-field-free
            // route: a tiny popup window would be more polish than this earns. The graph file is
            // hand-editable, and so is the node's own subtitle via the Inspector-free path below.
            string current = node.Room.DisplayName;
            string next = EditorInputDialog.Show("Display Name", $"Shown to players for '{node.Room.Name}'", current);
            if (next == null || next == current) return;

            node.SetDisplayName(next);
            Changed?.Invoke();
        }

        private static void OpenRoomScene(string roomName)
        {
            string path = LevelGraphFile.ScenePathFor(roomName);
            if (path == null)
            {
                Debug.LogWarning($"Level graph: no scene file named '{roomName}.unity' in the project.");
                return;
            }

            if (UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene(path);
        }
    }
}
