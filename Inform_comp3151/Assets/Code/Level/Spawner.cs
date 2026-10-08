using Inkform.Tool;
using UnityEngine;

namespace Inkform.Level
{
    /// <summary>
    /// Spawn point: spawns one instance at its own position; after the instance is destroyed, waits a
    /// cooldown and respawns another. Ownership is judged by holding the instance reference —
    /// HazardBus.Exploded fires per victim without the bomb's own identity; using it to tell "did MY
    /// bomb blow up" would misjudge (others' blasts also trigger it, and a zero-victim blast never
    /// triggers it at all).
    /// </summary>
    public class Spawner : MonoBehaviour
    {
        [SerializeField] private float spawnTimer = 5f;
        [SerializeField] private GameObject spawnObject;

        private Timer timer;
        private GameObject current;     // the instance this spawner currently holds
        private bool cooling;           // detected the instance is gone, waiting out the cooldown

        void Start()
        {
            Spawn();
        }

        void Update()
        {
            if (current != null) return;        // Unity overloads ==, so a destroyed instance reads as null here

            if (!cooling)                       // just noticed the instance is gone; cooldown starts from this moment
            {
                cooling = true;
                timer.Set(spawnTimer);
                return;
            }

            if (timer.IsRunning) return;
            Spawn();
        }

        private void Spawn()
        {
            // Must use the overload with position: Instantiate(prefab, transform) keeps the prefab's
            // saved localPosition (the legacy bomb prefab stored -4.07, 0.45) instead of moving to this node
            current = Instantiate(spawnObject, transform.position, Quaternion.identity);
            cooling = false;
        }

        // ---- Authoring ----

        // Always-on spawn marker: orange while a spawn target is wired, warning yellow when the
        // slot is empty — a spawner without a prefab silently does nothing, and the gizmo makes
        // that visible in the scene view without selecting every node.
        private void OnDrawGizmos()
        {
            Gizmos.color = spawnObject != null
                ? new Color(1f, 0.6f, 0.2f, 0.9f)
                : new Color(1f, 0.85f, 0.2f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, 0.3f);

            Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.15f);
            Gizmos.DrawSphere(transform.position, 0.3f);
        }
    }
}
