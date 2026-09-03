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
    /// <summary>Single gatekeeper for graph policy and scene transitions.</summary>
    public class SceneDirector : MonoBehaviour
    {
        public static SceneDirector Instance { get; private set; }

        [Header("Level graph")]
        [SerializeField] private TextAsset levelGraphFile;

        private LevelGraph graph;
        private bool graphInitialized;
        private bool missingGraphWarned;
        private bool sceneInitPending;
        private bool transitionInProgress;
        private AsyncOperation loadOperation;

        private string pendingSpawnFrom;
        private Vector2? pendingSpawnPos;

        public bool IsTransitioning => transitionInProgress;

        void Awake()
        {
            if (Instance != null && Instance != this) { enabled = false; return; }
            Instance = this;
            EnsureInitialized();
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

        void Start() => InitForScene();

        void Update()
        {
            if (!sceneInitPending) return;
            sceneInitPending = false;
            InitForScene();
        }

        /// <summary>Idempotent and safe to call from another component's Awake.</summary>
        public bool EnsureInitialized()
        {
            if (graphInitialized) return graph != null;
            graphInitialized = true;
            graph = LevelGraphReader.Parse(levelGraphFile);

            if (levelGraphFile == null && !missingGraphWarned)
            {
                missingGraphWarned = true;
                Debug.LogWarning("SceneDirector: no LevelGraph.txt wired — open Tools > Inkform > Level Graph", this);
            }
            return graph != null;
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
            if (!EnsureInitialized()) return;
            string scene = SceneManager.GetActiveScene().name;
            LevelBus.RaiseStarted(graph.IsMenuScene(scene) || !graph.HasRoom(scene) ? null : scene);
        }

        public bool CanStartNewGame(out string reason)
        {
            if (!EnsureInitialized() || levelGraphFile == null)
            {
                reason = "no level graph is configured";
                return false;
            }
            if (string.IsNullOrWhiteSpace(graph.EntryScene))
            {
                reason = "the level graph has no New Game entry";
                return false;
            }
            if (!graph.HasRoom(graph.EntryScene))
            {
                reason = $"entry '{graph.EntryScene}' is not a declared room";
                return false;
            }
            if (!Application.CanStreamedLevelBeLoaded(graph.EntryScene))
            {
                reason = $"entry scene '{graph.EntryScene}' is not loadable";
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

            pendingSpawnFrom = null;
            pendingSpawnPos = null;
            return RequestScene(graph.EntryScene);
        }

        public bool CanContinueGame(SaveData save, out string reason)
        {
            if (save == null || save.IsEmpty)
            {
                reason = "the selected slot is empty";
                return false;
            }
            if (!EnsureInitialized() || levelGraphFile == null)
            {
                reason = "no level graph is configured";
                return false;
            }
            if (!graph.HasRoom(save.sceneName))
            {
                reason = $"saved room '{save.sceneName}' is not in the level graph";
                return false;
            }
            if (!Application.CanStreamedLevelBeLoaded(save.sceneName))
            {
                reason = $"saved scene '{save.sceneName}' is not loadable";
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

            pendingSpawnFrom = null;
            pendingSpawnPos = new Vector2(save.spawnX, save.spawnY);
            return RequestScene(save.sceneName);
        }

        public bool ReturnToMainMenu()
        {
            if (!EnsureInitialized() || string.IsNullOrWhiteSpace(graph.MenuScene)
                || !Application.CanStreamedLevelBeLoaded(graph.MenuScene))
            {
                Debug.LogWarning("SceneDirector: the configured menu scene is not loadable", this);
                return false;
            }
            if (transitionInProgress) return false;

            SaveStore.EndRun();
            pendingSpawnFrom = null;
            pendingSpawnPos = null;
            return RequestScene(graph.MenuScene);
        }

        private bool RequestScene(string sceneName)
        {
            if (transitionInProgress || string.IsNullOrWhiteSpace(sceneName)) return false;
            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                Debug.LogWarning($"SceneDirector: scene '{sceneName}' is not loadable", this);
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
        }

        private void HandleLoadFailure()
        {
            transitionInProgress = false;
            SetGameStateFromActiveScene();
            loadOperation = null;
            pendingSpawnFrom = null;
            pendingSpawnPos = null;
            if (!SaveStore.AbortNewRun()) SaveStore.EndRun();
            GetComponent<InputHandler>()?.SetPlaying(UIManager.Instance == null || !UIManager.Instance.IsPaused);
        }

        // GameState's final word on a finished switch: the Transition state set in RequestScene
        // clears here, settling on whatever scene is now active. UIManager's scene-landing write may
        // run a few frames earlier (sceneLoaded fires before this coroutine resumes); this one wins.
        private void SetGameStateFromActiveScene() =>
            GameStateStore.Set(graph != null && graph.IsMenuScene(SceneManager.GetActiveScene().name)
                ? GameStateStore.GameState.MainMenu
                : GameStateStore.GameState.Playing);

        private void OnCompleted(string exitId)
        {
            if (transitionInProgress || LifeBus.IsDead) return;
            if (!EnsureInitialized()) return;

            string current = LevelBus.Current;
            if (current == null)
            {
                Debug.LogWarning("SceneDirector: level completed but no current level — ignoring", this);
                return;
            }

            string target = graph.TargetOf(current, exitId);
            if (string.IsNullOrEmpty(target))
            {
                Debug.LogWarning($"SceneDirector: exit '{exitId}' of '{current}' has no target — returning to main menu", this);
                ReturnToMainMenu();
                return;
            }

            pendingSpawnFrom = current;
            if (!RequestScene(target)) pendingSpawnFrom = null;
        }

        public string ConsumePendingSpawnFrom()
        {
            string value = pendingSpawnFrom;
            pendingSpawnFrom = null;
            return value;
        }

        public Vector2? ConsumePendingSpawnPos()
        {
            Vector2? value = pendingSpawnPos;
            pendingSpawnPos = null;
            return value;
        }

        public bool IsMenuScene(string sceneName) => EnsureInitialized() && graph.IsMenuScene(sceneName);

        public string DisplayNameOf(string sceneName) =>
            EnsureInitialized() ? graph.DisplayNameOf(sceneName) : sceneName;
    }
}
