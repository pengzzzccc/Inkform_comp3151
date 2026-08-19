using Inkform.Bus;
using Inkform.Save;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Inkform.Level
{
    /// <summary>
    /// Scene director: the single gatekeeper for scene transitions. Owns the LevelFlow asset (the level
    /// graph) and answers "where do we go now" — StartNewGame, ReturnToMainMenu, and the LevelBus.Completed
    /// handler that resolves which level an exit leads to. UIManager and LevelExit triggers never call
    /// SceneManager themselves; they route through here, so the topology lives in one place and the
    /// scene names are never duplicated.
    ///
    /// Also the publisher of LevelBus.Started: on every scene load it resolves the scene name back to a
    /// LevelScene asset (null for the menu / unregistered scenes) and broadcasts it, keeping
    /// LevelBus.Current accurate for whoever needs "which level is this" (HUD, audio).
    ///
    /// Attach to GameManager (the DontDestroyOnLoad host, already carrying UIManager / RespawnDirector).
    /// A sibling SceneDirector is reached by UIManager via GetComponent — like it already does for
    /// RespawnDirector — rather than through a static, so the menu's first ApplySceneState works no
    /// matter which component's Awake runs first.
    /// </summary>
    public class SceneDirector : MonoBehaviour
    {
        public static SceneDirector Instance { get; private set; }

        [Header("Level graph")]
        [Tooltip("The one LevelFlow asset: menu scene, entry level, and every level. The single source of truth for scene names")]
        [SerializeField] private LevelFlow flow;

        // Set by sceneLoaded, consumed one frame later in Update. See OnSceneLoaded for why.
        private bool sceneInitPending;

        // Set on LevelBus.Completed, consumed by RespawnDirector on the next scene's init: spawn the
        // player beside the door that leads back to the scene we came from, instead of the level start point
        private string pendingSpawnFrom;

        // Set on ContinueGame, consumed the same way one scene later: the exact coordinate a save
        // recorded. Outranks both of the above at the far end — a load is the one case where the
        // scene's own spawn points are the wrong answer.
        private Vector2? pendingSpawnPos;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void OnEnable()
        {
            LevelBus.Completed += OnCompleted;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        void OnDisable()
        {
            LevelBus.Completed -= OnCompleted;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // sceneLoaded never fires for the startup scene, so the first scene is initialized here
        void Start() => InitForScene();

        // The GameManager hosting this survives scene switches, and sceneLoaded fires before the new
        // scene's Start() methods. Deferring by a frame means LevelBus.Started is broadcast after the
        // new scene's objects have initialized — the same timing RespawnDirector / LevelMemento rely on.
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => sceneInitPending = true;

        void Update()
        {
            if (!sceneInitPending) return;
            sceneInitPending = false;
            InitForScene();
        }

        private void InitForScene()
        {
            // No flow wired yet: nothing to resolve. The public methods warn when they are actually
            // called, so a silent skip here does not hide a real misconfiguration.
            if (flow == null) return;

            LevelBus.RaiseStarted(Resolve(SceneManager.GetActiveScene()));
        }

        private LevelScene Resolve(Scene scene)
        {
            if (flow.IsMenuScene(scene.name)) return null;
            return flow.FindBySceneName(scene.name);
        }

        // ---- Transitions (the only SceneManager.LoadScene call sites in the project) ----

        /// <summary>
        /// Starts a fresh run in the flow's entry level. Called by UIManager for an empty save slot,
        /// and by ContinueGame when a save cannot be honoured.
        /// </summary>
        public void StartNewGame()
        {
            if (flow == null || flow.entryLevel == null)
            {
                Debug.LogWarning("SceneDirector: no LevelFlow or entryLevel configured — cannot start a new game", this);
                return;
            }
            LoadLevel(flow.entryLevel);
        }

        /// <summary>
        /// Resumes a saved run: loads the room the save names and hands RespawnDirector the coordinate
        /// to put the player on. A save naming a room the flow no longer has (an asset renamed or
        /// deleted since it was written) is not fatal — warn and start over rather than load nothing.
        /// </summary>
        public void ContinueGame(SaveData save)
        {
            if (save == null || save.IsEmpty) { StartNewGame(); return; }

            if (flow == null)
            {
                Debug.LogWarning("SceneDirector: no LevelFlow configured — cannot continue a saved run", this);
                return;
            }

            LevelScene target = flow.FindBySceneName(save.sceneName);
            if (target == null)
            {
                Debug.LogWarning($"SceneDirector: saved room '{save.sceneName}' is not in the flow — starting a new run instead", this);
                StartNewGame();
                return;
            }

            // A load places the player by coordinate, so any door-side spawn left over from an earlier
            // transition must not also be waiting at the far end.
            pendingSpawnFrom = null;
            pendingSpawnPos = new Vector2(save.spawnX, save.spawnY);
            LoadLevel(target);
        }

        /// <summary>Returns to the main menu. Called by UIManager (pause menu's Save &amp; Quit) and by
        /// the dead-end fallback below. Closes the run first, which is the manual save: whatever the
        /// autosave last recorded gets its play time and death count brought up to date.</summary>
        public void ReturnToMainMenu()
        {
            SaveStore.EndRun();

            if (flow == null)
            {
                Debug.LogWarning("SceneDirector: no LevelFlow configured — cannot return to the main menu", this);
                return;
            }
            LoadScene(flow.mainMenuSceneName);
        }

        /// <summary>Loads a specific level by its LevelScene asset. Future level-select entry point.</summary>
        public void LoadLevel(LevelScene level)
        {
            if (level == null)
            {
                Debug.LogWarning("SceneDirector: LoadLevel called with a null level", this);
                return;
            }
            LoadScene(level.sceneName);
        }

        private void LoadScene(string sceneName)
        {
            SceneManager.LoadScene(sceneName);
        }

        // ---- LevelBus.Completed ----

        /// <summary>
        /// A LevelExit was reached. Resolves the exit's target through the topology and loads it; a dead
        /// end (no such exit, or the target is empty) falls back to the main menu rather than stranding
        /// the player.
        /// </summary>
        private void OnCompleted(string exitId)
        {
            LevelScene current = LevelBus.Current;
            if (current == null)
            {
                Debug.LogWarning("SceneDirector: level completed but no current level — ignoring", this);
                return;
            }

            LevelScene target = current.TargetOf(exitId);
            if (target == null)
            {
                Debug.LogWarning($"SceneDirector: exit '{exitId}' of '{current.name}' has no target — returning to main menu", this);
                ReturnToMainMenu();
                return;
            }

            pendingSpawnFrom = current.sceneName;   // the new scene spawns the player at this room's door
            LoadLevel(target);
        }

        /// <summary>The scene we came from (set by OnCompleted); null when no door was taken, e.g. a
        /// fresh run from the menu. Cleared on read so it applies to exactly one scene init.</summary>
        public string ConsumePendingSpawnFrom()
        {
            string v = pendingSpawnFrom;
            pendingSpawnFrom = null;
            return v;
        }

        /// <summary>The coordinate a loaded save wants the player placed on (set by ContinueGame); null
        /// when this scene was not reached by loading a save. Cleared on read, like the one above, so
        /// it applies to exactly one scene init.</summary>
        public Vector2? ConsumePendingSpawnPos()
        {
            Vector2? v = pendingSpawnPos;
            pendingSpawnPos = null;
            return v;
        }

        // ---- Accessors for UIManager (scene-name policy lives here, not duplicated in the UI layer) ----

        public bool IsMenuScene(string sceneName) => flow != null && flow.IsMenuScene(sceneName);

        public LevelScene CurrentLevel => LevelBus.Current;

        /// <summary>The LevelScene asset for a scene name, or null. Lets the save menu turn the room
        /// name stored in a save file into something worth reading, without holding the flow itself.</summary>
        public LevelScene FindLevel(string sceneName) => flow != null ? flow.FindBySceneName(sceneName) : null;
    }
}
