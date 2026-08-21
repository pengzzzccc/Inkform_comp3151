using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Inkform.LevelGraph.EditorTools
{
    /// <summary>
    /// The left-hand scene shelf: every scene under Assets/Scenes as a draggable row, so a
    /// hand-authored level enters the graph by being picked up and dropped onto the canvas — no
    /// naming ceremony, no "Create Room" step to forget.
    ///
    /// The row shows which directory the scene lives in (Generated vs Level1 vs Test) and whether the
    /// graph already knows it. Double-clicking opens the scene in the editor; dragging performs the
    /// adoption (see LevelGraphView.AdoptScene).
    /// </summary>
    public class LevelSceneTreePanel : VisualElement
    {
        public const float PanelWidth = 240f;

        private readonly ListView list;
        private readonly List<SceneEntry> scenes = new List<SceneEntry>();

        private SceneEntry? pendingDrag;        // row the mouse went down on, becomes a drag on movement
        private Vector2 dragStart;              // position the mouse went down, to distinguish drag from click

        private struct SceneEntry
        {
            public string Name;      // scene file name without extension — the room name if adopted
            public string Path;      // asset path, for opening and for relative-dir display
            public bool Adopted;     // whether the graph already has a room of this name

            public string DirectoryLabel => ParentFolder(Path);
        }

        public LevelSceneTreePanel(LevelGraphView graph)
        {
            style.width = PanelWidth;
            style.minWidth = PanelWidth;
            style.flexShrink = 0f;
            style.borderRightWidth = 1f;
            style.borderRightColor = new Color(0f, 0f, 0f, 0.4f);

            var title = new Label("Scenes — drag into the graph");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.fontSize = 11;
            title.style.marginLeft = 6f;
            title.style.marginTop = 4f;
            title.style.marginBottom = 2f;
            Add(title);

            list = new ListView
            {
                fixedItemHeight = 24f,
                selectionType = SelectionType.Single,
                makeItem = MakeRow,
                bindItem = BindRow,
            };
            list.style.flexGrow = 1f;
            list.itemsSource = scenes;
            Add(list);

            // Registering the drag callbacks on the panel only would make the whitespace below the
            // list a dead zone; the graph itself also accepts drops (LevelGraphView), so the whole
            // left column is a valid pickup point and the whole canvas a valid landing zone.
            RegisterCallback<DragUpdatedEvent>(OnDragUpdated);
            RegisterCallback<DragPerformEvent>(OnDragPerform);
        }

        // ---- drag initiation ----

        // Dragging starts from a row: mouse down records the row, movement past a small threshold
        // converts it into a Unity editor drag carrying the SceneAsset. The graph accepts that drag
        // on drop (LevelGraphView.OnDragPerform) and adopts the scene. currentTarget rather than
        // target: the mouse can land on the row's labels, and the row is what carries the userData.
        private void OnRowMouseDown(MouseDownEvent evt)
        {
            if (evt.button != 0 || evt.currentTarget is not VisualElement row || row.userData is not SceneEntry scene) return;
            pendingDrag = scene;
            dragStart = evt.mousePosition;
        }

        private void OnRowMouseMove(MouseMoveEvent evt)
        {
            if (!pendingDrag.HasValue) return;
            if ((evt.mousePosition - dragStart).sqrMagnitude < 16f) return;   // click, not yet a drag

            SceneEntry scene = pendingDrag.Value;
            pendingDrag = null;

            SceneAsset asset = AssetDatabase.LoadAssetAtPath<SceneAsset>(scene.Path);
            if (asset == null) return;

            DragAndDrop.PrepareStartDrag();
            DragAndDrop.objectReferences = new Object[] { asset };
            DragAndDrop.StartDrag($"Adopt {scene.Name}");
        }

        private void OnRowMouseUp(MouseUpEvent evt)
        {
            pendingDrag = null;
        }

        /// <summary>Re-scans Assets/Scenes and re-paints adoption status. Called on Reload and after
        /// Apply, so a freshly adopted scene stops looking available.</summary>
        public void Refresh(LevelGraphDocument doc)
        {
            scenes.Clear();

            foreach (string guid in AssetDatabase.FindAssets("t:Scene"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.StartsWith("Assets/Scenes/", System.StringComparison.Ordinal)) continue;

                string name = Path.GetFileNameWithoutExtension(path);
                if (name == "MainMenu") continue;      // the menu is not a room

                scenes.Add(new SceneEntry
                {
                    Name = name,
                    Path = path,
                    Adopted = doc != null && doc.HasRoom(name),
                });
            }

            scenes.Sort((a, b) => string.Compare(a.Path, b.Path, System.StringComparison.Ordinal));
            list.itemsSource = scenes;
            list.Rebuild();
        }

        // ---- rows ----

        // Instance method, not static: the rows it makes register the instance drag handlers below,
        // which need pendingDrag/dragStart.
        private VisualElement MakeRow()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.paddingLeft = 6f;
            row.style.paddingRight = 6f;

            var dot = new Label { name = "dot" };
            dot.style.width = 12f;
            dot.style.flexShrink = 0f;
            row.Add(dot);

            var name = new Label { name = "name" };
            name.style.flexGrow = 1f;
            name.style.overflow = Overflow.Hidden;
            row.Add(name);

            var dir = new Label { name = "dir" };
            dir.style.fontSize = 9;
            dir.style.color = new Color(0.55f, 0.55f, 0.55f);
            dir.style.flexShrink = 0f;
            row.Add(dir);

            // Drag initiation lives on the row: the mouse has to go down on a concrete scene before
            // movement can mean anything. Double-click opens the scene in the editor instead.
            row.RegisterCallback<MouseDownEvent>(OnRowMouseDown);
            row.RegisterCallback<MouseMoveEvent>(OnRowMouseMove);
            row.RegisterCallback<MouseUpEvent>(OnRowMouseUp);
            row.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.clickCount == 2 && row.userData is SceneEntry scene)
                    OpenScene(scene.Path);
            });

            return row;
        }

        private void BindRow(VisualElement element, int index)
        {
            if (index < 0 || index >= scenes.Count) return;
            SceneEntry scene = scenes[index];

            element.Q<Label>("dot").text = scene.Adopted ? "●" : "○";
            element.Q<Label>("dot").style.color = scene.Adopted
                ? new Color(0.45f, 0.85f, 0.50f)
                : new Color(0.45f, 0.45f, 0.45f);
            element.Q<Label>("name").text = scene.Name;
            element.Q<Label>("dir").text = scene.DirectoryLabel;

            element.userData = scene;
        }

        // ---- drag & drop ----

        private void OnDragUpdated(DragUpdatedEvent evt)
        {
            if (HasDraggedScene()) DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
        }

        private void OnDragPerform(DragPerformEvent evt)
        {
            if (!HasDraggedScene()) return;
            DragAndDrop.AcceptDrag();
        }

        private static bool HasDraggedScene()
        {
            foreach (Object reference in DragAndDrop.objectReferences)
            {
                if (reference is SceneAsset) return true;
            }
            return false;
        }

        private static string ParentFolder(string path)
        {
            string dir = Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "";
            const string prefix = "Assets/Scenes/";
            return dir.StartsWith(prefix, System.StringComparison.Ordinal) ? dir.Substring(prefix.Length) : dir;
        }

        private static void OpenScene(string path)
        {
            if (UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene(path);
        }
    }
}
