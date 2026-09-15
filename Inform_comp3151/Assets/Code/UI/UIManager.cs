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
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Inkform.UI
{
    /// <summary>
    /// UI manager: the single gatekeeper for the menu layer. Lives on GameManager (which persists
    /// across scenes via PersistentGameRoot's DontDestroyOnLoad), owns the UI Toolkit document
    /// (UXML sheets + USS theme) and the gameplay HUD, and routes the pause state machine +
    /// Escape key.
    ///
    /// The old uGUI stack (UI Canvas, EventSystem, panel prefabs, GamepadCursor) is gone: sheets
    /// are UXML templates under Resources/UI, styled by Theme.uss, hosted in one UIDocument with
    /// a #hud layer under a #menus layer. Gamepad/keyboard menu input is the Toolkit's own focus
    /// navigation (Celeste's way) — panels focus their first control on open, no virtual cursor.
    ///
    /// Panels never talk to each other or to the game: MainMenuPanel asks this class to load a
    /// scene, PausePanel asks it to resume, etc. The game never knows a menu exists.
    ///
    /// Scene policy is owned by the SceneDirector (a sibling component on this same GameManager):
    /// it holds the WorldDefinition asset (menu, entry room, room registry) and answers IsMenuScene /
    /// StartNewGame / ReturnToMainMenu, so no scene name is duplicated here. The main menu shows
    /// on load for the menu scene; any other scene is gameplay — all panels close on load, Escape
    /// opens pause.
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        public static UIManager Instance { get; private set; }

        // Menu sounds. Same "event -> cue" mapping AudioDirector does for gameplay, kept here
        // rather than there because these are the menu layer's own feedback and this class
        // already is the menu layer's one gatekeeper. Toolkit controls raise the UiBus signals
        // (ToolkitPanel.Bind / OptionRow); nothing in the UI knows the audio system exists.
        // Leaving a slot empty is legal — AudioManager skips silently.
        [Header("UI sound (drop clips into the Cue assets)")]
        [SerializeField] private SoundCue hoverCue;
        [SerializeField] private SoundCue clickCue;
        [SerializeField] private SoundCue toggleOnCue;
        [SerializeField] private SoundCue toggleOffCue;

        private readonly Dictionary<Type, ToolkitPanel> panels = new Dictionary<Type, ToolkitPanel>();
        private InputHandler inputHandler;
        private GameTimeController gameTime;
        private Inkform.Level.SceneDirector sceneDirector;   // sibling on GameManager; owns the scene policy
        private UIDocument uiDocument;
        private VisualElement menusRoot;
        private ToolkitLockOwner tutorialLockOwner;   // scoped-lock owner the tutorial sheet locks with
        private Hud hud;
        private bool paused;

        /// <summary>Real time the pause sheet was last opened. Resume is suppressed for a short
        /// grace window after it, so an input landing on the fresh sheet (the centre-locked
        /// cursor's stray click, a double-Esc) cannot instantly close what just opened.</summary>
        private float pauseOpenedAtRealtime = -10f;

        private const float PauseCloseGraceSeconds = 0.3f;

        public bool IsPaused => paused;
        public bool IsInMainMenu { get; private set; }

        /// <summary>True while any panel is open. Nothing else needs to know about the menus.</summary>
        public bool AnyPanelOpen
        {
            get
            {
                foreach (ToolkitPanel panel in panels.Values)
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
            // The tutorial's scoped locks key their owner by UnityEngine.Object reference; the
            // sheet itself is a plain object, so it locks through this dedicated component.
            tutorialLockOwner = gameObject.AddComponent<ToolkitLockOwner>();

            CreateDocument();
            CreateHud();
            CreatePanels();
            // Panels land hidden; a scene-landing pass below opens whichever sheet the scene wants.

            // sceneLoaded never fires for the startup scene (same pitfall InputHandler documents),
            // so the first scene's state is applied here directly; later scenes go through OnSceneLoaded.
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

            // The tutorial freezes the world while it is up, so a death under the sheet should not
            // happen — kept as a safety net for any damage path that ignores the freeze
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

            // Where the old MonoBehaviours' OnDestroy/OnDisable released static subscriptions and
            // scoped locks (save-menu refresh, tutorial stage hold, HUD toasts). Destroying the
            // lock owner additionally lets the lock tables' dead-owner prune sweep anything left.
            foreach (ToolkitPanel panel in panels.Values)
                panel.Teardown();
            hud?.Dispose();
            if (tutorialLockOwner != null) Destroy(tutorialLockOwner);
        }

        void Update()
        {
            // Drive the sheets' per-frame logic and the HUD off one loop, unscaled for the menus
            // (they must run while timeScale is 0) and scaled for the timer, whose rules live in Hud.
            float unscaled = Time.unscaledDeltaTime;
            foreach (ToolkitPanel panel in panels.Values)
                panel.Tick(unscaled);
            hud?.Tick(Time.deltaTime, unscaled);

            // Pause is read directly rather than via an action in the input asset: adding a Pause
            // action would require regenerating the generated wrapper (the project treats it as
            // hand-off). Esc (keyboard) and Start (gamepad) both back out one sheet; neither is
            // bound to gameplay (the Player map binds no Escape or Start).
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

            // The scene fader/RoomIntro owns input until the new room has handed gameplay back.
            if (sceneDirector != null && sceneDirector.IsTransitioning) return;

            // Innermost sheet first, then outwards — Escape always backs out one level
            if (IsOpen<TutorialPanel>()) { CloseTutorial(); return; }
            if (IsOpen<SettingsPanel>()) { CloseSettings(); return; }
            if (IsOpen<SaveMenuPanel>()) { Close<SaveMenuPanel>(); return; }
            // The end sheet has no inner level: Escape leaves the finished run for the main menu
            if (IsOpen<EndPanel>()) { ReturnToMainMenu(); return; }

            if (IsInMainMenu) return;   // menu root: nothing left to back out of
            if (paused) Resume();
            else OpenPause();
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

        // ---- Pause state machine ----

        /// <summary>Pauses gameplay: freezes time, disables player input, shows the pause panel.</summary>
        public void OpenPause()
        {
            SetPaused(true);
            pauseOpenedAtRealtime = Time.unscaledTime;
            Open<PausePanel>();
            SetCursor(true);
        }

        /// <summary>Un-pauses and returns to the game.</summary>
        public void Resume()
        {
            // Input grace right after opening: an Esc echo or a stray click landing on the fresh
            // sheet must not instantly close what just opened (it reads as the menu "flashing").
            // TEMPORARY log — confirms the diagnosis, remove once the flash is confirmed gone.
            if (paused && Time.unscaledTime - pauseOpenedAtRealtime < PauseCloseGraceSeconds)
            {
                Debug.Log($"[UIManager] Resume suppressed {Time.unscaledTime - pauseOpenedAtRealtime:0.00}s after open (input grace)", this);
                return;
            }

            SetPaused(false);
            Close<PausePanel>();
            SetCursor(false);
        }

        // ---- Tutorial ----

        private void OnAbilityUnlocked(Vector2 position, string abilityId) => OpenTutorial(abilityId);

        /// <summary>
        /// A pickup just granted an ability: show that ability's tutorial sheet and free the
        /// cursor. The sheet itself holds gameplay still while it is up — TutorialPanel.OnOpen
        /// takes a scoped gameplay-input lock and freezes the world, both released on every close
        /// path — so the click that flips a page no longer fires the rope gun or jumps, and
        /// nothing can hit the player mid-read.
        /// Fires only when the tutorial content is wired, its Show On Pickup flag is on, and it
        /// carries pages for this ability — any of those missing, the unlock simply happens
        /// silently.
        /// </summary>
        public void OpenTutorial(string abilityId)
        {
            TutorialPanel panel = GetPanel<TutorialPanel>();
            if (panel == null || !panel.ShowOnPickup || !panel.HasPages(abilityId)) return;

            panel.OpenWith(abilityId);
            SetCursor(true);
        }

        /// <summary>Tutorial's Close button / Escape: hide the sheet and lock the cursor back for
        /// gameplay. Idempotent — both the button and the sheet chain can land here in one frame.</summary>
        public void CloseTutorial()
        {
            Close<TutorialPanel>();
            SetCursor(false);
        }

        /// <summary>End sheet's Back button and Escape: leaves the finished run and returns to the
        /// main menu. SceneDirector owns the transition (and ends the run on the way out).</summary>
        public void ReturnToMainMenu()
        {
            Close<EndPanel>();
            SetCursor(false);
            sceneDirector?.ReturnToMainMenu();
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
            bool transitioning = sceneDirector != null && sceneDirector.IsTransitioning;
            GameStateStore.Set(value
                ? GameStateStore.GameState.Paused
                : transitioning ? GameStateStore.GameState.Transition
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
            ApplySceneState(scene);
        }

        private void ApplySceneState(Scene scene)
        {
            // Panels live in the persistent document, so scene switches never need rebuilding.
            IsInMainMenu = sceneDirector != null && sceneDirector.IsMenuScene(scene.name);
            bool isEnd = sceneDirector != null && sceneDirector.IsEndScene(scene.name);

            SetPaused(false);
            Close<PausePanel>();
            Close<SettingsPanel>();
            Close<SaveMenuPanel>();
            Close<TutorialPanel>();

            if (isEnd)
            {
                // The summary sheet replaces both the menu and the HUD: the end scene has no gameplay
                Close<MainMenuPanel>();
                Open<EndPanel>();
                SetCursor(true);
            }
            else if (IsInMainMenu)
            {
                Close<EndPanel>();
                Open<MainMenuPanel>();
                SetCursor(true);
            }
            else
            {
                Close<EndPanel>();
                Close<MainMenuPanel>();
                SetCursor(false);
            }

            bool gameplay = !IsInMainMenu && !isEnd;
            if (gameplay) hud?.ResetLevelTimer();
            hud?.SetGameplayVisible(gameplay);
        }

        /// <summary>
        /// Starts a fresh run in a save slot, discarding whatever that slot held. Callback for the
        /// Save menu's empty slots, and for a slot the player confirmed overwriting.
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

        /// <summary>Resumes the run held in a save slot. Callback for the Save menu's occupied
        /// slots. A slot that turns out to be empty falls through to a fresh run inside
        /// SceneDirector.</summary>
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

        /// <summary>The display name of the level a save file names, for the save menu's slot
        /// rows. Routed through here rather than read from the level graph directly because panels
        /// never touch the game layer (see the class docs); falls back to the raw scene name.</summary>
        public string LevelDisplayName(string sceneName)
        {
            return sceneDirector != null ? sceneDirector.DisplayNameOf(sceneName) : sceneName;
        }

        /// <summary>
        /// Pause menu's "Save &amp; Quit": saves and returns to the main menu. The saving happens
        /// inside SceneDirector.ReturnToMainMenu (SaveStore.EndRun), so the dead-end path back to
        /// the menu gets it too — the autosave has already recorded the position, and closing the
        /// run is what brings its play time and death count up to date.
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
        /// Controls tab's "Unstuck": teleports the player back to the last checkpoint and hands
        /// control straight back, for when a bug wedges them somewhere they cannot leave. Routed
        /// through here rather than called from the panel because panels never touch the game
        /// (see the class docs) — and because the RespawnDirector is a sibling component on this
        /// same GameManager.
        ///
        /// Does nothing in the menu scene, where there is no player. The panel greys the row out
        /// there; this guard is the one that matters, because the Resume below would otherwise
        /// hide and lock the cursor over a menu nobody could then click.
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

        private void CreatePanels()
        {
            AddPanel<MainMenuPanel>("UI/MainMenu", host => new MainMenuPanel(host, this));
            AddPanel<SaveMenuPanel>("UI/SaveMenu", host => new SaveMenuPanel(host, this));
            AddPanel<PausePanel>("UI/PauseMenu", host => new PausePanel(host, this));
            AddPanel<SettingsPanel>("UI/Settings", host => new SettingsPanel(host, this));
            AddPanel<TutorialPanel>("UI/Tutorial", host => new TutorialPanel(
                host, this,
                Resources.Load<TutorialPages>("UI/TutorialPages"),
                GetComponent<InputHandler>(),
                gameTime,
                tutorialLockOwner));
            AddPanel<EndPanel>("UI/EndPanel", host => new EndPanel(host, this));
        }

        private void AddPanel<TPanel>(string resource, Func<VisualElement, TPanel> create)
            where TPanel : ToolkitPanel
        {
            VisualTreeAsset tree = Resources.Load<VisualTreeAsset>(resource);
            if (tree == null)
            {
                Debug.LogWarning($"UIManager: missing UXML at Resources/{resource} — {typeof(TPanel).Name} is unavailable", this);
                return;
            }

            // tree.Instantiate hands back a TemplateContainer that does not stretch on its own;
            // pin it to the menus layer so the sheet's own .screen root fills the screen.
            VisualElement host = tree.Instantiate();
            host.style.position = Position.Absolute;
            host.style.left = 0f;
            host.style.top = 0f;
            host.style.right = 0f;
            host.style.bottom = 0f;
            menusRoot.Add(host);

            panels[typeof(TPanel)] = create(host);
        }

        public T GetPanel<T>() where T : ToolkitPanel
        {
            panels.TryGetValue(typeof(T), out ToolkitPanel panel);
            return panel as T;
        }

        /// <summary>Opens a panel: it raises itself above every other sheet and focuses its first
        /// control (ToolkitPanel.Open), standing in for the old SetAsLastSibling +
        /// SelectFirstControl pair.</summary>
        public void Open<T>() where T : ToolkitPanel => GetPanel<T>()?.Open();

        public void Close<T>() where T : ToolkitPanel => GetPanel<T>()?.Close();

        public bool IsOpen<T>() where T : ToolkitPanel => GetPanel<T>()?.IsOpen ?? false;

        // ---- Infrastructure ----

        /// <summary>
        /// Builds the UI Toolkit document: one UIDocument hosting a #hud layer under a #menus
        /// layer (later siblings draw on top, so open sheets cover the HUD). Replaces the whole
        /// old uGUI stack — Canvas + CanvasScaler + GraphicRaycaster + self-installed EventSystem.
        /// Panel settings come from Resources (generated by Tools > Inkform > UIToolkit
        /// Bootstrap); whatever the source, the scale policy below is enforced on the instance
        /// actually handed to the document, so a stale or hand-edited asset can never shrink the
        /// UI back into a corner — this is the CanvasScaler policy the old uGUI stack carried.
        /// </summary>
        private void CreateDocument()
        {
            // Inactive while the component is added and configured: OnEnable must see a complete
            // panelSettings, not null. A document enabled with no settings and configured later
            // ends up half-attached (root stays width x 0, styles never apply).
            GameObject go = new GameObject("UI Document");
            go.transform.SetParent(transform, false);
            go.SetActive(false);
            uiDocument = go.AddComponent<UIDocument>();

            PanelSettings settings = Resources.Load<PanelSettings>("UI/MainPanel");
            bool runtimeSettings = settings == null;
            if (runtimeSettings)
            {
                settings = ScriptableObject.CreateInstance<PanelSettings>();
                settings.hideFlags = HideFlags.HideAndDontSave;
            }

            // The one resolution policy for the whole UI (old CanvasScaler: 1920x1080, match 0.5).
            // Compared before assigning so a correct asset is not dirtied every play session.
            if (settings.scaleMode != PanelScaleMode.ScaleWithScreenSize)
                settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            if (settings.referenceResolution != new Vector2Int(1920, 1080))
                settings.referenceResolution = new Vector2Int(1920, 1080);
            if (settings.screenMatchMode != PanelScreenMatchMode.MatchWidthOrHeight)
                settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            if (!Mathf.Approximately(settings.match, 0.5f))
                settings.match = 0.5f;
            if (settings.sortingOrder != 100)
                settings.sortingOrder = 100;    // keeps the uGUI overlays that stay (SceneFader) on top
            if (settings.themeStyleSheet == null)
                settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("UI/RuntimeTheme");

            uiDocument.panelSettings = settings;
            go.SetActive(true);

            VisualElement root = uiDocument.rootVisualElement;
            // The root must never intercept a click meant for the game; the sheets themselves
            // pick where their controls are.
            root.pickingMode = PickingMode.Ignore;

            // One-shot viewport guard on the first real layout pass. Styling is verified by the
            // theme resolving (.screen -> absolute); a collapsed height here means the panel
            // never got its screen rect — in the editor this is the Game view Scale slider:
            // UI Toolkit runtime panels read a degenerate size while it is not 1x. TEMPORARY —
            // remove once P0 is confirmed on every machine that opens this project.
            root.RegisterCallbackOnce<GeometryChangedEvent>(_ =>
            {
                if (root.layout.height < 1f && Screen.height > 1f)
                    Debug.LogWarning(
                        $"[UIManager] panel viewport collapsed to {root.layout.width:0.#}x{root.layout.height:0.#} " +
                        $"while Screen is {Screen.width}x{Screen.height}. Known editor cause: the Game view " +
                        "Scale slider is not 1x — set it back to 1x and replay. (Builds are unaffected.)", this);
                else
                    Debug.Log($"[UIManager] first layout: root={root.layout.width:0.#}x{root.layout.height:0.#} — viewport OK", this);
            });

            // Font experiment: the first candidate that loads wins, and what actually applied is
            // logged. Revert to the project pixel font by moving "UI/BombSlimeFonts" first in the
            // array; the built-in LegacyRuntime (Arial metrics) is the final fallback.
            // (The USS sets no font of its own, so this root style inherits to every label.)
            Font uiFont = null;
            foreach (string fontCandidate in new[] { "UI/LiberationSans", "UI/BombSlimeFonts" })
            {
                Font loaded = Resources.Load<Font>(fontCandidate);
                if (loaded != null) { uiFont = loaded; break; }
            }
            if (uiFont == null)
                uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (uiFont != null)
            {
                root.style.unityFont = uiFont;
                Debug.Log($"[UIManager] UI font: {uiFont.name}", this);
            }

            // Belt-and-braces stylesheet attach: the panel theme (RuntimeTheme.tss) and the
            // per-UXML <Style src> are the proper routes; this roots Theme.uss on the document
            // directly so styling survives even if the asset's theme reference is ever lost.
            // Loads by name "Theme" — the USS must stay the only asset of that name in
            // Resources/UI (the .tss is RuntimeTheme), or Resources.Load resolves ambiguously.
            StyleSheet themeUss = Resources.Load<StyleSheet>("UI/Theme");
            if (themeUss != null)
            {
                root.styleSheets.Add(themeUss);
            }

            VisualElement hudLayer = new VisualElement { name = "Hud" };
            hudLayer.style.position = Position.Absolute;
            hudLayer.style.left = 0f;
            hudLayer.style.top = 0f;
            hudLayer.style.right = 0f;
            hudLayer.style.bottom = 0f;
            hudLayer.pickingMode = PickingMode.Ignore;
            root.Add(hudLayer);

            menusRoot = new VisualElement { name = "Menus" };
            menusRoot.style.position = Position.Absolute;
            menusRoot.style.left = 0f;
            menusRoot.style.top = 0f;
            menusRoot.style.right = 0f;
            menusRoot.style.bottom = 0f;
            menusRoot.pickingMode = PickingMode.Ignore;
            root.Add(menusRoot);
        }

        private void CreateHud()
        {
            VisualTreeAsset tree = Resources.Load<VisualTreeAsset>("UI/Hud");
            if (tree == null)
            {
                Debug.LogWarning("UIManager: missing UXML at Resources/UI/Hud — the gameplay HUD is unavailable", this);
                return;
            }

            VisualElement host = tree.Instantiate();
            host.style.position = Position.Absolute;
            host.style.left = 0f;
            host.style.top = 0f;
            host.style.right = 0f;
            host.style.bottom = 0f;

            VisualElement hudLayer = uiDocument.rootVisualElement.Q("Hud");
            hudLayer.Add(host);

            hud = new Hud(host);
        }

        private void SetCursor(bool visible)
        {
            // The OS cursor serves the menus directly now (Toolkit does mouse picking natively);
            // gameplay keeps it locked away as before. Fully qualified: UIElements also exports a
            // Cursor type, and both namespaces are imported here.
            UnityEngine.Cursor.visible = visible;
            UnityEngine.Cursor.lockState = visible ? CursorLockMode.None : CursorLockMode.Locked;
        }
    }
}
