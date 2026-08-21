using Inkform.Bus;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Inkform.Life
{
    /// <summary>
    /// Level snapshot caretaker (Caretaker of the memento pattern): on checkpoint touch, captures every
    /// restorable object in the scene; on death respawn, restores them all.
    ///
    /// Solves a real gameplay problem: respawn used to only teleport the player back to the checkpoint,
    /// so shattered walls never came back — retrying the same section repeatedly left it emptier and
    /// emptier, silently breaking later puzzles.
    ///
    /// This class knows no concrete object types — it only deals with IMemento, whose content is
    /// opaque. Attach to GameManager (already the host of RespawnDirector / DeathDirector).
    /// </summary>
    public class LevelMemento : MonoBehaviour
    {
        // Originators are scanned once per scene: level objects are placed in the scene and never
        // added at runtime. This also naturally excludes Spawner's runtime instances — those refill
        // themselves, and including them would spawn an extra batch of bombs on every respawn
        private readonly List<IRestorable> originators = new List<IRestorable>();
        private readonly List<IMemento> snapshot = new List<IMemento>();

        // Set by sceneLoaded, consumed one frame later in Update. See OnSceneLoaded for why.
        private bool rescanPending;

        void OnEnable()
        {
            LifeBus.CheckpointSet += OnCheckpointSet;
            LifeBus.Respawned += OnRespawned;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        void OnDisable()
        {
            LifeBus.CheckpointSet -= OnCheckpointSet;
            LifeBus.Respawned -= OnRespawned;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        // The GameManager hosting this survives scene switches (AudioManager calls DontDestroyOnLoad on
        // its own host), so Start() only ever scanned the *first* scene. That was invisible while the
        // game booted straight into a level, but the menu flow made MainMenu the first scene: the scan
        // then found nothing and never ran again, so no wall in any level ever came back on respawn.
        //
        // Deferred by a frame rather than scanned here: sceneLoaded fires before the new scene's
        // Start() methods, and Capture() records positions — scanning immediately could snapshot an
        // object before its own Start() has placed it. Update runs after all Starts, matching the
        // timing this used to have.
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => rescanPending = true;

        void Update()
        {
            if (!rescanPending) return;
            rescanPending = false;
            ScanScene();
        }

        // sceneLoaded never fires for the startup scene, so the first scene is scanned here
        void Start() => ScanScene();

        private void ScanScene()
        {
            // Rebuilt from scratch: on a scene switch the previous level's originators are destroyed,
            // and a stale list would make Capture() skip past dead entries every time
            originators.Clear();

            // Use the includeInactive overload: Unity 6000.4 deprecated the FindObjectsSortMode
            // versions (instance ID ordering will be replaced by EntityId), the no-sort-parameter
            // overload is the current recommended API. Order is irrelevant (restores never affect each
            // other), so no sort parameter is needed
            MonoBehaviour[] all = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include);

            foreach (MonoBehaviour mb in all)
            {
                if (mb is IRestorable r) originators.Add(r);
            }

            // Capture once on entry: dying before touching any checkpoint still needs a restorable
            // initial state
            Capture();
        }

        private void OnCheckpointSet(Vector2 pos) => Capture();

        private void OnRespawned(GameObject victim, Vector2 pos) => Restore();

        private void Capture()
        {
            snapshot.Clear();
            foreach (IRestorable r in originators)
            {
                // The originator may have been destroyed elsewhere. Must cast back to MonoBehaviour
                // before checking — Unity's overloaded == lives on UnityEngine.Object; comparing an
                // interface reference directly cannot recognize destroyed objects
                if (r as MonoBehaviour == null) continue;
                snapshot.Add(r.Capture());
            }
        }

        private void Restore()
        {
            foreach (IMemento m in snapshot) m.Restore();
        }
    }
}
