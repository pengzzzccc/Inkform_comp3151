using Inkform.Bus;
using Inkform.Save;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Inkform.Level
{
    /// <summary>
    /// Scene director: the single gatekeeper for scene transitions. Reads the level topology
    /// (LevelGraph.txt, via LevelGraphReader) once at startup and answers "where do we go now" —
    /// StartNewGame, ReturnToMainMenu, and the LevelBus.Completed handler that resolves which scene
    /// an exit leads to. UIManager and LevelExit triggers never call SceneManager themselves; they
    /// route through here, so the topology lives in one place and scene names are never duplicated.
    ///
    /// Also the publisher of LevelBus.Started: on every scene load it broadcasts the active scene's
    /// name (null for the menu / unregistered scenes), keeping LevelBus.Current accurate for whoever
    /// needs "which level is this" (HUD, audio).
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
        [Tooltip("LevelGraph.txt — the same file the editor window edits. The single source of truth for the topology")]
        [SerializeField] private TextAsset levelGraphFile;

        // Parsed once in Awake; null when no file is wired (the public methods warn when called)
        private LevelGraph graph;

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

            graph = LevelGraphReader.Parse(levelGraphFile);
            if (levelGraphFile == null)
                Debug.LogWarning("SceneDirector: no LevelGraph.txt wired — run Tools > Inkform > Room Builder > Wire GameManager Flow", this);
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
            // No graph wired yet: nothing to resolve. The public methods warn when they are actually
            // called, so a silent skip here does not hide a real misconfiguration.
            if (graph == null) return;

            string scene = SceneManager.GetActiveScene().name;
            LevelBus.RaiseStarted(graph.IsMenuScene(scene) || !graph.HasRoom(scene) ? null : scene);
        }

        // ---- Transitions (the only SceneManager.LoadScene call sites in the project) ----

        /// <summary>
        /// Starts a fresh run in the graph's entry scene. Called by UIManager for an empty save slot,
        /// and by ContinueGame when a save cannot be honoured.
        /// </summary>
        public void StartNewGame()
        {
            if (graph == null || string.IsNullOrEmpty(graph.EntryScene))
            {
                Debug.LogWarning("SceneDirector: no level graph or entry scene configured — cannot start a new game", this);
                return;
            }
            LoadScene(graph.EntryScene);
        }

        /// <summary>
        /// Resumes a saved run: loads the room the save names and hands RespawnDirector the coordinate
        /// to put the player on. A save naming a room the graph no longer has (the txt was edited since
        /// it was written) is not fatal — warn and start over rather than load nothing.
        /// </summary>
        public void ContinueGame(SaveData save)
        {
            if (save == null || save.IsEmpty) { StartNewGame(); return; }

            if (graph == null)
            {
                Debug.LogWarning("SceneDirector: no level graph wired — cannot continue a saved run", this);
                return;
            }

            if (!graph.HasRoom(save.sceneName))
            {
                Debug.LogWarning($"SceneDirector: saved room '{save.sceneName}' is not in the level graph — starting a new run instead", this);
                StartNewGame();
                return;
            }

            // A load places the player by coordinate, so any door-side spawn left over from an earlier
            // transition must not also be waiting at the far end.
            pendingSpawnFrom = null;
            pendingSpawnPos = new Vector2(save.spawnX, save.spawnY);
            LoadScene(save.sceneName);
        }

        /// <summary>Returns to the main menu. Called by UIManager (pause menu's Save &amp; Quit) and by
        /// the dead-end fallback below. Closes the run first, which is the manual save: whatever the
        /// autosave last recorded gets its play time and death count brought up to date.</summary>
        public void ReturnToMainMenu()
        {
            SaveStore.EndRun();

            if (graph == null)
            {
                Debug.LogWarning("SceneDirector: no level graph wired — cannot return to the main menu", this);
                return;
            }
            LoadScene(graph.MenuScene);
        }

        // The one SceneManager.LoadScene wrapper: every transition funnels through here, so scene
        // names are resolved before this point and a future fade/log sits at exactly one place.
        private void LoadScene(string sceneName)
        {
            SceneManager.LoadScene(sceneName);
        }

        // ---- LevelBus.Completed ----

        /// <summary>
        /// A LevelExit was reached. Resolves the exit's target through the topology and loads it; a dead
        /// end (no such exit) falls back to the main menu rather than stranding the player.
        /// </summary>
        private void OnCompleted(string exitId)
        {
            string current = LevelBus.Current;
            if (current == null)
            {
                Debug.LogWarning("SceneDirector: level completed but no current level — ignoring", this);
                return;
            }

            string target = graph != null ? graph.TargetOf(current, exitId) : null;
            if (string.IsNullOrEmpty(target))
            {
                Debug.LogWarning($"SceneDirector: exit '{exitId}' of '{current}' has no target — returning to main menu", this);
                ReturnToMainMenu();
                return;
            }

            pendingSpawnFrom = current;         // the new scene spawns the player at this room's door
            LoadScene(target);
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

        public bool IsMenuScene(string sceneName) => graph != null && graph.IsMenuScene(sceneName);

        /// <summary>The player-facing name for a room ("a laboratory in a cave"), falling back to the
        /// scene name. Lets the save menu turn the room stored in a save file into something worth
        /// reading, without holding the graph itself.</summary>
        public string DisplayNameOf(string sceneName) => graph != null ? graph.DisplayNameOf(sceneName) : sceneName;
    }
}
