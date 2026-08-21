using Inkform.Audio;
using Inkform.Settings;
using Inkform.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Inkform.EditorTools
{
    /// <summary>
    /// One-shot UI builder: creates the four panel prefabs (main menu / pause / save / settings) under
    /// Assets/Prefabs/UI, wires them into GameManager.prefab's UIManager component, builds the main
    /// menu scene under Assets/Scenes/Menu, and puts that scene at index 0 of Build Settings.
    ///
    /// The layout follows Docs/Bomb Slime UI.png. Every coordinate below is in the UI Canvas's
    /// 1920x1080 reference space (UIManager.CreateCanvas) with the origin at the screen centre.
    ///
    /// The generated prefabs are plain uGUI structures (CanvasGroup root + named Buttons). All button
    /// wiring happens at runtime by the panel scripts finding children by name — the builder only
    /// needs to name them correctly. Re-running is safe (it overwrites in place), but it *does*
    /// overwrite: hand edits to the four prefabs or to the menu scene are lost on the next run.
    ///
    /// Two things are pointedly outside that rule, because they are yours to edit rather than the
    /// builder's to own: the menu Cue assets under Assets/Audio/UI are created only when missing, and
    /// UIManager's Cue slots are filled only when empty. Everything else here is regenerated.
    ///
    /// Menu item: Tools &gt; Inkform &gt; Build UI. Also callable headlessly:
    ///   Unity -batchmode -quit -executeMethod Inkform.EditorTools.UIBuilder.BuildAll
    /// </summary>
    public static class UIBuilder
    {
        private const string PanelsDir = "Assets/Prefabs/UI";
        private const string MenuDir = "Assets/Scenes/Menu";
        private const string MenuScenePath = MenuDir + "/MainMenu.unity";
        private const string GameManagerPrefabPath = "Assets/Prefabs/GameManager.prefab";

        // ---- Settings sheet geometry ----

        private const float PaneX = 205f;                       // content pane centre, right of the tab column
        private const float PaneW = 1200f;
        private const float PaneH = 680f;
        private const float PanePad = 20f;                      // pane frame inset
        private const float BarW = 30f;                         // scrollbar strip on the pane's right edge
        private const float ViewportH = PaneH - PanePad * 2f;   // the visible height of a tab's content

        // Scrolling height per tab. Sound and Graphics match the viewport exactly — they do not scroll.
        // Controls runs well past it: the key-binding rows live below the fold, which is what the
        // design's partially-filled scrollbars imply.
        private const float SoundContentH = ViewportH;
        private const float GraphicsContentH = ViewportH;
        private const float ControlsContentH = 1000f;

        // One shared column geometry for every settings row, so the three tabs line up with each other.
        private const float RowLeft = -540f;                    // left edge each row's name label starts at
        private const float RowLabelW = 420f;                   // wide enough for "Controller Sensitivity"

        // Panel palette. Lives in Inkform.UI.UiPalette now that UiToggleFx paints a control at runtime
        // and has to reach the same colours; aliased here so the call sites below stay short.
        private static class Palette
        {
            public static Color Sheet => UiPalette.Sheet;
            public static Color Card => UiPalette.Card;
            public static Color Pane => UiPalette.Pane;
            public static Color Control => UiPalette.Control;
            public static Color ControlHover => UiPalette.ControlHover;
            public static Color ControlPressed => UiPalette.ControlPressed;
            public static Color ControlOff => UiPalette.ControlOff;
            public static Color Disabled => UiPalette.Disabled;
            public static Color OffText => UiPalette.OffText;
            public static Color Accent => UiPalette.Accent;
            public static Color Track => UiPalette.Track;
            public static Color Handle => UiPalette.Handle;
            public static Color Hint => UiPalette.Hint;
            public static Color Key => UiPalette.Key;
            public static Color None => UiPalette.None;
        }

        /// <summary>
        /// The type scale. Every label in the UI picks one of these seven steps; before this there
        /// were nine unrelated magic numbers spread across the call sites, with only the settings row
        /// size named at all, and nothing tying a control's text to the control's own height — the
        /// save slots were the worst of it, 34pt inside a 235px row.
        ///
        /// Do not reach for Text.resizeTextForBestFit instead: every label in the built prefabs
        /// carries Unity's default 10..40 best-fit range, so switching it on globally would quietly
        /// clamp the two titles down to 40.
        /// </summary>
        private static class FontSize
        {
            public const int Display = 96;   // main menu title
            public const int Title = 64;     // settings sheet title
            public const int Heading = 40;   // save slots, the pause sheet's Resume — the oversized rows
            public const int Button = 30;    // ordinary menu and tab buttons (AddButton's default)
            public const int Body = 26;      // settings row names and their values
            public const int Small = 22;     // key-binding rows, dropdown, compact buttons
            public const int Caption = 18;   // explanatory lines
        }

        [MenuItem("Tools/Inkform/Build UI")]
        public static void BuildAll()
        {
            EnsureFolders();

            GameObject mainMenu = BuildPanelPrefab("MainMenu", typeof(MainMenuPanel), panel =>
            {
                AddTitle(panel, "Bomb Slime", 250f, FontSize.Display);
                AddButton(panel, "Btn_Play", "Play", new Vector2(-600f, -70f), new Vector2(360f, 70f));
                AddButton(panel, "Btn_Settings", "Settings", new Vector2(-600f, -210f), new Vector2(360f, 70f));
                AddButton(panel, "Btn_Exit", "Exit", new Vector2(-600f, -350f), new Vector2(360f, 70f));
            });

            GameObject pause = BuildPanelPrefab("PauseMenu", typeof(PausePanel), panel =>
            {
                // The card is added first so uGUI's hierarchy draw order puts it behind the buttons.
                // The buttons stay direct children of the panel root rather than of the card: PausePanel
                // finds them with transform.Find(name), which does not search grandchildren.
                AddImage(panel.transform, "Card", Palette.Card,
                    rt => StretchCenter(rt, Vector2.zero, new Vector2(790f, 720f)));

                AddButton(panel, "Btn_Resume", "Resume", new Vector2(0f, 140f), new Vector2(640f, 120f), FontSize.Heading);
                AddButton(panel, "Btn_Settings", "Settings", -70f);
                AddButton(panel, "Btn_SaveAndQuit", "Save & Quit", -200f);
            }, Palette.None);

            GameObject saveMenu = BuildPanelPrefab("SaveMenu", typeof(SaveMenuPanel), panel =>
            {
                // Three wide rows filling the sheet, no title and no Back button — Escape backs out.
                for (int i = 0; i < 3; i++)
                    AddButton(panel, $"Btn_Slot{i}", "No Records", new Vector2(0f, 285f - i * 285f), new Vector2(1580f, 235f), FontSize.Heading);

                // Paging placeholders, flanking the first row as the design draws them. SaveMenuPanel
                // keeps them inert until save slots hold more than one page.
                AddButton(panel, "Btn_Left", "<", new Vector2(-880f, 285f), new Vector2(80f, 100f), FontSize.Heading);
                AddButton(panel, "Btn_Right", ">", new Vector2(855f, 285f), new Vector2(80f, 100f), FontSize.Heading);
            });

            GameObject settings = BuildPanelPrefab("Settings", typeof(SettingsPanel), panel =>
            {
                // Title and the bottom row are pulled ~20px toward the centre from where they sat.
                // The canvas scaler splits its match between width and height (UIManager.CreateCanvas),
                // so on a screen narrower than 16:9 the vertical extremes are what leaves the frame
                // first — this sheet was the tallest thing in the UI at roughly 980 of 1080.
                AddTitle(panel, "Settings", 430f, FontSize.Title);

                AddButton(panel, "Btn_TabSound", "Sound", new Vector2(-665f, 300f), new Vector2(360f, 70f));
                AddButton(panel, "Btn_TabGraphics", "Graphics", new Vector2(-665f, 0f), new Vector2(360f, 70f));
                AddButton(panel, "Btn_TabControls", "Controls", new Vector2(-665f, -300f), new Vector2(360f, 70f));

                BuildSoundTab(AddScrollPane(panel, "Content_Sound", SoundContentH));
                BuildGraphicsTab(AddScrollPane(panel, "Content_Graphics", GraphicsContentH));
                BuildControlsTab(AddScrollPane(panel, "Content_Controls", ControlsContentH));

                AddButton(panel, "Btn_Reset", "Reset Settings", new Vector2(360f, -435f), new Vector2(380f, 64f), FontSize.Button);
                AddButton(panel, "Btn_Back", "Back", new Vector2(700f, -435f), new Vector2(200f, 64f), FontSize.Button);
            });

            WireGameManager(mainMenu, pause, saveMenu, settings);
            BuildMenuScene();
            SetBuildSettings();

            Debug.Log("Inkform UI built: prefabs, GameManager wiring, MainMenu scene, build settings.");
        }

        // ---- Prefab generation ----

        private static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder(PanelsDir))
                AssetDatabase.CreateFolder("Assets/Prefabs", "UI");
            if (!AssetDatabase.IsValidFolder(MenuDir))
                AssetDatabase.CreateFolder("Assets/Scenes", "Menu");
            if (!AssetDatabase.IsValidFolder(UiAudioDir))
                AssetDatabase.CreateFolder("Assets/Audio", "UI");
        }

        // ---- Menu sound cues ----

        private const string UiAudioDir = "Assets/Audio/UI";

        /// <summary>
        /// Creates the four menu Cue assets if they are not already there, and hands them back for
        /// WireGameManager to drop into UIManager's slots.
        ///
        /// Create-if-missing, never overwrite. Unlike the panel prefabs — which this builder owns
        /// outright and rebuilds every run — a Cue asset is something you edit: the clips get dragged
        /// in by hand, and regenerating the asset would throw them away on the next run.
        ///
        /// The clips array is left empty. No menu sound effects exist in the project yet (the whole
        /// Audio folder is bombs, walls, deaths and respawns), and an unfilled Cue is a supported
        /// state throughout this codebase — AudioManager.Play returns null on a Cue with no clip and
        /// says nothing. Drop a wav into one of these assets and it starts sounding; no code changes.
        /// </summary>
        private static void EnsureUiCues(out SoundCue hover, out SoundCue click,
            out SoundCue toggleOn, out SoundCue toggleOff)
        {
            // Hover fires on every pointer cross and every navigation step, so it is the one that has
            // to stay out of the way: quietest, shortest cooldown ceiling, and a tight pitch spread so
            // sweeping across a menu does not sound like a rattle.
            hover = EnsureCue("SFX_UiHover", volume: 0.45f, cooldown: 0.04f, maxConcurrent: 2);
            click = EnsureCue("SFX_UiClick", volume: 0.8f, cooldown: 0.03f, maxConcurrent: 3);
            toggleOn = EnsureCue("SFX_UiToggleOn", volume: 0.7f, cooldown: 0.03f, maxConcurrent: 2);
            toggleOff = EnsureCue("SFX_UiToggleOff", volume: 0.7f, cooldown: 0.03f, maxConcurrent: 2);
        }

        private static SoundCue EnsureCue(string assetName, float volume, float cooldown, int maxConcurrent)
        {
            string path = $"{UiAudioDir}/{assetName}.asset";

            SoundCue existing = AssetDatabase.LoadAssetAtPath<SoundCue>(path);
            if (existing != null) return existing;

            SoundCue cue = ScriptableObject.CreateInstance<SoundCue>();
            cue.volume = volume;
            cue.cooldown = cooldown;
            cue.maxConcurrent = maxConcurrent;
            cue.category = SoundCue.Category.Sfx;   // so the SFX Volume slider scales menu sounds too
            cue.spatial = false;                    // a menu has no position to be far from
            cue.pitchRange = new Vector2(0.98f, 1.02f);

            AssetDatabase.CreateAsset(cue, path);
            return cue;
        }

        /// <summary>
        /// Builds one panel prefab: CanvasGroup root + panel script + a full-screen backdrop Image, then
        /// whatever the caller adds.
        ///
        /// backdrop defaults to the opaque sheet colour, which covers the frozen game frame. The pause
        /// sheet passes a fully transparent colour instead — the design leaves the game visible behind
        /// its card. That still blocks clicks from reaching the world: uGUI's hit test reads
        /// raycastTarget and alphaHitTestMinimumThreshold, never the colour, so an alpha-0 Image is an
        /// invisible blocker rather than a no-op. Do not "clean it up".
        /// </summary>
        private static GameObject BuildPanelPrefab(string prefabName, System.Type scriptType,
            System.Action<GameObject> build, Color? backdrop = null)
        {
            GameObject root = new GameObject(prefabName, typeof(RectTransform));
            root.AddComponent<CanvasGroup>();
            root.AddComponent(scriptType);

            Image bg = root.AddComponent<Image>();
            bg.color = backdrop ?? Palette.Sheet;

            RectTransform rt = root.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            build(root);

            string path = $"{PanelsDir}/{prefabName}.prefab";
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        private static void AddTitle(GameObject panel, string text, float y, int fontSize)
        {
            AddLabel(panel, "Title", text, new Vector2(0f, y), new Vector2(1200f, fontSize * 1.5f),
                fontSize, TextAnchor.MiddleCenter, Color.white);
        }

        private static void AddLabel(GameObject parent, string name, string text, Vector2 pos, Vector2 size,
            int fontSize, TextAnchor align, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent.transform, false);

            StretchCenter(go.GetComponent<RectTransform>(), pos, size);

            Text label = go.GetComponent<Text>();
            label.text = text;
            label.fontSize = fontSize;
            label.alignment = align;
            label.color = color;
            label.font = FontUtils.LegacyFont;

            // Labels never handle pointer input, and a settings row's name rect is wide enough to
            // overlap the controls beside it — left as a raycast target it would eat their clicks.
            label.raycastTarget = false;
        }

        private static void AddButton(GameObject panel, string buttonName, string labelText, float y)
            => AddButton(panel, buttonName, labelText, new Vector2(0f, y), new Vector2(360f, 70f));

        private static void AddButton(GameObject panel, string buttonName, string labelText, Vector2 pos, Vector2 size, int fontSize = FontSize.Button)
        {
            GameObject go = new GameObject(buttonName,
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(UiButtonFx));
            go.transform.SetParent(panel.transform, false);

            RectTransform rt = go.GetComponent<RectTransform>();
            StretchCenter(rt, pos, size);

            // White, with the real colour moved into the ColorBlock below. ColorTint *multiplies* the
            // target graphic's colour, so painting the Image dark here is what made hover invisible:
            // Unity's stock highlighted value is 0.96 grey, and 0.96 x Control is a two-step change
            // nobody can see. Against white the block's colours come through exactly as written.
            Image img = go.GetComponent<Image>();
            img.color = Color.white;

            Button button = go.GetComponent<Button>();
            button.targetGraphic = img;

            // Transition stays ColorTint rather than moving to None with UiButtonFx driving colour
            // too. The settings sheet marks its current tab by switching that button's interactable
            // off (SettingsPanel.SetTab), and the disabled tint is the only thing that draws it —
            // turning transitions off would erase the tab indicator. So: uGUI owns colour, the
            // component owns scale, and they never write the same field.
            button.transition = Selectable.Transition.ColorTint;
            button.colors = new ColorBlock
            {
                normalColor = Palette.Control,
                highlightedColor = Palette.ControlHover,
                pressedColor = Palette.ControlPressed,
                selectedColor = Palette.ControlHover,   // gamepad focus should read the same as hover
                disabledColor = Palette.Disabled,
                colorMultiplier = 1f,
                fadeDuration = 0.1f,
            };

            // Button label. The Image above is the raycast target, so the text does not need to be one.
            GameObject textGo = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            textGo.transform.SetParent(go.transform, false);
            StretchFill(textGo.GetComponent<RectTransform>());

            Text label = textGo.GetComponent<Text>();
            label.text = labelText;
            label.fontSize = fontSize;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            label.font = FontUtils.LegacyFont;
            label.raycastTarget = false;
        }

        /// <summary>
        /// One settings tab's content area: a framed pane carrying a ScrollRect, a viewport that clips,
        /// and a Content rect of the caller's height. Rows go into the returned Content object.
        ///
        /// Content is top-anchored (pivot 0.5/1) so a tab's height grows downward as it gains rows,
        /// while its children still use the same StretchCenter as everywhere else — a child's 0.5/0.5
        /// anchor resolves against Content's own rect regardless of Content's pivot. Each BuildXTab
        /// turns "distance from the top" into that centre-relative y with a local Y() helper.
        ///
        /// The bar is built even when contentHeight equals the viewport (Sound, Graphics): the design
        /// shows it on all three tabs, and Permanent visibility is what keeps the pane's usable width
        /// fixed — AutoHideAndExpandViewport would reflow the columns these tabs are laid out against.
        /// </summary>
        private static GameObject AddScrollPane(GameObject panel, string contentName, float contentHeight)
        {
            GameObject pane = new GameObject(contentName,
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(ScrollRect));
            pane.transform.SetParent(panel.transform, false);
            StretchCenter(pane.GetComponent<RectTransform>(), new Vector2(PaneX, 0f), new Vector2(PaneW, PaneH));
            pane.GetComponent<Image>().color = Palette.Pane;

            // RectMask2D rather than Mask: no second Image to feed it and no stencil buffer cost.
            GameObject viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(pane.transform, false);
            RectTransform vp = viewport.GetComponent<RectTransform>();
            vp.anchorMin = Vector2.zero;
            vp.anchorMax = Vector2.one;
            vp.pivot = new Vector2(0.5f, 0.5f);
            vp.offsetMin = new Vector2(PanePad, PanePad);
            vp.offsetMax = new Vector2(-(PanePad + BarW), -PanePad);

            GameObject content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            RectTransform ct = content.GetComponent<RectTransform>();
            ct.anchorMin = new Vector2(0f, 1f);
            ct.anchorMax = new Vector2(1f, 1f);
            ct.pivot = new Vector2(0.5f, 1f);
            ct.anchoredPosition = Vector2.zero;
            ct.sizeDelta = new Vector2(0f, contentHeight);   // x offset 0 = stretch to the viewport's width

            Scrollbar bar = AddScrollbar(pane);

            ScrollRect scroll = pane.GetComponent<ScrollRect>();
            scroll.viewport = vp;
            scroll.content = ct;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;
            scroll.verticalScrollbar = bar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

            return content;
        }

        /// <summary>Vertical scrollbar down the pane's right edge. The handle carries no offsets of its
        /// own — Scrollbar drives its anchors and leaves the rect to follow.</summary>
        private static Scrollbar AddScrollbar(GameObject pane)
        {
            GameObject root = new GameObject("Scrollbar_V",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Scrollbar));
            root.transform.SetParent(pane.transform, false);

            RectTransform rt = root.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = new Vector2(-PanePad, 0f);
            rt.sizeDelta = new Vector2(BarW, -PanePad * 2f);    // y is an offset: full height less the pane padding

            root.GetComponent<Image>().color = Palette.Track;

            GameObject slide = new GameObject("Sliding Area", typeof(RectTransform));
            slide.transform.SetParent(root.transform, false);
            StretchFill(slide.GetComponent<RectTransform>());

            Image handle = AddImage(slide.transform, "Handle", Palette.Handle, StretchFill);

            Scrollbar bar = root.GetComponent<Scrollbar>();
            bar.handleRect = handle.rectTransform;
            bar.targetGraphic = handle;
            bar.direction = Scrollbar.Direction.BottomToTop;
            return bar;
        }

        // ---- Settings rows ----

        /// <summary>A settings row's name, in the shared left column. Deliberately wider than most of
        /// the strings it holds so every tab's text starts on the same pixel.</summary>
        private static void AddRowLabel(GameObject content, string text, float y)
        {
            AddLabel(content, "Label", text, new Vector2(RowLeft + RowLabelW * 0.5f, y),
                new Vector2(RowLabelW, 36f), FontSize.Body, TextAnchor.MiddleLeft, Color.white);
        }

        /// <summary>
        /// One "&lt; value &gt;" row: name on the left, then the two step buttons around the current
        /// value. Shared by every option the panel cycles — resolution, fullscreen, frame cap, map.
        /// Two-state options use the same shape as the cycling ones; SettingsPanel just flips them.
        /// </summary>
        private static void AddArrowRow(GameObject content, string rowName, string leftName, string valueName,
            string rightName, string initialValue, float y)
        {
            AddRowLabel(content, rowName, y);
            AddButton(content, leftName, "<", new Vector2(-258f, y), new Vector2(56f, 56f));
            AddLabel(content, valueName, initialValue, new Vector2(-120f, y), new Vector2(300f, 40f),
                FontSize.Body, TextAnchor.MiddleCenter, Color.white);
            AddButton(content, rightName, ">", new Vector2(25f, y), new Vector2(56f, 56f));
        }

        /// <summary>
        /// Sound tab: a Mute checkbox, then "Main Volume" / "Music Volume" / "SFX Volume" as a name
        /// above a wide slider with the percentage to its right (the controls are found by
        /// SettingsPanel at runtime by the Tgl_ / Sld_ / Lbl_ names).
        /// </summary>
        private static void BuildSoundTab(GameObject content)
        {
            float Y(float fromTop) => SoundContentH * 0.5f - fromTop;

            // Laid out like the Graphics tab's Show FPS row rather than box-then-name as before: the
            // toggle now carries an ON/OFF word on its right, which would have run straight into a
            // name sitting beside it. Sharing the standard row shape also lines this up with the
            // sliders below it.
            AddRowLabel(content, "Mute", Y(55f));
            AddToggle(content, "Tgl_Mute", new Vector2(-120f, Y(55f)), new Vector2(46f, 46f));

            AddSoundRow(content, "Main Volume", "Sld_Master", "Lbl_Master", Y(140f), Y(195f));
            AddSoundRow(content, "Music Volume", "Sld_Music", "Lbl_Music", Y(280f), Y(335f));
            AddSoundRow(content, "SFX Volume", "Sld_Sfx", "Lbl_Sfx", Y(420f), Y(475f));
        }

        private static void AddSoundRow(GameObject content, string rowName, string sliderName, string labelName,
            float labelY, float sliderY)
        {
            AddRowLabel(content, rowName, labelY);
            AddSlider(content, sliderName, new Vector2(RowLeft + 302f, sliderY), new Vector2(604f, 34f));
            AddLabel(content, labelName, "100%", new Vector2(150f, sliderY), new Vector2(140f, 36f),
                FontSize.Body, TextAnchor.MiddleCenter, Color.white);
        }

        /// <summary>
        /// Graphics tab: Resolution / Full Screen / FPS as arrow rows, a VFX strength slider labelled
        /// Low..High at its ends rather than by percentage, and the Show FPS toggle. Show FPS is not in
        /// the design, but the counter it drives is real — it keeps the last row.
        /// </summary>
        private static void BuildGraphicsTab(GameObject content)
        {
            float Y(float fromTop) => GraphicsContentH * 0.5f - fromTop;

            AddArrowRow(content, "Resolution", "Btn_ResLeft", "Lbl_Res", "Btn_ResRight", "1920 x 1080", Y(60f));
            AddArrowRow(content, "Full Screen", "Btn_FullLeft", "Lbl_Full", "Btn_FullRight", "ON", Y(170f));
            AddArrowRow(content, "FPS", "Btn_FpsLeft", "Lbl_Fps", "Btn_FpsRight", "60", Y(280f));

            AddRowLabel(content, "VFX", Y(385f));
            AddLabel(content, "Label", "Low", new Vector2(-300f, Y(350f)), new Vector2(160f, 30f),
                FontSize.Caption, TextAnchor.MiddleCenter, Palette.Hint);
            AddLabel(content, "Label", "High", new Vector2(300f, Y(350f)), new Vector2(160f, 30f),
                FontSize.Caption, TextAnchor.MiddleCenter, Palette.Hint);
            AddSlider(content, "Sld_Fx", new Vector2(0f, Y(385f)), new Vector2(606f, 34f));

            AddRowLabel(content, "Show FPS", Y(495f));
            AddToggle(content, "Tgl_ShowFps", new Vector2(-120f, Y(495f)), new Vector2(46f, 46f));
        }

        /// <summary>
        /// Controls tab: the device dropdown, both sensitivity sliders (shown together — the device
        /// picker no longer hides one), and Unstuck with its explanatory line.
        /// Below those, past the fold the pane scrolls to reach, the key-binding half: a Reset Bindings
        /// button and the two per-device sub-panes, one row per SettingsPanel KbmRows / GamepadRows
        /// entry in the same order. Only the selected device's pane is active.
        /// </summary>
        private static void BuildControlsTab(GameObject content)
        {
            float Y(float fromTop) => ControlsContentH * 0.5f - fromTop;

            AddRowLabel(content, "Input Device", Y(55f));
            AddDropdown(content, "Dpd_Device", new Vector2(-90f, Y(55f)), new Vector2(260f, 56f),
                "Keyboard & Mouse", "Controller");

            AddSensRow(content, "Mouse Sensitivity", "Sld_Mouse", "Lbl_Mouse", Y(145f));
            AddSensRow(content, "Controller Sensitivity", "Sld_Stick", "Lbl_Stick", Y(210f));

            AddButton(content, "Btn_Unstuck", "Unstuck", new Vector2(RowLeft + 90f, Y(285f)), new Vector2(180f, 56f), FontSize.Small);
            AddLabel(content, "Label", "Use this button if you are stuck in a bug",
                new Vector2(RowLeft + 320f, Y(337f)), new Vector2(640f, 30f), FontSize.Caption, TextAnchor.MiddleLeft, Palette.Hint);

            AddRowLabel(content, "Key Bindings", Y(410f));
            AddButton(content, "Btn_ResetBindings", "Reset Bindings", new Vector2(430f, Y(410f)), new Vector2(240f, 50f), FontSize.Small);

            BuildKbmPane(content);
            BuildGamepadPane(content);
        }

        /// <summary>Sensitivity row: name, slider over the store's own 0~5 range, and the multiplier
        /// readout. Starts at 1 (no change) to match the settings default, not at the slider's max.</summary>
        private static void AddSensRow(GameObject content, string rowName, string sliderName, string labelName, float y)
        {
            AddRowLabel(content, rowName, y);
            AddSlider(content, sliderName, new Vector2(60f, y), new Vector2(604f, 34f),
                SettingsStore.MinSensitivity, SettingsStore.MaxSensitivity, 1f);
            AddLabel(content, labelName, "1.0", new Vector2(430f, y), new Vector2(140f, 36f),
                FontSize.Body, TextAnchor.MiddleCenter, Color.white);
        }

        // Both device sub-panes start at the same height and share a pitch, so switching device does
        // not make the list jump. Fills the Controls tab from the fold down to its content height.
        private static float BindRowY(int index) => ControlsContentH * 0.5f - (545f + index * 46f);

        private static void BuildKbmPane(GameObject content)
        {
            GameObject kbm = new GameObject("Content_Kbm", typeof(RectTransform));
            kbm.transform.SetParent(content.transform, false);
            StretchFill(kbm.GetComponent<RectTransform>());

            string[] rows =
            {
                "Move Up", "Move Down", "Move Left", "Move Right",
                "Jump", "Dash", "Rope Fire", "Spit Bomb", "Aim (Mouse)",
            };
            for (int i = 0; i < rows.Length; i++)
                AddBindRow(kbm, "K", i, rows[i], BindRowY(i));
        }

        private static void BuildGamepadPane(GameObject content)
        {
            GameObject gamepad = new GameObject("Content_Gamepad", typeof(RectTransform));
            gamepad.transform.SetParent(content.transform, false);
            StretchFill(gamepad.GetComponent<RectTransform>());

            string[] rows =
            {
                "Move (Left Stick)", "Aim (Right Stick)",
                "Jump", "Dash", "Rope Fire", "Spit Bomb",
            };
            for (int i = 0; i < rows.Length; i++)
                AddBindRow(gamepad, "G", i, rows[i], BindRowY(i));
        }

        /// <summary>One key-binding row: action name (left), current binding (centre, runtime-filled),
        /// and a Rebind button. Names follow Lbl_Bind{p}{i} / Lbl_Key{p}{i} / Btn_Bind{p}{i}.</summary>
        private static void AddBindRow(GameObject parent, string prefix, int index, string displayName, float y)
        {
            AddLabel(parent, $"Lbl_Bind{prefix}{index}", displayName, new Vector2(RowLeft + RowLabelW * 0.5f, y),
                new Vector2(RowLabelW, 40f), FontSize.Small, TextAnchor.MiddleLeft, Color.white);
            AddLabel(parent, $"Lbl_Key{prefix}{index}", "—", new Vector2(60f, y),
                new Vector2(400f, 40f), FontSize.Small, TextAnchor.MiddleCenter, Palette.Key);
            AddButton(parent, $"Btn_Bind{prefix}{index}", "Rebind", new Vector2(430f, y), new Vector2(160f, 40f), FontSize.Small);
        }

        // ---- Primitive controls ----

        /// <summary>
        /// Standard horizontal uGUI slider: background strip, fill (full stretch at start; the Slider
        /// drives fillRect.anchorMax.x from value), handle knob. initialValue defaults to the maximum,
        /// which is what the 0~1 settings want (their defaults are 100%); the sensitivity sliders run
        /// to 5 and pass 1 explicitly, so the prefab's visuals agree with the panel before any input.
        /// </summary>
        private static void AddSlider(GameObject parent, string name, Vector2 pos, Vector2 size,
            float minValue = 0f, float maxValue = 1f, float? initialValue = null)
        {
            GameObject root = new GameObject(name, typeof(RectTransform), typeof(Slider));
            root.transform.SetParent(parent.transform, false);
            StretchCenter(root.GetComponent<RectTransform>(), pos, size);

            AddImage(root.transform, "Background", Palette.Control, StretchFill);

            GameObject fillArea = new GameObject("Fill Area", typeof(RectTransform));
            fillArea.transform.SetParent(root.transform, false);
            RectTransform fa = fillArea.GetComponent<RectTransform>();
            fa.anchorMin = Vector2.zero;
            fa.anchorMax = Vector2.one;
            fa.offsetMin = new Vector2(6f, 6f);
            fa.offsetMax = new Vector2(-6f, -6f);

            Image fill = AddImage(fillArea.transform, "Fill", Palette.Accent, StretchFill);

            GameObject handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
            handleArea.transform.SetParent(root.transform, false);
            RectTransform ha = handleArea.GetComponent<RectTransform>();
            ha.anchorMin = Vector2.zero;
            ha.anchorMax = Vector2.one;
            ha.offsetMin = new Vector2(10f, 0f);
            ha.offsetMax = new Vector2(-10f, 0f);

            Image handle = AddImage(handleArea.transform, "Handle", Color.white, rt =>
            {
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(22f, 22f);
            });

            Slider slider = root.GetComponent<Slider>();
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = minValue;
            slider.maxValue = maxValue;
            slider.value = initialValue ?? maxValue;
        }

        private static Image AddImage(Transform parent, string name, Color color, System.Action<RectTransform> layout)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);

            Image img = go.GetComponent<Image>();
            img.color = color;

            layout(go.GetComponent<RectTransform>());
            return img;
        }

        /// <summary>
        /// A checkbox that actually says what it is: the box fills with the accent colour and shows a
        /// tick when on, dark and empty when off, with the word ON or OFF beside it. The old shape —
        /// a small blue square fading in inside a grey one, no text, no distinct off styling — was
        /// close to unreadable at a glance.
        ///
        /// UiToggleFx owns all of it at runtime, so the Toggle's own graphic/transition are left
        /// unused: the tick is two rotated strokes under one parent rather than a single Graphic, and
        /// toggle.graphic can only point at one. Unlike the buttons there is no interactable-based
        /// state to preserve here, so giving the component the whole control costs nothing.
        ///
        /// Children are named for UiToggleFx to find (BackgroundName / CheckName / StateLabelName) —
        /// the same find-by-name contract the panels use, so regenerating the prefabs never loses a
        /// reference. The root keeps its Tgl_ name and its Toggle, which is all SettingsPanel looks for.
        ///
        /// Starts off; the panel's Refresh pulls the real state on every open, so the builder's
        /// initial state is only cosmetic.
        /// </summary>
        private static void AddToggle(GameObject parent, string name, Vector2 pos, Vector2 size)
        {
            GameObject root = new GameObject(name, typeof(RectTransform), typeof(Toggle), typeof(UiToggleFx));
            root.transform.SetParent(parent.transform, false);
            StretchCenter(root.GetComponent<RectTransform>(), pos, size);

            Image bg = AddImage(root.transform, UiToggleFx.BackgroundName, Palette.ControlOff, StretchFill);

            AddCheckMark(root, size);
            AddToggleStateLabel(root, size);

            Toggle toggle = root.GetComponent<Toggle>();
            toggle.targetGraphic = bg;              // still the raycast target and the click surface
            toggle.graphic = null;                  // the tick is a group, not one Graphic — see above
            toggle.transition = Selectable.Transition.None;
            toggle.isOn = false;
        }

        /// <summary>
        /// The tick, built from two rotated bars under a parent UiToggleFx switches on and off.
        ///
        /// Drawn rather than written: the project ships no UI sprites at all, and the panel font is a
        /// display face with no guarantee of carrying U+2713 — a missing glyph would leave the "on"
        /// state showing an empty box, which is exactly the state it needs to be distinguishable from.
        /// Two bars always render.
        /// </summary>
        private static void AddCheckMark(GameObject root, Vector2 size)
        {
            GameObject check = new GameObject(UiToggleFx.CheckName, typeof(RectTransform));
            check.transform.SetParent(root.transform, false);
            StretchFill(check.GetComponent<RectTransform>());

            float unit = Mathf.Min(size.x, size.y);
            float thickness = Mathf.Max(3f, unit * 0.13f);

            // The tick as the three points it passes through, in fractions of the box: down from the
            // upper left to a low vertex, then back up to a high right end. Stated as points rather
            // than as two placed-and-rotated bars so the strokes cannot drift apart at the joint,
            // which is the one place a hand-built tick looks broken.
            Vector2 start = new Vector2(-unit * 0.26f, unit * 0.04f);
            Vector2 vertex = new Vector2(-unit * 0.08f, -unit * 0.18f);
            Vector2 end = new Vector2(unit * 0.28f, unit * 0.22f);

            AddTickStroke(check.transform, "Stroke_S", start, vertex, thickness);
            AddTickStroke(check.transform, "Stroke_L", vertex, end, thickness);

            // Saved hidden to agree with the toggle's own isOn = false, so the prefab does not sit in
            // the project showing a tick over an unchecked box. UiToggleFx takes over from here, and
            // transform.Find still reaches an inactive child when it looks this up.
            check.SetActive(false);
        }

        /// <summary>One bar of the tick, laid along the segment a..b. The bar is authored vertical, so
        /// the rotation is the segment's own angle less the 90 degrees that +Y already sits at.</summary>
        private static void AddTickStroke(Transform parent, string name, Vector2 a, Vector2 b, float thickness)
        {
            Vector2 delta = b - a;
            float length = delta.magnitude + thickness;     // the overrun that squares off the joint
            float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg - 90f;

            Image stroke = AddImage(parent, name, Color.white, rt =>
            {
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = a + delta * 0.5f;
                rt.sizeDelta = new Vector2(thickness, length);
            });
            stroke.rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle);
            stroke.raycastTarget = false;   // the Background is the click surface
        }

        /// <summary>The ON / OFF word, parked to the right of the box. Deliberately outside the
        /// toggle's own rect — nothing masks it, and keeping the box square keeps every settings row's
        /// checkbox the same size regardless of which word is showing.</summary>
        private static void AddToggleStateLabel(GameObject root, Vector2 size)
        {
            const float gap = 12f;      // clear air between the box's right edge and the word
            const float width = 90f;    // "OFF" at Small, with room to spare

            AddLabel(root, UiToggleFx.StateLabelName, "OFF",
                new Vector2(size.x * 0.5f + gap + width * 0.5f, 0f), new Vector2(width, size.y),
                FontSize.Small, TextAnchor.MiddleLeft, Palette.OffText);
        }

        /// <summary>
        /// Legacy uGUI Dropdown, assembled by hand to the same shape the editor's own
        /// GameObject &gt; UI &gt; Dropdown produces: caption Text, arrow, and an inactive Template
        /// holding the single Item the control clones once per option. Legacy rather than TextMeshPro
        /// because every other label in this UI is a legacy Text.
        ///
        /// Caveat worth knowing before moving this control: Unity opens the list as a sibling of the
        /// Template — still inside this object — so an ancestor RectMask2D clips it, and the child
        /// Canvas the control adds for sorting does not escape that clip. The one dropdown in this UI
        /// sits on the Controls tab's top row with two options, so its ~112px list opens well inside
        /// the 640px viewport. Push it further down, or give it many more options, and it will clip;
        /// the cheap fix then is the same "&lt; value &gt;" arrow row every other setting uses.
        /// </summary>
        private static void AddDropdown(GameObject parent, string name, Vector2 pos, Vector2 size, params string[] options)
        {
            GameObject root = new GameObject(name,
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Dropdown));
            root.transform.SetParent(parent.transform, false);
            StretchCenter(root.GetComponent<RectTransform>(), pos, size);

            Image bg = root.GetComponent<Image>();
            bg.color = Palette.Control;

            // Caption fills the control, inset to leave the right end for the arrow
            GameObject captionGo = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            captionGo.transform.SetParent(root.transform, false);
            RectTransform cap = captionGo.GetComponent<RectTransform>();
            cap.anchorMin = Vector2.zero;
            cap.anchorMax = Vector2.one;
            cap.offsetMin = new Vector2(14f, 0f);
            cap.offsetMax = new Vector2(-36f, 0f);

            Text caption = captionGo.GetComponent<Text>();
            caption.fontSize = FontSize.Small;
            caption.alignment = TextAnchor.MiddleLeft;
            caption.color = Color.white;
            caption.font = FontUtils.LegacyFont;
            caption.raycastTarget = false;

            AddImage(root.transform, "Arrow", Palette.Hint, rt =>
            {
                rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
                rt.pivot = new Vector2(1f, 0.5f);
                rt.anchoredPosition = new Vector2(-12f, 0f);
                rt.sizeDelta = new Vector2(16f, 16f);
            });

            float itemH = size.y;

            GameObject template = new GameObject("Template",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(ScrollRect));
            template.transform.SetParent(root.transform, false);
            RectTransform tp = template.GetComponent<RectTransform>();
            tp.anchorMin = new Vector2(0f, 0f);
            tp.anchorMax = new Vector2(1f, 0f);
            tp.pivot = new Vector2(0.5f, 1f);
            tp.anchoredPosition = new Vector2(0f, -2f);
            tp.sizeDelta = new Vector2(0f, itemH * Mathf.Max(1, options.Length));
            template.GetComponent<Image>().color = Palette.Card;

            GameObject tViewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            tViewport.transform.SetParent(template.transform, false);
            RectTransform tvp = tViewport.GetComponent<RectTransform>();
            StretchFill(tvp);

            GameObject tContent = new GameObject("Content", typeof(RectTransform));
            tContent.transform.SetParent(tViewport.transform, false);
            RectTransform tct = tContent.GetComponent<RectTransform>();
            tct.anchorMin = new Vector2(0f, 1f);
            tct.anchorMax = new Vector2(1f, 1f);
            tct.pivot = new Vector2(0.5f, 1f);
            tct.anchoredPosition = Vector2.zero;
            tct.sizeDelta = new Vector2(0f, itemH);

            GameObject item = new GameObject("Item", typeof(RectTransform), typeof(Toggle));
            item.transform.SetParent(tContent.transform, false);
            RectTransform it = item.GetComponent<RectTransform>();
            it.anchorMin = new Vector2(0f, 0.5f);
            it.anchorMax = new Vector2(1f, 0.5f);
            it.pivot = new Vector2(0.5f, 0.5f);
            it.anchoredPosition = Vector2.zero;
            it.sizeDelta = new Vector2(0f, itemH);

            Image itemBg = AddImage(item.transform, "Item Background", Palette.Card, StretchFill);
            Image itemCheck = AddImage(item.transform, "Item Checkmark", Palette.Accent, rt =>
            {
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
                rt.pivot = new Vector2(0f, 0.5f);
                rt.anchoredPosition = new Vector2(14f, 0f);
                rt.sizeDelta = new Vector2(14f, 14f);
            });

            GameObject itemLabelGo = new GameObject("Item Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            itemLabelGo.transform.SetParent(item.transform, false);
            RectTransform il = itemLabelGo.GetComponent<RectTransform>();
            il.anchorMin = Vector2.zero;
            il.anchorMax = Vector2.one;
            il.offsetMin = new Vector2(34f, 0f);
            il.offsetMax = new Vector2(-10f, 0f);

            Text itemLabel = itemLabelGo.GetComponent<Text>();
            itemLabel.fontSize = FontSize.Small;
            itemLabel.alignment = TextAnchor.MiddleLeft;
            itemLabel.color = Color.white;
            itemLabel.font = FontUtils.LegacyFont;
            itemLabel.raycastTarget = false;

            Toggle itemToggle = item.GetComponent<Toggle>();
            itemToggle.targetGraphic = itemBg;
            itemToggle.graphic = itemCheck;
            itemToggle.isOn = true;

            ScrollRect tScroll = template.GetComponent<ScrollRect>();
            tScroll.viewport = tvp;
            tScroll.content = tct;
            tScroll.horizontal = false;
            tScroll.vertical = true;
            tScroll.movementType = ScrollRect.MovementType.Clamped;

            Dropdown dropdown = root.GetComponent<Dropdown>();
            dropdown.targetGraphic = bg;
            dropdown.template = tp;
            dropdown.captionText = caption;
            dropdown.itemText = itemLabel;
            dropdown.ClearOptions();
            dropdown.AddOptions(new System.Collections.Generic.List<string>(options));
            dropdown.value = 0;
            dropdown.RefreshShownValue();

            // Unity ships its own dropdown template disabled; left active it would bake the popup into
            // the prefab as a list that is permanently on screen.
            template.SetActive(false);
        }

        private static void StretchCenter(RectTransform rt, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        private static void StretchFill(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        // ---- GameManager wiring ----

        private static void WireGameManager(GameObject mainMenu, GameObject pause, GameObject saveMenu, GameObject settings)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(GameManagerPrefabPath);
            UIManager ui = root.GetComponent<UIManager>();
            if (ui == null) ui = root.AddComponent<UIManager>();
            FpsDisplay fps = root.GetComponent<FpsDisplay>();
            if (fps == null) fps = root.AddComponent<FpsDisplay>();
            GamepadCursor cursor = root.GetComponent<GamepadCursor>();
            if (cursor == null) cursor = root.AddComponent<GamepadCursor>();

            // The fields are typed BasePanel; Unity cannot auto-convert a GameObject reference, so
            // resolve the matching panel component on each prefab before assigning.
            SerializedObject so = new SerializedObject(ui);
            so.FindProperty("mainMenuPrefab").objectReferenceValue = mainMenu.GetComponent<MainMenuPanel>();
            so.FindProperty("pauseMenuPrefab").objectReferenceValue = pause.GetComponent<PausePanel>();
            so.FindProperty("saveMenuPrefab").objectReferenceValue = saveMenu.GetComponent<SaveMenuPanel>();
            so.FindProperty("settingsPrefab").objectReferenceValue = settings.GetComponent<SettingsPanel>();

            // Menu sound slots. Filled only when empty, unlike the panel slots just above: those point
            // at prefabs this builder just regenerated, while a Cue slot may have been repointed by
            // hand at a different asset, and stamping over that every run would undo the change.
            EnsureUiCues(out SoundCue hover, out SoundCue click, out SoundCue toggleOn, out SoundCue toggleOff);
            AssignIfEmpty(so, "hoverCue", hover);
            AssignIfEmpty(so, "clickCue", click);
            AssignIfEmpty(so, "toggleOnCue", toggleOn);
            AssignIfEmpty(so, "toggleOffCue", toggleOff);

            so.ApplyModifiedPropertiesWithoutUndo();

            // The FPS counter is a runtime-created Text, so it cannot pick up the font from a UIBuilder
            // label like the panels do — hand the same font to it through a serialized reference
            // (falls back to the built-in font if never wired, see FpsDisplay).
            Font fpsFont = AssetDatabase.LoadAssetAtPath<Font>(PanelFontPath);
            if (fpsFont != null)
            {
                SerializedObject soFps = new SerializedObject(fps);
                soFps.FindProperty("font").objectReferenceValue = fpsFont;
                soFps.ApplyModifiedPropertiesWithoutUndo();
            }

            // Virtual cursor sprite. Filled only when empty like the Cue slots, so a hand-made
            // assignment survives the next build.
            Sprite aimCursor = LoadAimCursorSprite();
            if (aimCursor != null)
            {
                SerializedObject soCursor = new SerializedObject(cursor);
                AssignIfEmpty(soCursor, "cursorSprite", aimCursor);
                soCursor.ApplyModifiedPropertiesWithoutUndo();
            }

            PrefabUtility.SaveAsPrefabAsset(root, GameManagerPrefabPath);
            PrefabUtility.UnloadPrefabContents(root);
        }

        /// <summary>Fills a serialized object-reference slot only when it is currently empty, so a
        /// hand-made assignment survives the next build. A missing property is skipped rather than
        /// throwing — same "empty slot = silent skip" stance the rest of the project takes.</summary>
        private static void AssignIfEmpty(SerializedObject so, string propertyName, Object value)
        {
            SerializedProperty property = so.FindProperty(propertyName);
            if (property == null) return;
            if (property.objectReferenceValue != null) return;
            property.objectReferenceValue = value;
        }

        /// <summary>Loads the aim_cursor sprite for the gamepad virtual cursor. The png imports as
        /// Multiple, so the sprite is a sub-asset (aim_cursor_0) — LoadAllAssetsAtPath returns the
        /// texture plus its sub-assets, hence the name filter rather than assuming the first is a Sprite.</summary>
        private static Sprite LoadAimCursorSprite()
        {
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(AimCursorPath))
            {
                if (asset is Sprite sprite && sprite.name == "aim_cursor_0")
                    return sprite;
            }
            return null;
        }

        // ---- Menu scene ----

        private static void BuildMenuScene()
        {
            // Building swaps the open scene — ask the user before discarding any unsaved work in the
            // interactive editor. In batch mode (-executeMethod) there is no open scene to lose.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // A bare camera for the menu backdrop (no CamHandler/FxDirector — those need the player).
            GameObject camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            camGo.tag = "MainCamera";
            camGo.transform.position = new Vector3(0f, 0f, -10f);
            camGo.GetComponent<Camera>().orthographic = true;
            camGo.GetComponent<Camera>().orthographicSize = 5f;

            // GameManager carries UIManager (already wired with the panel prefabs).
            GameObject gmPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(GameManagerPrefabPath);
            PrefabUtility.InstantiatePrefab(gmPrefab, scene);

            EditorSceneManager.SaveScene(scene, MenuScenePath);
        }

        /// <summary>
        /// Puts the menu scene at build index 0, exactly once. Separate menu item as well as part of
        /// BuildAll: the build settings are the one piece that can go missing on its own (a merge, a
        /// reverted ProjectSettings file), and re-running the whole builder to fix them would
        /// regenerate every panel prefab and discard hand edits.
        /// </summary>
        [MenuItem("Tools/Inkform/Fix Build Settings")]
        public static void SetBuildSettings()
        {
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>();
            scenes.Add(new EditorBuildSettingsScene(MenuScenePath, true));

            // Drop any existing entry for the menu scene before re-adding it at index 0. Without this,
            // every run prepends another copy — the class docs' "re-running is safe" holds for the
            // prefabs (SaveAsPrefabAsset overwrites in place) but not for this list, which only ever grew.
            foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
            {
                if (s.path == MenuScenePath) continue;
                scenes.Add(s);
            }

            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log($"Build settings: {MenuScenePath} at index 0, {scenes.Count} scene(s) total.");
        }

        // ---- Font helper (legacy uGUI Text needs a Font asset) ----

        /// <summary>UI font asset. Loaded via AssetDatabase (this is an editor tool) with the project's
        /// own font first; falls back to the built-in LegacyRuntime when the file is missing, keeping
        /// the project's "empty slot = silent skip" convention.</summary>
        private const string PanelFontPath = "Assets/Art/UI/BombSlimeFonts.ttf";
        private const string AimCursorPath = "Assets/Art/UI/aim_cursor.png";

        private static class FontUtils
        {
            private static Font cached;
            public static Font LegacyFont
            {
                get
                {
                    if (cached == null)
                    {
                        cached = AssetDatabase.LoadAssetAtPath<Font>(PanelFontPath);
                        if (cached == null) cached = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                        if (cached == null) cached = Resources.GetBuiltinResource<Font>("Arial.ttf");
                    }
                    return cached;
                }
            }
        }
    }
}
