using Inkform.Bus;
using Inkform.Life;
using Inkform.Tool;
using UnityEngine;

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
        private UnscaledTimer respawnTimer;

        // Same lazy cache as Bomb.Player / AudioManager.Listener: after a scene change the old
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

        void OnEnable()
        {
            LifeBus.Died += OnDied;
            LifeBus.CheckpointSet += OnCheckpointSet;
        }

        void OnDisable()
        {
            LifeBus.Died -= OnDied;
            LifeBus.CheckpointSet -= OnCheckpointSet;
        }

        void Start()
        {
            GameObject player = GameObject.FindGameObjectWithTag(Tags.Player);
            if (player == null) return;

            // Startup respawn point: prefer the checkpoint with isStartPoint checked and teleport the
            // player there; with none checked the game still plays — falls back to "wherever the
            // player was placed in the scene"
            Checkpoint start = FindStartPoint();
            if (start == null)
            {
                checkpoint = player.transform.position;
                return;
            }

            checkpoint = start.SpawnPos;
            LifeBus.RaiseRespawned(player, checkpoint);   // reuse the same respawn path instead of a second teleport implementation
            FxBus.RaiseSnap();
        }

        void Update()
        {
            if (pending == null) return;
            if (respawnTimer.IsRunning) return;

            LifeBus.RaiseRespawned(pending, checkpoint);
            pending = null;

            // The player was just teleported; without a snap the camera drags its follow inertia all
            // the way from the death point
            FxBus.RaiseSnap();
        }

        private void OnCheckpointSet(Vector2 pos)
        {
            checkpoint = pos;
        }

        private void OnDied(DeathContext ctx)
        {
            pending = ctx.Victim;

            // Without a DeathDirector in the scene, or when this cause has no strategy, fall back:
            // a missing strategy should only drop the presentation, never leave the player dead forever
            DeathDirector deaths = Deaths;
            DeathStrategy strategy = deaths != null ? deaths.Resolve(ctx.Cause) : null;

            respawnTimer.Set(strategy != null ? strategy.RespawnDelay : fallbackDelay);
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
    }
}
