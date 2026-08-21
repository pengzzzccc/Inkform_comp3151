using System;
using System.Collections.Generic;
using UnityEngine;

namespace Inkform.Level.CrystalMine
{
    /// <summary>Configured fixed-capacity pools for the elevator's crystal/bomb rain waves.</summary>
    public sealed class HazardRainSpawner : MonoBehaviour
    {
        [Serializable]
        public sealed class HazardWave
        {
            public GameObject prefab;
            [Min(1)] public int poolSize = 6;
            [Min(0f)] public float startTime;
            [Min(0.01f)] public float interval = 0.65f;
            [Min(0f)] public float duration = 5f;
            public Vector2 horizontalRange = new Vector2(-5f, 5f);
            public float spawnHeight = 7f;
            [Min(0.1f)] public float lifetime = 8f;

            [NonSerialized] internal readonly List<GameObject> pool = new List<GameObject>();
            [NonSerialized] internal float nextSpawn;
        }

        [SerializeField] private HazardWave[] waves = Array.Empty<HazardWave>();
        private bool running;
        private float elapsed;

        public bool IsRunning => running;
        public int ActiveCount
        {
            get
            {
                int count = 0;
                foreach (HazardWave wave in waves)
                {
                    if (wave == null) continue;
                    foreach (GameObject item in wave.pool)
                        if (item != null && item.activeSelf) count++;
                }
                return count;
            }
        }

        private void Awake() => EnsurePools();
        private void OnDisable() => StopAndClear();

        private void Update()
        {
            if (!running) return;
            elapsed += Time.deltaTime;
            for (int i = 0; i < waves.Length; i++) TickWave(waves[i]);
        }

        public void Begin(float elapsedTime = 0f)
        {
            EnsurePools();
            StopAndClear();
            elapsed = Mathf.Max(0f, elapsedTime);
            foreach (HazardWave wave in waves)
            {
                float interval = Mathf.Max(0.01f, wave.interval);
                wave.nextSpawn = elapsed <= wave.startTime
                    ? wave.startTime
                    : wave.startTime + Mathf.Ceil((elapsed - wave.startTime) / interval) * interval;
            }
            running = true;
        }

        public void StopAndClear()
        {
            running = false;
            for (int i = 0; i < waves.Length; i++)
            {
                if (waves[i] == null) continue;
                for (int j = 0; j < waves[i].pool.Count; j++)
                    if (waves[i].pool[j] != null) waves[i].pool[j].SetActive(false);
            }
        }

        private void TickWave(HazardWave wave)
        {
            float end = wave.startTime + wave.duration;
            if (elapsed < wave.startTime || elapsed > end || elapsed < wave.nextSpawn) return;

            Spawn(wave);
            wave.nextSpawn += Mathf.Max(0.01f, wave.interval);
        }

        private void EnsurePools()
        {
            if (waves == null) waves = Array.Empty<HazardWave>();
            foreach (HazardWave wave in waves)
            {
                if (wave == null || wave.prefab == null) continue;
                int capacity = Mathf.Max(1, wave.poolSize);
                for (int i = wave.pool.Count; i < capacity; i++) wave.pool.Add(Create(wave));
            }
        }

        private GameObject Create(HazardWave wave)
        {
            GameObject instance = Instantiate(wave.prefab, transform);
            instance.name = wave.prefab.name + " (Pooled)";
            PooledEncounterHazard lifetime = instance.GetComponent<PooledEncounterHazard>();
            if (lifetime == null) lifetime = instance.AddComponent<PooledEncounterHazard>();
            lifetime.Configure(wave.lifetime);
            instance.SetActive(false);
            return instance;
        }

        private void Spawn(HazardWave wave)
        {
            for (int i = 0; i < wave.pool.Count; i++)
            {
                GameObject instance = wave.pool[i];
                if (instance == null)
                {
                    wave.pool[i] = Create(wave);
                    instance = wave.pool[i];
                }
                if (instance.activeSelf) continue;

                float min = Mathf.Min(wave.horizontalRange.x, wave.horizontalRange.y);
                float max = Mathf.Max(wave.horizontalRange.x, wave.horizontalRange.y);
                instance.transform.position = (Vector2)transform.position
                    + new Vector2(UnityEngine.Random.Range(min, max), wave.spawnHeight);
                instance.transform.rotation = Quaternion.identity;
                if (instance.TryGetComponent(out Rigidbody2D body))
                {
                    body.linearVelocity = Vector2.zero;
                    body.angularVelocity = 0f;
                }
                instance.SetActive(true);
                return;
            }
        }

        private void OnValidate()
        {
            if (waves == null) return;
            foreach (HazardWave wave in waves)
            {
                if (wave == null) continue;
                wave.poolSize = Mathf.Max(1, wave.poolSize);
                wave.interval = Mathf.Max(0.01f, wave.interval);
                wave.duration = Mathf.Max(0f, wave.duration);
                wave.lifetime = Mathf.Max(0.1f, wave.lifetime);
            }
        }
    }

    /// <summary>Returns a surviving pooled hazard after a scaled-time lifetime.</summary>
    internal sealed class PooledEncounterHazard : MonoBehaviour
    {
        private float configuredLifetime = 8f;
        private float remaining;

        public void Configure(float lifetime) => configuredLifetime = Mathf.Max(0.1f, lifetime);
        private void OnEnable() => remaining = configuredLifetime;

        private void Update()
        {
            remaining -= Time.deltaTime;
            if (remaining <= 0f) gameObject.SetActive(false);
        }
    }
}
