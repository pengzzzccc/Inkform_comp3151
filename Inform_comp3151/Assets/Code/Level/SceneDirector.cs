using System.Collections;
using Inkform.Bus;
using Inkform.Fx;
using Inkform.Input;
using Inkform.Life;
using Inkform.Save;
using Inkform.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Inkform.Level
{
    /// <summary>
    /// Single gatekeeper for scene transitions. The world comes from a WorldDefinition asset (menu
    /// scene, new-game entry room, room registry); the room-to-room topology lives on the LevelExit
    /// triggers themselves, which call in through LevelBus.ExitReached carrying their own
    /// destination reference — there is no graph file to resolve against anymore.
    ///
    /// Transition pipeline, the same shape the official Unity samples use: lock + fade out → async
    /// Single-mode load → resolve the current room → fade in → hand input back. The lock makes every
    /// entry point (doors, new game, continue, quit-to-menu) mutually exclusive, and sceneLoaded
    /// only sets a flag because it fires before this coroutine's AsyncOperation continuation
    /// (clearing state there has crashed the coroutine before — see OnSceneLoaded).
    /// </summary>
    public class SceneDirector : MonoBehaviour
    {
        public static SceneDirector Instance { get; private set; }

        [Header("World")]
        [SerializeField] private WorldDefinition world;

        [Header("Transition")]
        [Tooltip("Seconds of fade to black before a scene switch")]
        [SerializeField] private float fadeOutSeconds = 0.25f;
        [Tooltip("Seconds of fade back to clear after the new scene is active")]
        [SerializeField] private float fadeInSeconds = 0.4f;

        private RoomDefinition currentRoom;
        private bool worldMissingWarned;
        private bool sceneInitPending;
        private bool transitionInProgress;
        private AsyncOperation loadOperation;

        private string pendingSpawnId;
        private Vector2? pendingSpawnPos;

        private SceneFader fader;

        public bool IsTransitioning => transitionInProgress;

        void Awake()
        {
            if (Instance != null && Instance != this) { enabled = false; return; }
            Instance = this;

            // Self-installed like UIManager's GamepadCursor / InventoryHud: the fader exists without
            // anyone having to add it to the GameManager prefab by hand
            fader = GetComponent<SceneFader>();
            if (fader == null) fader = gameObject.AddComponent<SceneFader>();
        }

        void OnEnable()
        {
            LevelBus.ExitReached += OnExitReached;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        void OnDisable()
        {
            LevelBus.ExitReached -= OnExitReached;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Start() => InitForScene();

        void Update()
        {
            if (!sceneInitPending) return;
            sceneInitPending = false;
            InitForScene();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // sceneLoaded is raised just before the AsyncOperation resumes its waiting coroutine.
            // Keep the transition locked until LoadSceneRoutine observes actual completion; clearing
            // the shared operation here used to make that coroutine dereference null on its next tick.
            sceneInitPending = true;
            GameTimeController.Instance?.ClearHitStop();
        }

        private void InitForScene()
        {
            string scene = SceneManager.GetActiveScene().name;
            currentRoom = world != null ? world.FindBySceneName(scene) : null;

            // Null means the menu or an unregistered scene (an editor cold start into a test level) —
            // either way no room is running, which is all Started's subscribers need to know
            LevelBus.RaiseStarted(IsMenuScene(scene) || currentRoom == null ? null : scene);
        }

        // ---- World access ----

        // Editor cold starts and a not-yet-migrated prefab both arrive here; one warning is enough
        private bool WorldMissing(string what)
        {
            if (world != null) return false;
            if (!worldMissingWarned)
            {
                worldMissingWarned = true;
                Debug.LogWarning(
                    $"SceneDirector: no WorldDefinition assigned — {what} unavailable. Tools > Inkform > World wires it", this);
            }
            return true;
        }

        public bool IsMenuScene(string sceneName) =>
            world != null && world.MenuSceneName == sceneName;

        public string DisplayNameOf(string sceneName)
        {
            RoomDefinition room = world != null ? world.FindBySceneName(sceneName) : null;
            return room != null ? room.DisplayName : sceneName;
        }

        // ---- Flow entry points (UIManager calls these) ----

        public bool CanStartNewGame(out string reason)
        {
            if (WorldMissing("starting a new game"))
            {
                reason = "no world definition is assigned";
                return false;
            }
            if (world.EntryRoom == null || !world.EntryRoom.IsSet)
            {
                reason = "the world has no New Game entry room";
                return false;
            }
            if (!Application.CanStreamedLevelBeLoaded(world.EntryRoom.SceneName))
            {
                reason = $"entry scene '{world.EntryRoom.SceneName}' is not loadable (Build Settings?)";
                return false;
            }
            if (transitionInProgress)
            {
                reason = "a scene transition is already running";
                return false;
            }

            reason = null;
            return true;
        }

        public bool StartNewGame()
        {
            if (!CanStartNewGame(out string reason))
            {
                Debug.LogWarning($"SceneDirector: cannot start a new game — {reason}", this);
                return false;
            }

            pendingSpawnId = null;
            pendingSpawnPos = null;
            return RequestScene(world.EntryRoom.SceneName);
        }

        public bool CanContinueGame(SaveData save, out string reason)
        {
            if (save == null || save.IsEmpty)
            {
                reason = "the selected slot is empty";
                return false;
            }
            if (WorldMissing("continuing a save"))
            {
                reason = "no world definition is assigned";
                return false;
            }
            if (world.FindBySceneName(save.sceneName) == null)
            {
                reason = $"saved room '{save.sceneName}' is not in the world";
                return false;
            }
            if (!Application.CanStreamedLevelBeLoaded(save.sceneName))
            {
                reason = $"saved scene '{save.sceneName}' is not loadable (Build Settings?)";
                return false;
            }
            if (transitionInProgress)
            {
                reason = "a scene transition is already running";
                return false;
            }

            reason = null;
            return true;
        }

        public bool ContinueGame(SaveData save)
        {
            if (!CanContinueGame(save, out string reason))
            {
                Debug.LogWarning($"SceneDirector: cannot continue — {reason}", this);
                return false;
            }

            // The save's coordinate outranks every spawn point in the target scene — it is the whole
            // point of continuing a run (RespawnDirector consumes it after the load)
            pendingSpawnId = null;
            pendingSpawnPos = new Vector2(save.spawnX, save.spawnY);
            return RequestScene(save.sceneName);
        }

        public bool ReturnToMainMenu()
        {
            if (WorldMissing("returning to the main menu")) return false;
            if (string.IsNullOrWhiteSpace(world.MenuSceneName)
                || !Application.CanStreamedLevelBeLoaded(world.MenuSceneName))
            {
                Debug.LogWarning("SceneDirector: the configured menu scene is not loadable", this);
                return false;
            }
            if (transitionInProgress) return false;

            SaveStore.EndRun();
            pendingSpawnId = null;
            pendingSpawnPos = null;
            return RequestScene(world.MenuSceneName);
        }

        // ---- Doors ----

        private void OnExitReached(RoomDefinition destination, string spawnId)
        {
            if (transitionInProgress || LifeBus.IsDead) return;
            if (destination == null) return;   // LevelExit already warned; the World validator owns content errors

            pendingSpawnId = spawnId;
            if (!RequestScene(destination.SceneName)) pendingSpawnId = null;
        }

        // ---- The one loading path ----

        private bool RequestScene(string sceneName)
        {
            if (transitionInProgress || string.IsNullOrWhiteSpace(sceneName)) return false;
            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                Debug.LogWarning(
                    $"SceneDirector: scene '{sceneName}' is not loadable — run Tools > Inkform > World > Sync Build Settings", this);
                return false;
            }

            transitionInProgress = true;
            GameStateStore.Set(GameStateStore.GameState.Transition);
            GetComponent<InputHandler>()?.SetPlaying(false);
            StartCoroutine(LoadSceneRoutine(sceneName));
            return true;
        }

        private IEnumerator LoadSceneRoutine(string sceneName)
        {
            // Fade to black first, so the load's first hiccup is already behind the curtain
            if (fader != null) yield return fader.FadeOut(fadeOutSeconds);

            // Never unload a scene from inside the trigger/physics callback that requested it.
            yield return null;

            AsyncOperation operation;
            try
            {
                operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
                loadOperation = operation;
            }
            catch (System.Exception exception)
            {
                Debug.LogError($"SceneDirector: loading '{sceneName}' failed ({exception.Message})", this);
                HandleLoadFailure();
                yield break;
            }
            if (operation == null)
            {
                HandleLoadFailure();
                yield break;
            }

            // Yield the operation itself. sceneLoaded may run before this continuation, but the local
            // reference remains valid and no scene callback is allowed to unlock the transition early.
            yield return operation;

            loadOperation = null;
            transitionInProgress = false;
            SetGameStateFromActiveScene();
            GetComponent<InputHandler>()?.SetPlaying(UIManager.Instance == null || !UIManager.Instance.IsPaused);

            // RespawnDirector places the player one frame later (its deferred scene init); the fade-in
            // covers that placement exactly like it covered the load
            if (fader != null) yield return fader.FadeIn(fadeInSeconds);
        }

        private void HandleLoadFailure()
        {
            transitionInProgress = false;
            SetGameStateFromActiveScene();
            loadOperation = null;
            pendingSpawnId = null;
            pendingSpawnPos = null;
            if (!SaveStore.AbortNewRun()) SaveStore.EndRun();

            // The screen faded out for a load that never came — do not leave it black
            if (fader != null) StartCoroutine(fader.FadeIn(fadeInSeconds));
            GetComponent<InputHandler>()?.SetPlaying(UIManager.Instance == null || !UIManager.Instance.IsPaused);
        }

        // GameState's final word on a finished switch: the Transition state set in RequestScene
        // clears here, settling on whatever scene is now active. UIManager's scene-landing write may
        // run a few frames earlier (sceneLoaded fires before this coroutine resumes); this one wins.
        private void SetGameStateFromActiveScene() =>
            GameStateStore.Set(IsMenuScene(SceneManager.GetActiveScene().name)
                ? GameStateStore.GameState.MainMenu
                : GameStateStore.GameState.Playing);

        // ---- Arrival data (RespawnDirector consumes after the load) ----

        public string ConsumePendingSpawnId()
        {
            string value = pendingSpawnId;
            pendingSpawnId = null;
            return value;
        }

        public Vector2? ConsumePendingSpawnPos()
        {
            Vector2? value = pendingSpawnPos;
            pendingSpawnPos = null;
            return value;
        }
    }
}
