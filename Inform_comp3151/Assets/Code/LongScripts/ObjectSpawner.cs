using UnityEngine;

public class ObjectSpawner : MonoBehaviour
{
    [Header("Spawner Settings")]
    [SerializeField] private GameObject prefabToSpawn;
    [SerializeField] private float spawnInterval = 3.0f; // X seconds
    [SerializeField] private Transform spawnParent; // Optional: parent container to keep hierarchy clean

    private float timer;

    private void Update()
    {
        HandleTimerSpawn();
    }

    private void HandleTimerSpawn()
    {
        timer += Time.deltaTime;

        if (timer >= spawnInterval)
        {
            SpawnObject();
            timer = 0f; // Reset timer
        }
    }

    public void SpawnObject()
    {
        if (prefabToSpawn == null)
        {
            Debug.LogWarning("Prefab to spawn is not assigned in the inspector!", this);
            return;
        }

        // Instantiate at this GameObject's position and rotation
        GameObject spawnedObj = Instantiate(prefabToSpawn, transform.position, transform.rotation);

        // Optionally parent it to keep the hierarchy organized
        if (spawnParent != null)
        {
            spawnedObj.transform.SetParent(spawnParent);
        }
    }
}