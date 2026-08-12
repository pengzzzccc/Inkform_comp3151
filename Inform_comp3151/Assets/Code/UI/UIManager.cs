using System;
using System.Collections.Generic;
using Inkform.Input;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Inkform.UI
{
    /// <summary>
    /// UI manager: the single gatekeeper for the menu layer. Lives on GameManager (which persists
    /// across scenes via AudioManager's DontDestroyOnLoad), owns the one UI Canvas, instantiates all
    /// panel prefabs under it, and routes the pause state machine + cursor + Escape key.
    ///
    /// Panels never talk to each other or to the game: MainMenuPanel asks this class to load a scene,
    /// PausePanel asks it to resume, etc. The game never knows a menu exists.
    ///
    /// Scene policy: a scene whose name equals mainMenuSceneName is "the menu scene" — the main menu
    /// shows on load. Any other scene is gameplay — all panels close on load, Escape opens pause.
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        public static UIManager Instance { get; private set; }

        [Header("Panel prefabs (instantiated under the UI Canvas)")]
        [SerializeField] private BasePanel mainMenuPrefab;
        [SerializeField] private BasePanel pauseMenuPrefab;
        [SerializeField] private BasePanel saveMenuPrefab;
        [SerializeField] private BasePanel settingsPrefab;

        [Header("Scene names (must match Build Settings exactly)")]
        [SerializeField] private string mainMenuSceneName = "MainMenu";
        [SerializeField] private string gameSceneName = "Level1";

        private readonly Dictionary<Type, BasePanel> panels = new Dictionary<Type, BasePanel>();
        private InputHandler inputHandler;
        private InputSystem_Actions uiActions;   // owned wrapper: the UI module reads the same asset
        private EventSystem ownEventSystem;      // the one we installed; see EnsureEventSystem
        private bool paused;

        public bool IsPaused => paused;
        public bool IsInMainMenu { get; private set; }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            inputHandler = GetComponent<InputHandler>();

            EnsureEventSystem();
            CreateCanvas();
            InstantiatePanels();
            // No "close them all" pass here: BasePanel.Awake lands its own hidden state on instantiate.
            // A pass here could never have worked anyway — Close() early-returns while IsOpen is still
            // its default false, which is exactly the bug that left every panel visible.

            // sceneLoaded never fires for the startup scene (same pitfall InputHandler documents), so
            // the first scene's state is applied here directly; later scenes go through OnSceneLoaded.
            ApplySceneState(SceneManager.GetActiveScene());

            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            uiActions?.Dispose();
        }

        void Update()
        {
            // Escape is a system-level UI key, intentionally read directly rather than via an action
            // in the input asset: adding a Pause action would require regenerating the generated
            // wrapper (the project treats it as hand-off). This must not run while the pause key is
            // also bound to gameplay — it is not (the asset's Player map binds no Escape).
            if (Keyboard.current == null) return;
            if (!Keyboard.current.escapeKey.wasPressedThisFrame) return;

            // Innermost sheet first, then outwards — Escape always backs out one level
            if (IsOpen<SettingsPanel>()) { CloseSettings(); return; }
            if (IsOpen<SaveMenuPanel>()) { Close<SaveMenuPanel>(); return; }

            if (IsInMainMenu) return;   // menu root: nothing left to back out of
            if (paused) Resume();
            else OpenPause();
        }

        // ---- Pause state machine ----

        /// <summary>Pauses gameplay: freezes time, disables player input, shows the pause panel.</summary>
        public void OpenPause()
        {
            SetPaused(true);
            Open<PausePanel>();
            SetCursor(true);
        }

        /// <summary>Un-pauses and returns to the game.</summary>
        public void Resume()
        {
            SetPaused(false);
            Close<PausePanel>();
            SetCursor(false);
        }

        private void SetPaused(bool value)
        {
            if (paused == value) return;
            paused = value;
            Time.timeScale = value ? 0f : 1f;
            inputHandler?.SetPlaying(!value);
        }

        // ---- Navigation ----

        /// <summary>Menu scene loaded: show the main menu (Save menu / settings stay closed).</summary>
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            EnsureEventSystem();    // idempotent; re-asserts ours if the new scene brought its own
            ApplySceneState(scene);
        }

        private void ApplySceneState(Scene scene)
        {
            // Panels live under the persistent UI Canvas, so scene switches never need rebuilding.
            IsInMainMenu = scene.name == mainMenuSceneName;

            SetPaused(false);
            Close<PausePanel>();
            Close<SettingsPanel>();
            Close<SaveMenuPanel>();

            if (IsInMainMenu)
            {
                Open<MainMenuPanel>();
                SetCursor(true);
            }
            else
            {
                Close<MainMenuPanel>();
                SetCursor(false);
            }
        }

        /// <summary>
        /// Starts a fresh run. Callback for the Save menu's slot buttons — every slot lands here
        /// because there is no save system yet; when there is, loading a slot becomes a different call.
        /// </summary>
        public void StartNewGame()
        {
            SetPaused(false);
            SceneManager.LoadScene(gameSceneName);
        }

        /// <summary>
        /// Pause menu's "Save &amp; Quit": returns to the main menu. **Saves nothing** — no save system
        /// exists yet; the button's label is aspirational and the run is lost.
        /// </summary>
        public void QuitToMainMenu()
        {
            SetPaused(false);
            SceneManager.LoadScene(mainMenuSceneName);
        }

        public void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        public void OpenSettings()
        {
            Open<SettingsPanel>();
        }

        /// <summary>Settings' Back: close it and restore the sheet underneath (pause or main menu).</summary>
        public void CloseSettings()
        {
            Close<SettingsPanel>();
            if (!IsInMainMenu && paused) Open<PausePanel>();
        }

        // ---- Panel plumbing ----

        private void InstantiatePanels()
        {
            AddPanel(mainMenuPrefab);
            AddPanel(pauseMenuPrefab);
            AddPanel(saveMenuPrefab);
            AddPanel(settingsPrefab);
        }

        private void AddPanel(BasePanel prefab)
        {
            if (prefab == null) { Debug.LogWarning($"UIManager: unassigned panel prefab slot (type {typeof(BasePanel).Name})", this); return; }
            BasePanel panel = Instantiate(prefab, uiCanvas.transform);
            panel.name = prefab.name;
            panels[prefab.GetType()] = panel;
        }

        public T GetPanel<T>() where T : BasePanel
        {
            panels.TryGetValue(typeof(T), out BasePanel panel);
            return panel as T;
        }

        /// <summary>
        /// Opens a panel and raises it above every other sheet. The explicit SetAsLastSibling matters:
        /// uGUI draws in hierarchy order, so before this the settings sheet only covered the pause sheet
        /// because InstantiatePanels happened to add it last — reordering that method would have
        /// silently put the wrong sheet on top.
        /// </summary>
        public void Open<T>() where T : BasePanel
        {
            T panel = GetPanel<T>();
            if (panel == null) return;

            panel.Open();
            panel.transform.SetAsLastSibling();
        }

        public void Close<T>() where T : BasePanel => GetPanel<T>()?.Close();
        public bool IsOpen<T>() where T : BasePanel => GetPanel<T>()?.IsOpen ?? false;

        // ---- Infrastructure ----

        private Canvas uiCanvas;

        private void CreateCanvas()
        {
            GameObject go = new GameObject("UI Canvas");
            go.transform.SetParent(transform);

            uiCanvas = go.AddComponent<Canvas>();
            uiCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            uiCanvas.sortingOrder = 100;

            CanvasScaler scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            go.AddComponent<GraphicRaycaster>();
        }

        /// <summary>
        /// Creates an EventSystem with an InputSystemUIInputModule wired to the project's input asset —
        /// lazily, and at most once. Gameplay scenes never needed one, but the pause menu must be
        /// clickable everywhere, so this self-installs without touching any scene file.
        ///
        /// Guarded on **our own** instance rather than EventSystem.current: a scene that ships its own
        /// EventSystem (Unity adds one automatically the moment anyone creates a UI object in it) would
        /// otherwise satisfy the guard while carrying no wiring to this project's input asset, and the
        /// menu would go completely unclickable — a symptom nearly identical to a panel-visibility bug
        /// and just as hard to trace. Ours is parented to the persistent GameManager, so it stays alive
        /// across scenes and keeps priority.
        /// </summary>
        private void EnsureEventSystem()
        {
            if (ownEventSystem != null) return;

            GameObject go = new GameObject("EventSystem");
            go.transform.SetParent(transform);

            ownEventSystem = go.AddComponent<EventSystem>();
            InputSystemUIInputModule module = go.AddComponent<InputSystemUIInputModule>();
            uiActions = new InputSystem_Actions();
            module.actionsAsset = uiActions.asset;
        }

        private void SetCursor(bool visible)
        {
            Cursor.visible = visible;
            Cursor.lockState = visible ? CursorLockMode.None : CursorLockMode.Locked;
        }
    }
}
