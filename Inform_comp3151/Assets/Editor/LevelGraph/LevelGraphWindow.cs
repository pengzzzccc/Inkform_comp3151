using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Inkform.LevelGraph.EditorTools
{
    /// <summary>
    /// The level graph window: a scene shelf on the left, the blueprint canvas in the middle, an
    /// inspector for the selected room on the right, and a findings list underneath.
    ///
    /// The workflow this layout exists for: drag a hand-authored scene from the shelf onto the canvas
    /// to adopt it as a room, configure it in the inspector (display name, door count), connect it by
    /// dragging links, then press "Apply Level Topology" — the graph file, the scene wiring (doors /
    /// spawn points) and both build lists all follow in one click.
    ///
    /// The split down the middle is between cheap and expensive validation. Graph rules and asset
    /// rules re-run on every edit — they are milliseconds and they keep the node colours honest.
    /// Scene rules open all room scenes, so they run only when asked, and their results are kept
    /// until the next run rather than thrown away on the next keystroke.
    ///
    /// Menu: Tools &gt; Inkform &gt; Level Graph.
    /// </summary>
    public class LevelGraphWindow : EditorWindow
    {
        /// <summary>Defaults to false: the help panel now lives under the inspector and is one toggle
        /// away, so the first-time default does not need to crowd the canvas.</summary>
        private const string ShowHelpPref = "Inkform.LevelGraph.ShowHelp";

        private LevelGraphView graph;
        private LevelSceneTreePanel sceneTree;
        private LevelNodeInspectorPanel inspector;
        private ListView findingsList;
        private Label summary;
        private VisualElement helpPanel;
        private ToolbarToggle helpToggle;
        private DropdownField entryDropdown;

        private LevelGraphDocument doc;
        private readonly List<Finding> findings = new List<Finding>();
        private List<Finding> sceneFindings = new List<Finding>();
        private List<LevelGraphParser.ParseError> parseErrors = new List<LevelGraphParser.ParseError>();

        private bool dirty;

        [MenuItem("Tools/Inkform/Level Graph")]
        public static void Open()
        {
            var window = GetWindow<LevelGraphWindow>();
            window.titleContent = new GUIContent("Level Graph");
            window.minSize = new Vector2(960f, 480f);
            window.Show();
        }

        void CreateGUI()
        {
            rootVisualElement.Add(BuildToolbar());

            // Three columns: scene shelf | canvas | inspector. The shelf is where hand-authored
            // levels are picked up (drag onto the canvas), the inspector is where a selected node's
            // room is configured — the canvas sits between them and stays the largest surface.
            var middle = new VisualElement();
            middle.style.flexDirection = FlexDirection.Row;
            middle.style.flexGrow = 1f;
            middle.style.minHeight = 0f;    // lets the row shrink instead of pushing the findings panel off

            graph = new LevelGraphView();
            graph.Changed += OnGraphChanged;

            sceneTree = new LevelSceneTreePanel(graph);
            middle.Add(sceneTree);

            middle.Add(graph);

            var rightColumn = new VisualElement();
            rightColumn.style.flexShrink = 0f;
            rightColumn.style.minWidth = LevelNodeInspectorPanel.PanelWidth;
            rightColumn.style.width = LevelNodeInspectorPanel.PanelWidth;

            inspector = new LevelNodeInspectorPanel(graph);
            inspector.style.flexGrow = 1f;
            rightColumn.Add(inspector);

            helpPanel = BuildHelpPanel();
            helpPanel.style.width = LevelNodeInspectorPanel.PanelWidth;
            helpPanel.style.flexGrow = 0f;      // ScrollView grows by default; the inspector takes the room
            helpPanel.style.maxHeight = 260f;
            rightColumn.Add(helpPanel);

            middle.Add(rightColumn);

            rootVisualElement.Add(middle);
            rootVisualElement.Add(BuildFindingsPanel());

            SetHelpVisible(EditorPrefs.GetBool(ShowHelpPref, false));
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
            bar.Add(new ToolbarButton(ApplyTopology) { text = "Apply Level Topology" });

            entryDropdown = new DropdownField("New Game Entry");
            entryDropdown.style.minWidth = 230f;
            entryDropdown.RegisterValueChangedCallback(evt =>
            {
                if (doc == null || doc.EntryRoom == evt.newValue) return;
                doc.EntryRoom = evt.newValue ?? string.Empty;
                OnGraphChanged();
            });
            bar.Add(entryDropdown);

            var spacer = new ToolbarSpacer { flex = true };
            bar.Add(spacer);

            bar.Add(new ToolbarButton(DeepValidate) { text = "Deep Validate (opens scenes)" });
            bar.Add(new ToolbarButton(FixAll) { text = "Fix All" });

            helpToggle = new ToolbarToggle { text = "Help" };
            helpToggle.RegisterValueChangedCallback(evt => SetHelpVisible(evt.newValue));
            bar.Add(helpToggle);

            return bar;
        }

        private void SetHelpVisible(bool visible)
        {
            if (helpPanel != null) helpPanel.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

            // SetValueWithoutNotify: this is also called from CreateGUI to restore the saved state, and
            // notifying there would write the pref back before the user has touched anything.
            helpToggle?.SetValueWithoutNotify(visible);
            EditorPrefs.SetBool(ShowHelpPref, visible);
        }

        // ---- help ----

        /// <summary>
        /// The tutorial. Written out here rather than pointed at a wiki because the thing it explains
        /// — that one door is a five-part contract nothing in the game checks — is the reason this
        /// window exists, and a designer who has not read it will not understand a single finding.
        /// </summary>
        private VisualElement BuildHelpPanel()
        {
            var scroll = new ScrollView { name = "help" };
            scroll.style.flexShrink = 0f;
            scroll.style.borderTopWidth = 1f;
            scroll.style.borderTopColor = new Color(0f, 0f, 0f, 0.4f);
            scroll.style.paddingLeft = 10f;
            scroll.style.paddingRight = 10f;
            scroll.style.paddingBottom = 12f;

            VisualElement c = scroll.contentContainer;

            Title(c, "Level Graph — Getting Started");

            Heading(c, "1 · What this window owns");
            Body(c, "LevelGraph.txt is the source of truth for the level topology — SceneDirector reads "
                  + "it directly at runtime, so what this window saves is literally what the game runs.");
            Body(c, "Edit here, press Apply Level Topology, and the file is written, the scenes are "
                  + "wired and both build lists are updated.");

            Heading(c, "2 · One door is five things");
            Body(c, "A link from A to B is a contract with five parts. Break any one and the only symptom "
                  + "is a warning the moment a player walks into that door, followed by a bounce back to "
                  + "the main menu.");
            Step(c, 1, "The link in this graph.");
            Step(c, 2, "A LevelExit in scene A whose exitId is B.");
            Step(c, 3, "A checkpoint named Spawn_A in scene B — without it, arrivals land at B's start point instead of the doorway.");
            Step(c, 4, "B is in Build Settings.");
            Step(c, 5, "B is a room in LevelGraph.txt.");
            Body(c, "The running game rejects invalid entry/load requests. Deep Validate catches the full authoring contract before play.");

            Heading(c, "3 · Toolbar");
            Term(c, "Reload", "Re-reads LevelGraph.txt. Discards edits you have not applied.");
            Term(c, "Apply Level Topology", "The one-button workflow: writes the graph to LevelGraph.txt, "
                                           + "creates every missing door and spawn point in the room "
                                           + "scenes, and updates both build lists. Additive and "
                                           + "idempotent — applying an unchanged graph writes nothing.");
            Term(c, "Deep Validate", "Opens every room scene to check the door wiring. Seconds, not "
                                   + "milliseconds; your open scenes are restored afterwards.");
            Term(c, "Fix All", "Creates everything the graph says is missing. It only ever creates.");

            Heading(c, "4 · On the canvas");
            Term(c, "Left shelf", "Every scene under Assets/Scenes. Drag one onto the canvas to adopt it as a room; double-click opens it.");
            Term(c, "New Game Entry", "Explicitly selects the room loaded by New Game. An empty or invalid entry blocks Apply.");
            Term(c, "Right inspector", "Click a node to configure its room: display name and door count. Apply writes the edit into the graph.");
            Term(c, "Drag out → in", "New link. Two-way by default, which is what nearly every door is.");
            Term(c, "Right-click a link", "Switch between two-way (<->) and one-way (->).");
            Term(c, "Right-click a node", "Set As Entry, rename its display name, or open its scene.");
            Term(c, "Double-click a node", "Open its scene.");
            Term(c, "Delete key", "Removes selected nodes and links from the graph. Scenes are untouched.");
            Body(c, "The stripe under each node title is its worst finding: green passes, blue is a note, "
                  + "yellow a warning, red an error. A thick orange edge is a one-way door. The x/y badge "
                  + "is door occupancy (connected doors / door count).");

            Heading(c, "5 · Adopting a hand-authored level");
            Body(c, "B1.unity and B2.unity contain no LevelExit at all, so the flow cannot reach them. "
                  + "This is the sequence that brings one in:");
            Step(c, 1, "Drag the scene from the left shelf onto the canvas — it becomes a room.");
            Step(c, 2, "Click the node, set its door count in the inspector, press Apply.");
            Step(c, 3, "Drag links from whichever rooms should connect.");
            Step(c, 4, "Press Apply Level Topology — doors, spawn points and build lists all follow.");
            Step(c, 5, "Deep Validate, then drag the created doors / spawn points to their real positions and save.");

            Heading(c, "6 · What this tool will never do");
            Bullet(c, "Never deletes a LevelExit or a Checkpoint.");
            Bullet(c, "Never removes a Build Settings entry.");
            Bullet(c, "Never regenerates or overwrites a scene.");
            Body(c, "An exit the graph has no link for is reported (C16), not removed — it is far more "
                  + "likely to be a secret door somebody placed than a mistake, and this tool has no way "
                  + "to tell. Missing room scenes are errors and must be authored explicitly.");

            Heading(c, "7 · Validation rules");
            Body(c, "Graph rules re-run on every edit. Scene rules need Deep Validate.");

            AddRuleGroup(c, "Graph", RuleLayer.Graph);
            AddRuleGroup(c, "Scenes (Deep Validate)", RuleLayer.Scenes);

            return scroll;
        }

        private void AddRuleGroup(VisualElement parent, string label, RuleLayer layer)
        {
            SubHeading(parent, label);

            foreach (RuleInfo rule in LevelGraphRules.InLayer(layer))
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.marginBottom = 3f;

                var code = new Label(rule.Code);
                code.style.width = 34f;
                code.style.flexShrink = 0f;
                code.style.unityFontStyleAndWeight = FontStyle.Bold;
                code.style.color = ColorFor(rule.Severity);   // same legend as the findings list below
                row.Add(code);

                var text = new Label(rule.Fixable ? rule.Title + "  [Fix]" : rule.Title);
                text.style.flexGrow = 1f;
                text.style.whiteSpace = WhiteSpace.Normal;
                text.style.fontSize = 11;
                text.style.color = new Color(0.78f, 0.78f, 0.78f);
                row.Add(text);

                parent.Add(row);
            }
        }

        // ---- small text builders, in the same shape as MakeFindingRow below ----

        private static void Title(VisualElement parent, string text)
        {
            var label = new Label(text);
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.fontSize = 14;
            label.style.marginTop = 10f;
            label.style.marginBottom = 6f;
            label.style.whiteSpace = WhiteSpace.Normal;
            parent.Add(label);
        }

        private static void Heading(VisualElement parent, string text)
        {
            var label = new Label(text);
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.fontSize = 12;
            label.style.marginTop = 14f;
            label.style.marginBottom = 4f;
            label.style.color = new Color(0.62f, 0.78f, 0.95f);
            label.style.whiteSpace = WhiteSpace.Normal;
            parent.Add(label);
        }

        private static void SubHeading(VisualElement parent, string text)
        {
            var label = new Label(text);
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.fontSize = 11;
            label.style.marginTop = 8f;
            label.style.marginBottom = 3f;
            label.style.color = new Color(0.65f, 0.65f, 0.65f);
            parent.Add(label);
        }

        private static void Body(VisualElement parent, string text)
        {
            var label = new Label(text);
            label.style.fontSize = 11;
            label.style.marginBottom = 5f;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.color = new Color(0.80f, 0.80f, 0.80f);
            parent.Add(label);
        }

        private static void Bullet(VisualElement parent, string text) => Prefixed(parent, "•", text, 14f);

        private static void Step(VisualElement parent, int index, string text) => Prefixed(parent, index + ".", text, 18f);

        private static void Term(VisualElement parent, string term, string text)
        {
            var label = new Label();
            label.style.fontSize = 11;
            label.style.marginBottom = 5f;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.color = new Color(0.80f, 0.80f, 0.80f);

            // Rich text rather than two elements: the term has to wrap together with the sentence that
            // explains it, and a bold Label beside a normal one cannot share a line box.
            label.enableRichText = true;
            label.text = $"<b>{term}</b> — {text}";
            parent.Add(label);
        }

        private static void Prefixed(VisualElement parent, string prefix, string text, float indent)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom = 3f;

            var marker = new Label(prefix);
            marker.style.width = indent;
            marker.style.flexShrink = 0f;
            marker.style.fontSize = 11;
            marker.style.color = new Color(0.65f, 0.65f, 0.65f);
            row.Add(marker);

            var body = new Label(text);
            body.style.flexGrow = 1f;
            body.style.fontSize = 11;
            body.style.whiteSpace = WhiteSpace.Normal;
            body.style.color = new Color(0.80f, 0.80f, 0.80f);
            row.Add(body);

            parent.Add(row);
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
            doc = LevelGraphFile.Load(out parseErrors);
            dirty = false;
            sceneFindings.Clear();

            graph.Populate(doc);
            RefreshEntryDropdown();
            sceneTree?.Refresh(doc);
            Revalidate(keepSceneFindings: false);

            if (doc.Rooms.Count == 0 && !LevelGraphFile.Exists)
            {
                summary.text = $"{LevelGraphFile.Path} does not exist yet — drag scenes from the left shelf onto the canvas to build a graph.";
            }
        }

        /// <summary>
        /// The one-button workflow: writes the graph to the text file (the source of truth SceneDirector
        /// reads at runtime), wires every room scene with the doors / spawn points its edges require,
        /// and makes sure the scenes are in the build lists (global and build profile). Everything it
        /// does is additive and idempotent.
        /// </summary>
        private void ApplyTopology()
        {
            if (doc == null) return;

            graph.WriteBackPositions();
            Revalidate(keepSceneFindings: false);
            foreach (Finding finding in findings)
            {
                if (finding.Severity != Severity.Error) continue;
                Debug.LogError($"Level topology not applied: [{finding.Code}] {finding.Message}");
                EditorUtility.DisplayDialog("Cannot apply level topology",
                    $"Fix all errors before applying.\n\n[{finding.Code}] {finding.Message}", "OK");
                return;
            }

            LevelGraphFile.Save(doc);

            // Scene wiring before the build lists: FixBuildSettings reads the graph file back, and
            // wiring works off the in-memory doc — order between them does not matter, but doing the
            // slow part (scene opens) once is the point of batching both here.
            int wired = LevelGraphFixer.EnsureSceneWiring(doc);

            int profileChanges = LevelGraphProjectSetup.Apply(doc);
            dirty = false;

            sceneTree?.Refresh(doc);
            Revalidate(keepSceneFindings: false);
            Debug.Log($"Level topology applied: {doc.Rooms.Count} rooms, {doc.Links.Count} links, "
                    + $"{wired} door(s)/spawn(s) created, build settings updated, "
                    + $"{profileChanges} build-profile scene entry/entries updated.");
        }

        private void DeepValidate()
        {
            if (doc == null) return;

            if (dirty && !EditorUtility.DisplayDialog(
                    "Unsaved graph changes",
                    "The graph has edits that have not been applied. Deep validation checks the scenes against the *saved* graph, so it will not see them.\n\nValidate anyway?",
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

            Debug.Log($"Level graph: fixed {repaired} finding(s). Created objects sit in a row of slots near the origin — drag them into place.");
        }

        private void OnGraphChanged()
        {
            dirty = true;
            RefreshEntryDropdown();

            // Scene findings describe the project on disk, which an unapplied edit has not changed —
            // keeping them would show doors as missing that are only missing in the edit.
            sceneFindings.Clear();
            Revalidate(keepSceneFindings: false);
        }

        // ---- validation plumbing ----

        private void Revalidate(bool keepSceneFindings)
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

        private void RefreshEntryDropdown()
        {
            if (entryDropdown == null || doc == null) return;
            var choices = new List<string>();
            foreach (RoomEntry room in doc.Rooms) choices.Add(room.Name);
            entryDropdown.choices = choices;
            entryDropdown.SetValueWithoutNotify(doc.EntryRoom ?? string.Empty);
        }
    }
}
