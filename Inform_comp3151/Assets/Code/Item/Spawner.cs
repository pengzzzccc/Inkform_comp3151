using Inkform.Bus;
using UnityEngine;

public class Spawner : MonoBehaviour
{
    [SerializeField] private float spawneraTimer = 5f;
    [SerializeField] private GameObject spawneraObject;
    private Timer timer;
    private bool isSpawn = true;

    void Start()
    {
        timer.Set(spawneraTimer);
        Instantiate(spawneraObject, transform);
    }

    void Update()
    {
        if (!isSpawn && !timer.IsRunning)
        {
            timer.Set(spawneraTimer);
            Instantiate(spawneraObject, transform);
            isSpawn = true;
        }
    }

    private void bombExploded(GameObject victim, Vector2 center, float force)
    {
        isSpawn = false;
    }

    void OnEnable()
    {
        HazardBus.Exploded += bombExploded;
    }

    void OnDisable()
    {
        HazardBus.Exploded -= bombExploded;
    }

}
