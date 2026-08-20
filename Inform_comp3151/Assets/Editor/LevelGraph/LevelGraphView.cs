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

        /// <summary>Raised when the selection changes, so the inspector can re-bind to the selected node.</summary>
        public event Action SelectionChanged;

        public LevelGraphDocument Document => doc;

        public LevelRoomNode SelectedRoomNode => selection.OfType<LevelRoomNode>().FirstOrDefault();

        public override void AddToSelection(ISelectable selectable)
        {
            base.AddToSelection(selectable);
            SelectionChanged?.Invoke();
        }

        public override void ClearSelection()
        {
            base.ClearSelection();
            SelectionChanged?.Invoke();
        }

        public override void RemoveFromSelection(ISelectable selectable)
        {
            base.RemoveFromSelection(selectable);
            SelectionChanged?.Invoke();
        }

        /// <summary>
        /// Tells the view that the document changed outside the canvas (the inspector edited a room)
        /// and re-paints whatever depends on it. Same effect as a canvas edit: badge refresh + dirty
        /// signal, without pretending a graph element moved.
        /// </summary>
        public void NotifyEdited()
        {
            RefreshEntryBadges();
            RefreshDoorBadges();
            Changed?.Invoke();
        }

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

            // Wired explicitly rather than left to whatever GraphView does by default, because the help
            // panel tells the designer the Delete key works. Routing it through DeleteSelection keeps it
            // going via graphViewChanged, so removals land in the document like every other edit.
            deleteSelection = (operationName, askUser) => DeleteSelection();

            // The scene tree on the left starts a drag onto this canvas: accepting it here means the
            // whole window is a drop target, not just the tree's own rows.
            RegisterCallback<DragUpdatedEvent>(OnDragUpdated);
            RegisterCallback<DragPerformEvent>(OnDragPerform);
        }

        // ---- dropping scenes from the scene tree ----

        private void OnDragUpdated(DragUpdatedEvent evt)
        {
            if (HasDraggedScene()) DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
        }

        private void OnDragPerform(DragPerformEvent evt)
        {
            if (!HasDraggedScene()) return;

            DragAndDrop.AcceptDrag();
            Vector2 local = contentViewContainer.WorldToLocal(evt.mousePosition);

            // UnityEngine.Object spelled in full: this file also has using System (the Changed event's
            // Action), and a bare Object would be ambiguous between the two.
            foreach (UnityEngine.Object reference in DragAndDrop.objectReferences)
            {
                if (reference is not SceneAsset scene) continue;
                AdoptScene(scene.name, local);
            }
        }

        private static bool HasDraggedScene()
        {
            foreach (UnityEngine.Object reference in DragAndDrop.objectReferences)
            {
                if (reference is SceneAsset) return true;
            }
            return false;
        }

        /// <summary>
        /// Adopts a hand-authored scene into the graph: existing rooms are selected instead of
        /// duplicated, new ones are created at the drop position. Called by the scene-tree drag and by
        /// the tree's double-click. Returns the node (always non-null: creation or lookup).
        /// </summary>
        public LevelRoomNode AdoptScene(string sceneName, Vector2 position)
        {
            LevelRoomNode node = SelectExisting(sceneName);
            if (node != null) return node;

            // Scene files that were never adopted live outside the graph; the room name is the scene
            // file name, which is also the default exit id — the "one string, four places" convention.
            string name = sceneName;
            int suffix = 1;
            while (doc.HasRoom(name)) name = $"{sceneName}_{suffix++}";

            var room = new RoomEntry(name, position);
            doc.Rooms.Add(room);

            node = AddRoomNode(room);
            ClearSelection();
            AddToSelection(node);
            RefreshDoorBadges();
            Changed?.Invoke();
            return node;
        }

        private LevelRoomNode SelectExisting(string sceneName)
        {
            if (!roomNodes.TryGetValue(sceneName, out LevelRoomNode node)) return null;

            ClearSelection();
            AddToSelection(node);
            FrameSelection();
            return node;
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
            RefreshDoorBadges();
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

        /// <summary>Re-paints every node's door occupancy (used / cap) from the current document.</summary>
        public void RefreshDoorBadges()
        {
            if (doc == null) return;

            var outgoing = new Dictionary<string, int>();
            foreach (DirectedLink link in doc.EnumerateDirected())
            {
                outgoing.TryGetValue(link.From, out int count);
                outgoing[link.From] = count + 1;
            }

            foreach (KeyValuePair<string, LevelRoomNode> pair in roomNodes)
            {
                LevelRoomNode node = pair.Value;
                node.SetDoorInfo(node.Room.DoorCount, outgoing.TryGetValue(pair.Key, out int c) ? c : 0);
            }
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

                // Door-count cap: the target room's output slots are the limit. A bidirectional link
                // consumes one slot on each end, so the target must still have a free outgoing slot
                // (its door badge shows the occupancy). Full rooms cannot be dragged onto.
                if (port.direction == Direction.Input && port.node is LevelRoomNode target
                    && LinkCountOf(target.Room.Name) >= target.Room.DoorCount) continue;

                compatible.Add(port);
            }

            return compatible;
        }

        /// <summary>Number of directed links leaving the room — a bidirectional link counts against
        /// both ends, since each direction is one door.</summary>
        private int LinkCountOf(string roomName)
        {
            if (doc == null) return 0;

            int count = 0;
            foreach (DirectedLink link in doc.EnumerateDirected())
            {
                if (link.From == roomName) count++;
            }
            return count;
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
                var rejected = new List<Edge>();

                foreach (Edge edge in change.edgesToCreate)
                {
                    var from = edge.output?.node as LevelRoomNode;
                    var to = edge.input?.node as LevelRoomNode;
                    if (from == null || to == null) continue;

                    // The port filter already hides full target rooms; the start end can still be over
                    // cap (dragging a 5th edge out of a 4-door room), so re-check both ends here. A
                    // bidirectional link consumes one slot on each side.
                    if (LinkCountOf(from.Room.Name) >= from.Room.DoorCount
                        || LinkCountOf(to.Room.Name) >= to.Room.DoorCount)
                    {
                        rejected.Add(edge);
                        Debug.LogWarning($"Level graph: '{from.Room.Name}' or '{to.Room.Name}' has no free door slot (cap {from.Room.DoorCount}/{to.Room.DoorCount}).");
                        continue;
                    }

                    // New connections default to two-way: the overwhelming majority of doors are, and
                    // a designer who wants one-way can say so from the edge's context menu.
                    var link = new LinkEntry(from.Room.Name, to.Room.Name, bidirectional: true);
                    doc.Links.Add(link);
                    StyleEdge(edge, link);
                    dirty = true;
                }

                // Dropping the edge from the change list is what tells GraphView it was never created;
                // leaving it parented would show a phantom connection the document does not know about.
                foreach (Edge edge in rejected)
                {
                    change.edgesToCreate.Remove(edge);
                    if (edge.parent != null) edge.parent.Remove(edge);
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
                RefreshDoorBadges();
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
            RefreshDoorBadges();

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
