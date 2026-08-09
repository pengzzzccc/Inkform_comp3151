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
            // saved localPosition (Bomb.prefab stores -4.07, 0.45) instead of moving to this node
            current = Instantiate(spawnObject, transform.position, Quaternion.identity);
            cooling = false;
        }
    }
}
