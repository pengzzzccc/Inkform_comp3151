using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace Inkform.LevelGraph.EditorTools
{
    /// <summary>
    /// One room in the blueprint. Deliberately thin: it draws a RoomEntry and owns nothing — the
    /// document stays the model, and the node is a view onto it.
    ///
    /// Ports are one in, one out, both Multi. Giving each exit its own port was the alternative, but
    /// exit ids default to the target room's name, so an edge already says which exit it is; per-exit
    /// ports would add a row of near-identical stubs and a "+" button to maintain, for no information.
    ///
    /// The status stripe is the point of the whole window: validation findings are also listed as text
    /// below the graph, but a designer scanning 22 rooms should be able to see which one is broken
    /// without reading anything.
    /// </summary>
    public class LevelRoomNode : Node
    {
        public RoomEntry Room { get; }

        public Port Input { get; }
        public Port Output { get; }

        private readonly VisualElement statusStripe;
        private readonly Label subtitle;
        private readonly Label entryBadge;
        private readonly Label doorBadge;

        private static readonly Color OkColor = new Color(0.30f, 0.55f, 0.32f);
        private static readonly Color InfoColor = new Color(0.28f, 0.44f, 0.58f);
        private static readonly Color WarnColor = new Color(0.68f, 0.52f, 0.16f);
        private static readonly Color ErrorColor = new Color(0.64f, 0.24f, 0.24f);

        public LevelRoomNode(RoomEntry room)
        {
            Room = room;
            title = room.Name;

            // A thin coloured bar under the title rather than tinting the whole node: at a zoom level
            // where 22 rooms fit on screen the stripe still reads, and the title stays legible.
            statusStripe = new VisualElement();
            statusStripe.style.height = 4f;
            statusStripe.style.marginBottom = 2f;
            titleContainer.Add(statusStripe);

            entryBadge = new Label("ENTRY");
            entryBadge.style.unityFontStyleAndWeight = FontStyle.Bold;
            entryBadge.style.fontSize = 9;
            entryBadge.style.color = new Color(0.45f, 0.85f, 0.50f);
            entryBadge.style.marginLeft = 6f;
            entryBadge.style.display = DisplayStyle.None;
            titleContainer.Add(entryBadge);

            // Door occupancy reads at a glance while the node is small: "3/4" means three of the four
            // door slots this room allows are wired. Red once the cap is hit or exceeded.
            doorBadge = new Label();
            doorBadge.style.unityFontStyleAndWeight = FontStyle.Bold;
            doorBadge.style.fontSize = 9;
            doorBadge.style.marginLeft = 6f;
            titleContainer.Add(doorBadge);

            subtitle = new Label(room.DisplayName);
            subtitle.style.fontSize = 10;
            subtitle.style.color = new Color(0.7f, 0.7f, 0.7f);
            subtitle.style.marginLeft = 6f;
            subtitle.style.marginRight = 6f;
            subtitle.style.marginBottom = 4f;
            subtitle.style.whiteSpace = WhiteSpace.Normal;
            extensionContainer.Add(subtitle);

            Input = InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Multi, typeof(bool));
            Input.portName = "in";
            inputContainer.Add(Input);

            Output = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(bool));
            Output.portName = "out";
            outputContainer.Add(Output);

            RefreshExpandedState();
            RefreshPorts();

            SetPosition(new Rect(room.Position, new Vector2(180f, 90f)));
        }

        /// <summary>Paints the stripe from the worst finding this room has. Info is shown too — a
        /// deliberate one-way door is worth seeing on the graph even though it is not a problem.</summary>
        public void SetStatus(Severity? worst)
        {
            Color color = OkColor;
            if (worst.HasValue)
            {
                switch (worst.Value)
                {
                    case Severity.Error: color = ErrorColor; break;
                    case Severity.Warning: color = WarnColor; break;
                    default: color = InfoColor; break;
                }
            }

            statusStripe.style.backgroundColor = color;
        }

        public void SetIsEntry(bool isEntry)
        {
            entryBadge.style.display = isEntry ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>
        /// Paints the door occupancy badge. `linkCount` is the number of directed edges leaving this
        /// room (a bidirectional link counts against both ends — each direction is one door slot).
        /// </summary>
        public void SetDoorInfo(int doorCount, int linkCount)
        {
            doorBadge.text = $"{linkCount}/{doorCount}";
            doorBadge.style.color = linkCount > doorCount
                ? new Color(0.95f, 0.45f, 0.45f)
                : new Color(0.62f, 0.62f, 0.62f);
        }

        public void SetDisplayName(string value)
        {
            Room.DisplayName = value;
            subtitle.text = value;
        }

        /// <summary>Copies the node's current rect back into the document, so a drag survives Apply.</summary>
        public void WriteBackPosition()
        {
            Room.Position = GetPosition().position;
        }
    }
}
