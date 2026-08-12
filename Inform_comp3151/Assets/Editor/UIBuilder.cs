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
    /// The generated prefabs are plain uGUI structures (CanvasGroup root + named Buttons). All button
    /// wiring happens at runtime by the panel scripts finding children by name — the builder only
    /// needs to name them correctly. Re-running is safe (it overwrites in place).
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

        private const string MenuSceneName = "MainMenu";
        private const string GameSceneName = "Level1";

        [MenuItem("Tools/Inkform/Build UI")]
        public static void BuildAll()
        {
            EnsureFolders();

            GameObject mainMenu = BuildPanelPrefab("MainMenu", typeof(MainMenuPanel), panel =>
            {
                AddTitle(panel, "Bomb Slime", 200f);
                AddButton(panel, "Btn_Play", "Play", 60f);
                AddButton(panel, "Btn_Settings", "Settings", -40f);
                AddButton(panel, "Btn_Exit", "Exit", -140f);
            });

            GameObject pause = BuildPanelPrefab("PauseMenu", typeof(PausePanel), panel =>
            {
                AddTitle(panel, "Paused", 200f);
                AddButton(panel, "Btn_Resume", "Resume", 60f);
                AddButton(panel, "Btn_Settings", "Settings", -40f);
                AddButton(panel, "Btn_SaveAndQuit", "Save & Quit", -140f);
            });

            GameObject saveMenu = BuildPanelPrefab("SaveMenu", typeof(SaveMenuPanel), panel =>
            {
                AddTitle(panel, "Save Menu", 220f);
                for (int i = 0; i < 3; i++)
                    AddButton(panel, $"Btn_Slot{i}", $"Slot {i}\nNo Records", 60f - i * 90f);
                AddButton(panel, "Btn_Left", "<", new Vector2(-260f, 10f), new Vector2(60f, 60f));
                AddButton(panel, "Btn_Right", ">", new Vector2(260f, 10f), new Vector2(60f, 60f));
                AddButton(panel, "Btn_Back", "Back", -240f);
            });

            GameObject settings = BuildPanelPrefab("Settings", typeof(SettingsPanel), panel =>
            {
                AddButton(panel, "Btn_TabSound", "Sound", new Vector2(-560f, 120f), new Vector2(260f, 50f));
                AddButton(panel, "Btn_TabGraphics", "Graphics", new Vector2(-560f, 50f), new Vector2(260f, 50f));
                AddButton(panel, "Btn_TabControls", "Controls", new Vector2(-560f, -20f), new Vector2(260f, 50f));
                AddButton(panel, "Btn_Reset", "Reset Settings", new Vector2(-560f, -90f), new Vector2(260f, 50f));

                AddContentPane(panel, "Content_Sound");
                AddContentPane(panel, "Content_Graphics");
                AddContentPane(panel, "Content_Controls");

                AddButton(panel, "Btn_Back", "Back", -240f);
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
        }

        private static GameObject BuildPanelPrefab(string prefabName, System.Type scriptType, System.Action<GameObject> build)
        {
            GameObject root = new GameObject(prefabName, typeof(RectTransform));
            root.AddComponent<CanvasGroup>();
            root.AddComponent(scriptType);

            // Full-screen opaque background so panels fully cover the frozen game frame.
            Image bg = root.AddComponent<Image>();
            bg.color = new Color(0.05f, 0.06f, 0.10f, 0.92f);

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

        private static void AddTitle(GameObject panel, string text, float y)
        {
            GameObject go = new GameObject("Title", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(panel.transform, false);

            RectTransform rt = go.GetComponent<RectTransform>();
            StretchCenter(rt, new Vector2(0f, y), new Vector2(800f, 120f));

            Text label = go.GetComponent<Text>();
            label.text = text;
            label.fontSize = 64;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            label.font = FontUtils.LegacyFont;
        }

        private static void AddButton(GameObject panel, string buttonName, string labelText, float y)
            => AddButton(panel, buttonName, labelText, new Vector2(0f, y), new Vector2(360f, 70f));

        private static void AddButton(GameObject panel, string buttonName, string labelText, Vector2 pos, Vector2 size)
        {
            GameObject go = new GameObject(buttonName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.transform.SetParent(panel.transform, false);

            RectTransform rt = go.GetComponent<RectTransform>();
            StretchCenter(rt, pos, size);

            Image img = go.GetComponent<Image>();
            img.color = new Color(0.22f, 0.26f, 0.36f, 1f);

            Button button = go.GetComponent<Button>();
            button.targetGraphic = img;

            // Button label
            GameObject textGo = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            textGo.transform.SetParent(go.transform, false);
            StretchFill(textGo.GetComponent<RectTransform>());

            Text label = textGo.GetComponent<Text>();
            label.text = labelText;
            label.fontSize = 28;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            label.font = FontUtils.LegacyFont;
        }

        private static void AddContentPane(GameObject panel, string contentName)
        {
            GameObject go = new GameObject(contentName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(panel.transform, false);

            RectTransform rt = go.GetComponent<RectTransform>();
            StretchCenter(rt, new Vector2(280f, 30f), new Vector2(760f, 500f));

            Image img = go.GetComponent<Image>();
            img.color = new Color(0.14f, 0.16f, 0.22f, 0.9f);

            // Placeholder text: content sheets are stubs until real settings land.
            GameObject textGo = new GameObject("Placeholder", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            textGo.transform.SetParent(go.transform, false);
            StretchFill(textGo.GetComponent<RectTransform>());

            Text label = textGo.GetComponent<Text>();
            label.text = contentName.Replace("Content_", "") + " settings";
            label.fontSize = 26;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = new Color(0.7f, 0.7f, 0.7f, 1f);
            label.font = FontUtils.LegacyFont;
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

            // The fields are typed BasePanel; Unity cannot auto-convert a GameObject reference, so
            // resolve the matching panel component on each prefab before assigning.
            SerializedObject so = new SerializedObject(ui);
            so.FindProperty("mainMenuPrefab").objectReferenceValue = mainMenu.GetComponent<MainMenuPanel>();
            so.FindProperty("pauseMenuPrefab").objectReferenceValue = pause.GetComponent<PausePanel>();
            so.FindProperty("saveMenuPrefab").objectReferenceValue = saveMenu.GetComponent<SaveMenuPanel>();
            so.FindProperty("settingsPrefab").objectReferenceValue = settings.GetComponent<SettingsPanel>();
            so.FindProperty("mainMenuSceneName").stringValue = MenuSceneName;
            so.FindProperty("gameSceneName").stringValue = GameSceneName;
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, GameManagerPrefabPath);
            PrefabUtility.UnloadPrefabContents(root);
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

        private static class FontUtils
        {
            private static Font cached;
            public static Font LegacyFont
            {
                get
                {
                    if (cached == null)
                    {
                        cached = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                        if (cached == null) cached = Resources.GetBuiltinResource<Font>("Arial.ttf");
                    }
                    return cached;
                }
            }
        }
    }
}
