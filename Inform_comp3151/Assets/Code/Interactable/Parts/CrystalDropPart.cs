using Inkform.Bus;
using UnityEngine;

namespace Inkform.Interactable.Parts
{
    /// <summary>Spawns room-local crystal shards once when this formation is destroyed.</summary>
    public sealed class CrystalDropPart : MonoBehaviour, IInteractablePart
    {
        [SerializeField] private GameObject crystalShardPrefab;
        [SerializeField, Min(1)] private int dropCount = 3;
        [SerializeField, Min(0f)] private float scatterSpeed = 2.5f;

        private Interactable root;
        private bool dropped;

        public void Attach(Interactable interactable) => root = interactable;
        public bool HandleContact(ContactPhase phase, Collider2D other) => false;

        private void OnEnable() => HazardBus.Exploded += OnExploded;
        private void OnDisable() => HazardBus.Exploded -= OnExploded;

        private void OnExploded(GameObject victim, Vector2 center, float force)
        {
            if (dropped || root == null || victim != root.gameObject) return;
            dropped = true;
            ProgressionBus.RaiseCrystalFormationDestroyed(root.transform.position);

            if (crystalShardPrefab == null)
            {
                Debug.LogWarning($"{name}: CrystalDropPart has no Crystal Shard prefab.", this);
                return;
            }

            for (int i = 0; i < dropCount; i++)
            {
                float angle = 360f * i / dropCount + Random.Range(-18f, 18f);
                Vector2 direction = Quaternion.Euler(0f, 0f, angle) * Vector2.up;
                GameObject shard = Instantiate(crystalShardPrefab, transform.position, Quaternion.identity);
                if (shard.TryGetComponent(out Rigidbody2D body))
                    body.linearVelocity = direction * scatterSpeed;
            }
        }
    }
}
