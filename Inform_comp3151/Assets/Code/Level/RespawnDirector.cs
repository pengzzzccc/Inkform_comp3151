using Inkform.Bus;
using Inkform.Fx;
using Inkform.Life;
using Inkform.Save;
using Inkform.Tool;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Inkform.Level
{
    /// <summary>
    /// Respawn director: remembers the current checkpoint, and after death pauses briefly before
    /// putting the deceased back. Teleports only, never reloads the scene — so it does not depend on
    /// Build Settings, and cross-scene singletons like AudioManager never need rebuilding.
    /// Attach to GameManager (already the host of InputHandler / AudioDirector / AudioManager).
    /// </summary>
    public class RespawnDirector : MonoBehaviour
    {
        [Header("Respawn")]
        // Fallback value: used when the cause has no strategy configured. Normally the pause length is
        // decided by DeathStrategy.RespawnDelay — "how quickly you come back" is a property of the
        // death method (falling should respawn faster than being spiked), not of the respawn system
        [SerializeField] private float fallbackDelay = 0.9f;     // death-to-respawn pause; Celeste is about 1 second

        private Vector2 checkpoint;
        private GameObject pending;         // the deceased waiting to respawn, null = nobody waiting

        // Must use unscaled timing: on the death frame the death strategy requests hitstop, crushing
        // timeScale to 0 — a plain Timer's Time.time is frozen then, waiting for it to expire is
        // waiting forever
        private float respawnRemaining;

        // Same lazy cache as PlayerBus.Player / AudioManager.Listener: after a scene change the old
        // reference becomes a Unity fake-null and is re-looked-up on next use, so no ResetStatics needed
        private DeathDirector deathCache;

        private DeathDirector Deaths
        {
            get
            {
                // FindAnyObjectByType rather than FindFirstObjectByType: the latter depends on instance
                // ID ordering and is deprecated (the order was never stable anyway; which one is found
                // first is irrelevant here)
                if (deathCache == null) deathCache = FindAnyObjectByType<DeathDirector>();
                return deathCache;
            }
        }

        // Set by sceneLoaded, consumed one frame later in Update. See OnSceneLoaded for why.
        private bool sceneInitPending;

        void OnEnable()
        {
            LifeBus.Died += OnDied;
            LifeBus.CheckpointSet += OnCheckpointSet;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        void OnDisable()
        {
            LifeBus.Died -= OnDied;
            LifeBus.CheckpointSet -= OnCheckpointSet;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        // The GameManager hosting this survives scene switches (PersistentGameRoot calls
        // DontDestroyOnLoad on its host), so Start() only ever ran against the *first* scene. Booting
        // straight into a level hid that, but the menu flow made MainMenu the first scene: no player
        // was found there, Start() bailed out, and `checkpoint` stayed at its default (0,0) — dying
        // in a level before touching any checkpoint teleported the player to the world origin.
        //
        // Deferred by a frame rather than run here: sceneLoaded fires before the new scene's Start()
        // methods, and the init below actually teleports the player and snaps the camera — doing that
        // ahead of the player's own Start() risks being overwritten. Update runs after all Starts,
        // matching the timing this used to have.
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => sceneInitPending = true;

        // sceneLoaded never fires for the startup scene, so the first scene is initialized here
        void Start() => InitForScene();

        private void InitForScene()
        {
            GameObject player = GameObject.FindGameObjectWithTag(Tags.Player);
            if (player == null) return;

            // Loaded save: the coordinate the save recorded outranks every spawn point in the scene —
            // it is the whole point of continuing a run. Checked first so neither the door-side spawn
            // nor the start point can win, and consumed here so it applies to this scene only.
            Vector2? loaded = SceneDirector.Instance != null ? SceneDirector.Instance.ConsumePendingSpawnPos() : null;
            if (loaded.HasValue)
            {
                checkpoint = loaded.Value;
                PlaceAndRecord(player);
                return;
            }

            // Startup respawn point: prefer the checkpoint with isStartPoint checked and teleport the
            // player there; with none checked the game still plays — falls back to "wherever the
            // player was placed in the scene"
            Checkpoint start = FindStartPoint();

            // Door-side spawn: entering from another room puts the player beside that room's door in
            // this scene (SceneDirector records the source scene on LevelBus.Completed) rather than
            // the level start point, so direction stays intuitive across level transitions
            string from = SceneDirector.Instance != null ? SceneDirector.Instance.ConsumePendingSpawnFrom() : null;
            Checkpoint spawn = from != null ? FindDoorSpawn(from) : null;
            if (spawn == null) spawn = start;

            if (spawn == null)
            {
                checkpoint = player.transform.position;
                RecordSave();   // still a new room, still where the run now is — see RecordSave
                return;
            }

            checkpoint = spawn.SpawnPos;
            PlaceAndRecord(player);
        }

        // Teleport + camera snap + autosave, shared by both of InitForScene's placing branches.
        // RaiseRespawned rather than moving the transform here: reuse the same respawn path instead of
        // a second teleport implementation.
        private void PlaceAndRecord(GameObject player)
        {
            LifeBus.RaiseRespawned(player, checkpoint);
            FxBus.RaiseSnap();
            RecordSave();
        }

        /// <summary>
        /// Autosave. Called at the two moments `checkpoint` changes for a reason worth keeping — a room
        /// entered, or a checkpoint touched — so the save always names a place the player can be put
        /// back onto. Deliberately *not* called from the death respawn or from Unstuck: neither moves
        /// the checkpoint, so there would be nothing new to write.
        ///
        /// This lives here rather than in a save-specific director because this class already owns
        /// "where the player comes back", and reading `checkpoint` from anywhere else would mean
        /// racing this component's own deferred scene init (both SceneDirector and this one defer by a
        /// frame, and Update order between two components on one GameObject is not defined).
        /// SaveStore ignores the call entirely outside a run, so opening a level straight from the
        /// editor writes nothing.
        /// </summary>
        private void RecordSave() => SaveStore.RecordProgress(SceneManager.GetActiveScene().name, checkpoint);

        void Update()
        {
            // Runs before the respawn pump: a scene switch invalidates `checkpoint`, and a death
            // pending from the previous scene must never be resurrected against the new one
            if (sceneInitPending)
            {
                sceneInitPending = false;
                pending = null;
                respawnRemaining = 0f;
                InitForScene();
            }

            if (pending == null) return;
            if (GameTimeController.Instance == null || !GameTimeController.Instance.IsUserPaused)
                respawnRemaining = Mathf.Max(0f, respawnRemaining - Time.unscaledDeltaTime);
            if (respawnRemaining > 0f) return;

            LifeBus.RaiseRespawned(pending, checkpoint);
            pending = null;

            // The player was just teleported; without a snap the camera drags its follow inertia all
            // the way from the death point
            FxBus.RaiseSnap();
        }

        /// <summary>
        /// Puts the player back on the last checkpoint on demand — the Controls tab's Unstuck button,
        /// for when a bug wedges the slime somewhere it cannot leave. Reuses the death path's teleport
        /// rather than moving the transform here, so anything listening for a respawn (camera snap,
        /// state reset) sees the same event it always does.
        ///
        /// Clears any death still waiting out its delay: that pending respawn would otherwise fire a
        /// second later against a player who has already been moved, teleporting them again.
        /// </summary>
        public void RespawnNow()
        {
            GameObject player = GameObject.FindGameObjectWithTag(Tags.Player);
            if (player == null) return;     // no player in this scene (the menu) — nobody to rescue

            pending = null;
            respawnRemaining = 0f;
            LifeBus.RaiseRespawned(player, checkpoint);
            FxBus.RaiseSnap();
        }

        private void OnCheckpointSet(Vector2 pos)
        {
            checkpoint = pos;
            RecordSave();
        }

        private void OnDied(DeathContext ctx)
        {
            pending = ctx.Victim;

            // Without a DeathDirector in the scene, or when this cause has no strategy, fall back:
            // a missing strategy should only drop the presentation, never leave the player dead forever
            DeathDirector deaths = Deaths;
            DeathStrategy strategy = deaths != null ? deaths.Resolve(ctx.Cause) : null;

            respawnRemaining = Mathf.Max(0f, strategy != null ? strategy.RespawnDelay : fallbackDelay);
        }

        // The no-sort-parameter overload is the current recommended API: the FindObjectsSortMode
        // version is deprecated in Unity 6000.4, for the same reason as AudioManager — instance ID
        // ordering was never stable, and order does not matter here
        private Checkpoint FindStartPoint()
        {
            Checkpoint[] all = Object.FindObjectsByType<Checkpoint>();
            foreach (Checkpoint c in all)
            {
                if (c.IsStartPoint) return c;
            }
            return null;
        }

        // The door-side spawn point maintained by Level Graph as "Spawn_<sceneName>" (a Checkpoint
        // beside the door whose exitId is that scene); matching by name keeps it a pure editor-side
        // convention — no new component, no scene wiring
        private Checkpoint FindDoorSpawn(string fromScene)
        {
            string want = $"Spawn_{fromScene}";
            Checkpoint[] all = Object.FindObjectsByType<Checkpoint>();
            foreach (Checkpoint c in all)
            {
                if (c.gameObject.name == want) return c;
            }
            return null;
        }
    }
}
