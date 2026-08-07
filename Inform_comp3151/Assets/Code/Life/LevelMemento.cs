using Inkform.Bus;
using System.Collections.Generic;
using UnityEngine;

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
        // Originators are scanned once at startup: level objects are placed in the scene and never
        // added at runtime. This also naturally excludes Spawner's runtime instances — those refill
        // themselves, and including them would spawn an extra batch of bombs on every respawn
        private readonly List<IRestorable> originators = new List<IRestorable>();
        private readonly List<IMemento> snapshot = new List<IMemento>();

        void OnEnable()
        {
            LifeBus.CheckpointSet += OnCheckpointSet;
            LifeBus.Respawned += OnRespawned;
        }

        void OnDisable()
        {
            LifeBus.CheckpointSet -= OnCheckpointSet;
            LifeBus.Respawned -= OnRespawned;
        }

        void Start()
        {
            // Use the includeInactive overload: Unity 6000.4 deprecated the FindObjectsSortMode
            // versions (instance ID ordering will be replaced by EntityId), the no-sort-parameter
            // overload is the current recommended API. Order is irrelevant (restores never affect each
            // other), so no sort parameter is needed
            MonoBehaviour[] all = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include);

            foreach (MonoBehaviour mb in all)
            {
                if (mb is IRestorable r) originators.Add(r);
            }

            // Capture once at startup: dying before touching any checkpoint still needs a restorable
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
