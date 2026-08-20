using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Inkform.LevelGraph.EditorTools
{
    /// <summary>
    /// The right-hand inspector: edits the room behind the selected node. A node on the canvas is a
    /// view of a RoomEntry; this panel is the second view, and Apply is the point where an edit made
    /// here lands in the document (the same document the canvas writes to) and the node re-paints.
    ///
    /// Door count is the one gameplay-relevant knob: how many outgoing doors the room may have, which
    /// the canvas enforces as the connection cap. Everything else (name, scene path) is context.
    /// </summary>
    public class LevelNodeInspectorPanel : VisualElement
    {
        public const float PanelWidth = 320f;

        private readonly LevelGraphView graph;

        private readonly Label placeholder;
        private readonly VisualElement fields;
        private readonly Label nameField;
        private readonly Label scenePathField;
        private readonly TextField displayNameField;
        private readonly IntegerField doorCountField;
        private readonly Label occupancyLabel;
        private readonly Button setEntryButton;
        private readonly Button applyButton;

        private LevelRoomNode bound;    // node the fields currently edit; null = placeholder shown

        public LevelNodeInspectorPanel(LevelGraphView graph)
        {
            this.graph = graph;
            style.width = PanelWidth;
            style.minWidth = PanelWidth;
            style.flexShrink = 0f;
            style.borderLeftWidth = 1f;
            style.borderLeftColor = new Color(0f, 0f, 0f, 0.4f);
            style.paddingLeft = 8f;
            style.paddingRight = 8f;

            var title = new Label("Inspector");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.fontSize = 11;
            title.style.marginTop = 4f;
            title.style.marginBottom = 4f;
            Add(title);

            placeholder = new Label("Select a node on the graph to configure its level.");
            placeholder.style.fontSize = 11;
            placeholder.style.color = new Color(0.6f, 0.6f, 0.6f);
            placeholder.style.whiteSpace = WhiteSpace.Normal;
            Add(placeholder);

            fields = new VisualElement();
            fields.style.display = DisplayStyle.None;
            Add(fields);

            // ---- fields ----

            LabeledField("Room", nameField = new Label());
            LabeledField("Scene", scenePathField = new Label());
            LabeledField("Display name", displayNameField = new TextField { value = "" });
            LabeledField("Door count", doorCountField = new IntegerField { value = 4 });
            doorCountField.isDelayed = true;
            doorCountField.RegisterValueChangedCallback(_ => RefreshOccupancy());
            doorCountField.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return) Apply();
            });

            occupancyLabel = new Label();
            occupancyLabel.style.fontSize = 10;
            occupancyLabel.style.marginLeft = 8f;
            occupancyLabel.style.marginBottom = 4f;
            fields.Add(occupancyLabel);

            setEntryButton = new Button(SetAsEntry) { text = "Set As Entry" };
            fields.Add(setEntryButton);

            applyButton = new Button(Apply) { text = "Apply" };
            applyButton.style.marginTop = 4f;
            fields.Add(applyButton);

            // A new scene appearing in the tree can only mean the doc changed; re-check the bound node.
            graph.SelectionChanged += Refresh;
            graph.Changed += RefreshOccupancy;
        }

        private void LabeledField(string labelText, VisualElement field)
        {
            var label = new Label(labelText);
            label.style.fontSize = 10;
            label.style.color = new Color(0.55f, 0.55f, 0.55f);
            label.style.marginTop = 6f;
            label.style.marginBottom = 1f;
            fields.Add(label);

            field.style.fontSize = 11;
            fields.Add(field);
        }

        // ---- binding ----

        private void Refresh()
        {
            LevelRoomNode node = graph.SelectedRoomNode;
            if (bound == node) return;      // same node selected again: nothing to re-bind

            bound = node;

            if (node == null)
            {
                placeholder.style.display = DisplayStyle.Flex;
                fields.style.display = DisplayStyle.None;
                return;
            }

            placeholder.style.display = DisplayStyle.None;
            fields.style.display = DisplayStyle.Flex;

            nameField.text = node.Room.Name;
            string path = LevelGraphFile.ScenePathFor(node.Room.Name);
            scenePathField.text = path ?? "(no scene file — create or adopt it before Apply)";
            scenePathField.style.color = path != null
                ? new Color(0.7f, 0.7f, 0.7f)
                : new Color(0.85f, 0.6f, 0.35f);

            displayNameField.SetValueWithoutNotify(node.Room.DisplayName);
            doorCountField.SetValueWithoutNotify(node.Room.DoorCount);
            RefreshOccupancy();
        }

        private void RefreshOccupancy()
        {
            if (bound == null) return;

            int linkCount = 0;
            if (graph.Document != null)
            {
                foreach (DirectedLink link in graph.Document.EnumerateDirected())
                {
                    if (link.From == bound.Room.Name) linkCount++;
                }
            }

            int cap = Mathf.Clamp(doorCountField.value, 1, 4);
            occupancyLabel.text = $"{linkCount}/{cap} doors connected";
            occupancyLabel.style.color = linkCount > cap
                ? new Color(0.95f, 0.45f, 0.45f)
                : new Color(0.65f, 0.65f, 0.65f);
        }

        private void SetAsEntry()
        {
            if (bound == null || graph.Document == null) return;

            graph.Document.EntryRoom = bound.Room.Name;
            graph.NotifyEdited();
        }

        private void Apply()
        {
            if (bound == null) return;

            RoomEntry room = bound.Room;
            room.DisplayName = displayNameField.value;
            room.DoorCount = Mathf.Clamp(doorCountField.value, 1, 4);

            bound.SetDisplayName(room.DisplayName);
            graph.NotifyEdited();
        }
    }
}
