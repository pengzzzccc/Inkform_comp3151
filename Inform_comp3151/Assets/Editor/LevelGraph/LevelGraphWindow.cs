using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Inkform.LevelGraph.EditorTools
{
    /// <summary>
    /// The level graph window: a blueprint canvas on top, a findings list underneath.
    ///
    /// The split down the middle is between cheap and expensive validation. Graph rules and asset
    /// rules re-run on every edit — they are milliseconds and they keep the node colours honest.
    /// Scene rules open all 22 room scenes, so they run only when asked, and their results are kept
    /// until the next run rather than thrown away on the next keystroke.
    ///
    /// Menu: Tools &gt; Inkform &gt; Level Graph.
    /// </summary>
    public class LevelGraphWindow : EditorWindow
    {
        private LevelGraphView graph;
        private ListView findingsList;
        private Label summary;

        private LevelGraphDocument doc;
        private readonly List<Finding> findings = new List<Finding>();
        private List<Finding> sceneFindings = new List<Finding>();

        private bool dirty;

        [MenuItem("Tools/Inkform/Level Graph")]
        public static void Open()
        {
            var window = GetWindow<LevelGraphWindow>();
            window.titleContent = new GUIContent("Level Graph");
            window.minSize = new Vector2(720f, 420f);
            window.Show();
        }

        void CreateGUI()
        {
            rootVisualElement.Add(BuildToolbar());

            graph = new LevelGraphView();
            graph.Changed += OnGraphChanged;
            rootVisualElement.Add(graph);

            rootVisualElement.Add(BuildFindingsPanel());

            Reload();
        }

        void OnDisable()
        {
            if (graph != null) graph.Changed -= OnGraphChanged;
        }

        // ---- chrome ----

        private VisualElement BuildToolbar()
        {
            var bar = new Toolbar();

            bar.Add(new ToolbarButton(Reload) { text = "Reload" });
            bar.Add(new ToolbarButton(Apply) { text = "Apply" });

            var spacer = new ToolbarSpacer { flex = true };
            bar.Add(spacer);

            bar.Add(new ToolbarButton(DeepValidate) { text = "Deep Validate (opens scenes)" });
            bar.Add(new ToolbarButton(FixAll) { text = "Fix All" });
            bar.Add(new ToolbarButton(SeedFromAssets) { text = "Import From Assets" });

            return bar;
        }

        private VisualElement BuildFindingsPanel()
        {
            var panel = new VisualElement();
            panel.style.height = 190f;
            panel.style.borderTopWidth = 1f;
            panel.style.borderTopColor = new Color(0f, 0f, 0f, 0.4f);

            summary = new Label("—");
            summary.style.unityFontStyleAndWeight = FontStyle.Bold;
            summary.style.marginLeft = 6f;
            summary.style.marginTop = 4f;
            summary.style.marginBottom = 2f;
            panel.Add(summary);

            findingsList = new ListView
            {
                fixedItemHeight = 22f,
                selectionType = SelectionType.Single,
                itemsSource = findings,
                makeItem = MakeFindingRow,
                bindItem = BindFindingRow,
            };
            findingsList.style.flexGrow = 1f;

            // Clicking a finding frames the room it is about — the fastest way from "something is
            // wrong" to "this node".
            findingsList.selectionChanged += selected =>
            {
                foreach (object o in selected)
                {
                    if (o is Finding f && !string.IsNullOrEmpty(f.Room)) graph.FrameRoom(f.Room);
                    break;
                }
            };

            panel.Add(findingsList);
            return panel;
        }

        private static VisualElement MakeFindingRow()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;

            var code = new Label { name = "code" };
            code.style.width = 42f;
            code.style.unityFontStyleAndWeight = FontStyle.Bold;
            code.style.marginLeft = 6f;
            row.Add(code);

            var message = new Label { name = "message" };
            message.style.flexGrow = 1f;
            message.style.overflow = Overflow.Hidden;
            row.Add(message);

            var fix = new Button { name = "fix", text = "Fix" };
            fix.style.width = 60f;
            row.Add(fix);

            return row;
        }

        private void BindFindingRow(VisualElement element, int index)
        {
            if (index < 0 || index >= findings.Count) return;
            Finding f = findings[index];

            var code = element.Q<Label>("code");
            code.text = f.Code;
            code.style.color = ColorFor(f.Severity);

            element.Q<Label>("message").text = f.Message;

            var fix = element.Q<Button>("fix");
            fix.style.display = f.CanFix ? DisplayStyle.Flex : DisplayStyle.None;
            fix.clickable = new Clickable(() =>
            {
                if (LevelGraphFixer.FixOne(f, doc)) Revalidate(keepSceneFindings: false);
            });
        }

        private static Color ColorFor(Severity severity)
        {
            switch (severity)
            {
                case Severity.Error: return new Color(0.95f, 0.45f, 0.45f);
                case Severity.Warning: return new Color(0.95f, 0.78f, 0.35f);
                default: return new Color(0.55f, 0.75f, 0.95f);
            }
        }

        // ---- actions ----

        private void Reload()
        {
            doc = LevelGraphFile.Load(out List<LevelGraphParser.ParseError> parseErrors);
            dirty = false;
            sceneFindings.Clear();

            graph.Populate(doc);
            Revalidate(keepSceneFindings: false, parseErrors);

            if (doc.Rooms.Count == 0 && !LevelGraphFile.Exists)
            {
                summary.text = $"{LevelGraphFile.Path} does not exist yet — press \"Import From Assets\" to seed it from the current LevelFlow.";
            }
        }

        /// <summary>Writes the graph to the text file (the source of truth) and regenerates the assets
        /// from it.</summary>
        private void Apply()
        {
            if (doc == null) return;

            graph.WriteBackPositions();
            LevelGraphFile.Save(doc);

            int touched = LevelGraphSync.Apply(doc);
            dirty = false;

            Revalidate(keepSceneFindings: false);
            Debug.Log($"Level graph applied: {doc.Rooms.Count} rooms, {doc.Links.Count} links, {touched} asset(s) written.");
        }

        private void DeepValidate()
        {
            if (doc == null) return;

            if (dirty && !EditorUtility.DisplayDialog(
                    "Unsaved graph changes",
                    "The graph has edits that have not been applied. Deep validation checks the scenes against the *applied* assets, so it will not see them.\n\nValidate anyway?",
                    "Validate", "Cancel"))
                return;

            sceneFindings = LevelGraphProjectValidator.ValidateScenes(doc);
            Revalidate(keepSceneFindings: true);
        }

        private void FixAll()
        {
            if (doc == null) return;

            var fixable = new List<Finding>();
            foreach (Finding f in findings)
            {
                if (f.CanFix) fixable.Add(f);
            }

            if (fixable.Count == 0)
            {
                Debug.Log("Level graph: nothing to fix.");
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "Fix all",
                    $"Create {fixable.Count} missing item(s)?\n\n"
                    + "Everything is additive — doors and checkpoints are created at each scene's origin for you to "
                    + "position. Nothing is deleted and no scene is regenerated.",
                    "Fix", "Cancel"))
                return;

            int repaired = LevelGraphFixer.FixAll(fixable, doc);
            sceneFindings.Clear();
            Revalidate(keepSceneFindings: false);

            Debug.Log($"Level graph: fixed {repaired} finding(s). Created objects sit at each scene's origin — drag them into place.");
        }

        /// <summary>One-time adoption: read whatever the LevelFlow assets already say and write it out
        /// as the text file, so switching to this tool does not start from an empty graph.</summary>
        private void SeedFromAssets()
        {
            if (LevelGraphFile.Exists && !EditorUtility.DisplayDialog(
                    "Import from assets",
                    $"{LevelGraphFile.Path} already exists. Rebuild it from the current LevelFlow assets, discarding the file (including node positions)?",
                    "Rebuild", "Cancel"))
                return;

            doc = LevelGraphSync.ImportFromAssets();
            LevelGraphFile.Save(doc);

            graph.Populate(doc);
            dirty = false;
            sceneFindings.Clear();
            Revalidate(keepSceneFindings: false);

            Debug.Log($"Level graph seeded from assets: {doc.Rooms.Count} rooms, {doc.Links.Count} links → {LevelGraphFile.Path}");
        }

        private void OnGraphChanged()
        {
            dirty = true;

            // Scene findings describe the project on disk, which an unapplied edit has not changed —
            // keeping them would show doors as missing that are only missing in the edit.
            sceneFindings.Clear();
            Revalidate(keepSceneFindings: false);
        }

        // ---- validation plumbing ----

        private void Revalidate(bool keepSceneFindings, List<LevelGraphParser.ParseError> parseErrors = null)
        {
            findings.Clear();

            if (parseErrors != null)
            {
                foreach (LevelGraphParser.ParseError e in parseErrors)
                {
                    findings.Add(new Finding
                    {
                        Severity = Severity.Error,
                        Code = "P",
                        Message = $"{LevelGraphFile.Path} {e}",
                    });
                }
            }

            if (doc != null)
            {
                findings.AddRange(LevelGraphValidator.Validate(doc));
                findings.AddRange(LevelGraphProjectValidator.ValidateProject(doc));
            }

            if (keepSceneFindings) findings.AddRange(sceneFindings);

            // Worst first: a designer scanning the list should hit the errors before the notes.
            findings.Sort((a, b) => b.Severity.CompareTo(a.Severity));

            int errors = 0, warnings = 0;
            foreach (Finding f in findings)
            {
                if (f.Severity == Severity.Error) errors++;
                else if (f.Severity == Severity.Warning) warnings++;
            }

            string scenePart = keepSceneFindings ? "scenes checked" : "scenes not checked";
            summary.text = doc == null
                ? "—"
                : $"{doc.Rooms.Count} rooms · {doc.Links.Count} links · {errors} error(s) · {warnings} warning(s) · {scenePart}"
                  + (dirty ? "   [unapplied changes]" : "");

            graph.ApplyFindings(findings);

            findingsList.itemsSource = findings;
            findingsList.Rebuild();
        }
    }
}
