using System;
using System.Collections.Generic;
using Inkform.Audio;
using Inkform.Bus;
using Inkform.Fx;
using Inkform.Input;
using Inkform.Life;
using Inkform.Save;
using Inkform.Settings;
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
    /// across scenes via PersistentGameRoot's DontDestroyOnLoad), owns the one UI Canvas, instantiates all
    /// panel prefabs under it, and routes the pause state machine + cursor + Escape key.
    ///
    /// Panels never talk to each other or to the game: MainMenuPanel asks this class to load a scene,
    /// PausePanel asks it to resume, etc. The game never knows a menu exists.
    ///
    /// Scene policy is owned by the SceneDirector (a sibling component on this same GameManager): it
    /// holds the WorldDefinition asset (menu, entry room, room registry) and answers IsMenuScene /
    /// StartNewGame /
    /// ReturnToMainMenu, so no scene name is duplicated here. The main menu shows on load for the
    /// menu scene; any other scene is gameplay — all panels close on load, Escape opens pause.
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        public static UIManager Instance { get; private set; }

        [Header("Panel prefabs (instantiated under the UI Canvas)")]
        [SerializeField] private BasePanel mainMenuPrefab;
        [SerializeField] private BasePanel pauseMenuPrefab;
        [SerializeField] private BasePanel saveMenuPrefab;
        [SerializeField] private BasePanel settingsPrefab;
        [SerializeField] private BasePanel tutorialPrefab;

        // Menu sounds. Same "event -> cue" mapping AudioDirector does for gameplay, kept here rather
        // than there because these are the menu layer's own feedback and this class already is the
        // menu layer's one gatekeeper. UiButtonFx / UiToggleFx raise the signals; nothing in the UI
        // knows the audio system exists. Leaving a slot empty is legal — AudioManager skips silently.
        [Header("UI sound (cues built by UIBuilder; drop clips into the Cue assets)")]
        [SerializeField] private SoundCue hoverCue;
        [SerializeField] private SoundCue clickCue;
        [SerializeField] private SoundCue toggleOnCue;
        [SerializeField] private SoundCue toggleOffCue;

        private readonly Dictionary<Type, BasePanel> panels = new Dictionary<Type, BasePanel>();
        private InputHandler inputHandler;
        private GameTimeController gameTime;
        private Inkform.Level.SceneDirector sceneDirector;   // sibling on GameManager; owns the scene policy
        private InputSystem_Actions uiActions;   // owned wrapper: the UI module reads the same asset
        private EventSystem ownEventSystem;      // the one we installed; see EnsureEventSystem
        private bool paused;

        public bool IsPaused => paused;
        public bool IsInMainMenu { get; private set; }

        /// <summary>True while any panel is open. GamepadCursor reads this to decide whether to show its
        /// cursor; no panel open (plain gameplay) means no menu to point at.</summary>
        public bool AnyPanelOpen
        {
            get
            {
                foreach (BasePanel panel in panels.Values)
                    if (panel.IsOpen) return true;
                return false;
            }
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            inputHandler = GetComponent<InputHandler>();
            sceneDirector = GetComponent<Inkform.Level.SceneDirector>();
            gameTime = GetComponent<GameTimeController>();
            if (gameTime == null) gameTime = gameObject.AddComponent<GameTimeController>();

            EnsureEventSystem();
            CreateCanvas();
            EnsureGamepadCursor();
            EnsureInventoryHud();
            EnsureSaveIndicator();
            InstantiatePanels();
            // No "close them all" pass here: BasePanel.Awake lands its own hidden state on instantiate.
            // A pass here could never have worked anyway — Close() early-returns while IsOpen is still
            // its default false, which is exactly the bug that left every panel visible.

            // sceneLoaded never fires for the startup scene (same pitfall InputHandler documents), so
            // the first scene's state is applied here directly; later scenes go through OnSceneLoaded.
            ApplySceneState(SceneManager.GetActiveScene());

            SceneManager.sceneLoaded += OnSceneLoaded;

            // Subscribed here rather than in OnEnable so the duplicate-instance guard above has
            // already run — a second UIManager must not spend a frame playing every menu sound twice.
            UiBus.Hovered += OnUiHovered;
            UiBus.Clicked += OnUiClicked;
            UiBus.Toggled += OnUiToggled;

            // A pickup grants an ability and the menu layer answers with its tutorial. Raised here
            // (not on the panel) because panels never touch the game bus — same stance as every
            // other panel; this class is the one place gameplay meets menus.
            ItemBus.AbilityUnlocked += OnAbilityUnlocked;

            // The tutorial is a non-blocking overlay, so the player can take a hit while it is up:
            // a death under the sheet just closes it, and respawn owns the screen from there.
            LifeBus.Died += OnPlayerDied;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            SceneManager.sceneLoaded -= OnSceneLoaded;

            UiBus.Hovered -= OnUiHovered;
            UiBus.Clicked -= OnUiClicked;
            UiBus.Toggled -= OnUiToggled;
            ItemBus.AbilityUnlocked -= OnAbilityUnlocked;
            LifeBus.Died -= OnPlayerDied;
            // No uiActions Dispose: it is the shared InputActions.Wrapper, released by play mode end.
        }

        // ---- UI sound ----

        private void OnUiHovered() => Play(hoverCue);

        private void OnUiClicked() => Play(clickCue);

        // Note the asymmetry the player will hear: turning Mute *on* silences its own confirmation,
        // because SettingsStore.ApplyAudio implements mute as AudioListener.volume = 0. Turning it
        // back off is audible. That is the setting working, not a dropped sound.
        private void OnUiToggled(bool on) => Play(on ? toggleOnCue : toggleOffCue);

        /// <summary>Same guarded one-shot AudioDirector uses. No position: menu sounds are 2D, like
        /// the player's own, so there is nothing for SoundCue.spatial to measure against.</summary>
        private void Play(SoundCue cue)
        {
            if (cue == null) return;                        // slot unconfigured, skip silently
            if (AudioManager.Instance == null) return;      // no AudioManager in the scene yet
            AudioManager.Instance.Play(cue);
        }

        void Update()
        {
            // Pause is read directly rather than via an action in the input asset: adding a Pause
            // action would require regenerating the generated wrapper (the project treats it as
            // hand-off). Esc (keyboard) and Start (gamepad) both back out one sheet; neither is bound
            // to gameplay (the Player map binds no Escape or Start).
            bool keyboard = Keyboard.current != null;
            bool gamepad = Gamepad.current != null;
            if (!keyboard && !gamepad) return;

            // Start on a pad is also the clearest "playing with a pad" signal: flip the input family
            // now, so the gamepad auto-detection does not wait for the first movement input
            if (gamepad && Gamepad.current.startButton.wasPressedThisFrame)
                SettingsStore.SetDevice(SettingsStore.InputDevice.Gamepad);

            bool pausePressed = (keyboard && Keyboard.current.escapeKey.wasPressedThisFrame)
                             || (gamepad && Gamepad.current.startButton.wasPressedThisFrame);
            if (!pausePressed) return;

            // Innermost sheet first, then outwards — Escape always backs out one level
            if (IsOpen<TutorialPanel>()) { CloseTutorial(); return; }
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

        // ---- Tutorial ----

        private void OnAbilityUnlocked(Vector2 position, string abilityId) => OpenTutorial(abilityId);

        /// <summary>
        /// A pickup just granted an ability: show that ability's tutorial as a NON-BLOCKING overlay.
        /// The game keeps running — no pause, no input change; the only thing released is the cursor,
        /// so the sheet's buttons are clickable (it is re-locked on close). Fires only when the
        /// Tutorial panel is wired, its Show On Pickup checkbox is on, and it carries pages for this
        /// ability — any of those missing, the unlock simply happens silently.
        ///
        /// Known trade-off of leaving gameplay input untouched: the click on a tutorial button is
        /// also a gameplay press (left mouse fires the rope gun, gamepad A jumps). One stray shot or
        /// hop per click is the price of the game never stopping.
        /// </summary>
        public void OpenTutorial(string abilityId)
        {
            TutorialPanel panel = GetPanel<TutorialPanel>();
            if (panel == null || !panel.ShowOnPickup || !panel.HasPages(abilityId)) return;

            panel.OpenWith(abilityId);
            panel.transform.SetAsLastSibling();
            SelectFirstControl(panel);
            SetCursor(true);
        }

        /// <summary>Tutorial's Close button / Escape: hide the sheet and lock the cursor back for
        /// gameplay. Idempotent — both the button and the sheet chain can land here in one frame.</summary>
        public void CloseTutorial()
        {
            Close<TutorialPanel>();
            SetCursor(false);
        }

        private void OnPlayerDied(DeathContext ctx)
        {
            if (IsOpen<TutorialPanel>()) CloseTutorial();
        }

        private void SetPaused(bool value)
        {
            paused = value;
            // One of the two GameStateStore writers (the other is SceneDirector): pause/unpause is
            // the only flow transition this class owns. Unpausing returns to whichever side of the
            // menu/gameplay divide the player is on, and ApplySceneState always re-runs SetPaused
            // after assigning IsInMainMenu, so a scene landing settles the state too.
            GameStateStore.Set(value
                ? GameStateStore.GameState.Paused
                : IsInMainMenu ? GameStateStore.GameState.MainMenu
                : GameStateStore.GameState.Playing);
            gameTime?.SetUserPaused(value);
            // sceneLoaded fires before SceneDirector's AsyncOperation continuation. Do not let the
            // new scene's UI state re-enable gameplay during that small but real transition window;
            // SceneDirector restores it once the operation has fully completed.
            bool transitionAllowsInput = sceneDirector == null || !sceneDirector.IsTransitioning;
            inputHandler?.SetPlaying(!value && transitionAllowsInput);
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
            IsInMainMenu = sceneDirector != null && sceneDirector.IsMenuScene(scene.name);

            SetPaused(false);
            Close<PausePanel>();
            Close<SettingsPanel>();
            Close<SaveMenuPanel>();
            Close<TutorialPanel>();

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

            GetComponent<InventoryHud>()?.RefreshVisibility();
        }

        /// <summary>
        /// Starts a fresh run in a save slot, discarding whatever that slot held. Callback for the Save
        /// menu's empty slots, and for a slot the player confirmed overwriting.
        /// </summary>
        public void StartNewGame(int slot)
        {
            SetPaused(false);

            if (sceneDirector == null)
            {
                Debug.LogWarning("UIManager: no SceneDirector on the GameManager — cannot start a new game", this);
                return;
            }

            if (!sceneDirector.CanStartNewGame(out string reason))
            {
                Debug.LogWarning($"UIManager: cannot start a new game — {reason}", this);
                return;
            }

            // Validation must precede BeginNewRun: a broken graph may not erase an occupied slot.
            SaveStore.BeginNewRun(slot);
            if (!sceneDirector.StartNewGame()) SaveStore.AbortNewRun();
        }

        /// <summary>Resumes the run held in a save slot. Callback for the Save menu's occupied slots.
        /// A slot that turns out to be empty falls through to a fresh run inside SceneDirector.</summary>
        public void ContinueGame(int slot)
        {
            SetPaused(false);

            if (sceneDirector == null)
            {
                Debug.LogWarning("UIManager: no SceneDirector on the GameManager — cannot continue a saved run", this);
                return;
            }

            SaveData save = SaveStore.Get(slot);
            if (!sceneDirector.CanContinueGame(save, out string reason))
            {
                Debug.LogWarning($"UIManager: cannot continue this save — {reason}", this);
                return;
            }

            SaveStore.ContinueRun(slot);
            if (!sceneDirector.ContinueGame(save)) SaveStore.EndRun();
        }

        /// <summary>The display name of the level a save file names, for the save menu's slot rows.
        /// Routed through here rather than read from the level graph directly because panels never
        /// touch the game layer (see the class docs); falls back to the raw scene name.</summary>
        public string LevelDisplayName(string sceneName)
        {
            return sceneDirector != null ? sceneDirector.DisplayNameOf(sceneName) : sceneName;
        }

        /// <summary>
        /// Pause menu's "Save &amp; Quit": saves and returns to the main menu. The saving happens inside
        /// SceneDirector.ReturnToMainMenu (SaveStore.EndRun), so the dead-end path back to the menu
        /// gets it too — the autosave has already recorded the position, and closing the run is what
        /// brings its play time and death count up to date.
        /// </summary>
        public void QuitToMainMenu()
        {
            SetPaused(false);

            if (sceneDirector == null)
            {
                Debug.LogWarning("UIManager: no SceneDirector on the GameManager — cannot return to the main menu", this);
                return;
            }
            sceneDirector.ReturnToMainMenu();
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

        /// <summary>
        /// Controls tab's "Unstuck": teleports the player back to the last checkpoint and hands control
        /// straight back, for when a bug wedges them somewhere they cannot leave. Routed through here
        /// rather than called from the panel because panels never touch the game (see the class docs) —
        /// and because the RespawnDirector is a sibling component on this same GameManager.
        ///
        /// Does nothing in the menu scene, where there is no player. The panel greys the button out
        /// there; this guard is the one that matters, because the Resume below would otherwise hide
        /// and lock the cursor over a menu nobody could then click.
        /// </summary>
        public void Unstuck()
        {
            if (IsInMainMenu) return;

            GetComponent<Inkform.Level.RespawnDirector>()?.RespawnNow();
            Close<SettingsPanel>();
            Resume();
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
            AddPanel(tutorialPrefab);
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
            SelectFirstControl(panel);
        }

        /// <summary>
        /// Puts keyboard/gamepad focus on the panel's first usable control. Without it nothing is ever
        /// selected — every Selectable in the built prefabs uses Automatic navigation, but Automatic
        /// only decides where focus moves *next*, not where it starts, so a controller player saw no
        /// highlight at all until they happened to touch the mouse.
        ///
        /// Done here instead of per panel: one call covers all four sheets, and Open is already the
        /// single place a panel becomes visible. GetComponentsInChildren finds them in hierarchy order,
        /// which is the order UIBuilder adds them, so "first" means the top control on the sheet.
        /// </summary>
        private void SelectFirstControl(BasePanel panel)
        {
            if (EventSystem.current == null) return;

            Selectable[] controls = panel.GetComponentsInChildren<Selectable>(false);
            foreach (Selectable control in controls)
            {
                if (!control.IsInteractable()) continue;     // skip the selected tab and dead placeholders
                EventSystem.current.SetSelectedGameObject(control.gameObject);
                return;
            }

            // A sheet with nothing usable should not keep the previous sheet's focus alive underneath.
            EventSystem.current.SetSelectedGameObject(null);
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

            // The whole UI is resolution-adaptive through this one scaler — no prefab or scene in the
            // project carries a CanvasScaler of its own, so these four lines are the entire policy.
            //
            // screenMatchMode is written out rather than left to its default, because the default is
            // what the match value below is meaningless without. At match 0.5 the scale splits the
            // difference between the width and height ratios, which keeps both axes off the reference
            // frame's edges rather than guaranteeing either: the widest content is the save-slot row
            // at 1580 of 1920 (340 to spare) and the tallest is the settings sheet at roughly 980 of
            // 1080 — so the vertical margin is the tighter one, and UIBuilder pulls the settings
            // sheet's top and bottom rows in to buy some of it back.
            CanvasScaler scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
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
            uiActions = InputActions.Wrapper;   // shared with InputHandler — one asset, one set of remaps
            module.actionsAsset = uiActions.asset;
        }

        private void SetCursor(bool visible)
        {
            // The unified cursor renders the exact pointer position for both mouse and gamepad.
            Cursor.visible = false;
            Cursor.lockState = visible ? CursorLockMode.None : CursorLockMode.Locked;
        }

        /// <summary>Idempotent self-install of the gamepad virtual cursor, same as EnsureEventSystem.
        /// UIBuilder also adds the component (to wire the aim_cursor sprite); this guarantee means the
        /// feature works even before that build has run, using the generated disc fallback.</summary>
        private void EnsureGamepadCursor()
        {
            if (GetComponent<GamepadCursor>() == null)
                gameObject.AddComponent<GamepadCursor>();
        }

        private void EnsureInventoryHud()
        {
            if (GetComponent<InventoryHud>() == null)
                gameObject.AddComponent<InventoryHud>();
        }

        private void EnsureSaveIndicator()
        {
            if (GetComponent<SaveIndicator>() == null)
                gameObject.AddComponent<SaveIndicator>();
        }
    }
}
