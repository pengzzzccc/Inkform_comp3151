using Inkform.Bus;
using Inkform.Progression;
using Inkform.Tool;
using UnityEngine;

namespace Inkform.Item
{
    /// <summary>Room-local, touch-collected currency. It deliberately bypasses the bomb inventory.</summary>
    [RequireComponent(typeof(Collider2D))]
    public sealed class CrystalShardPickup : MonoBehaviour
    {
        [SerializeField, Min(1)] private int amount = 1;
        private bool collected;

        private void Reset()
        {
            Collider2D hitbox = GetComponent<Collider2D>();
            if (hitbox != null) hitbox.isTrigger = true;
        }

        void OnCollisionEnter2D(Collision2D collision)
        {
            if (collected || !collision.gameObject.CompareTag(Tags.Player)) return;
            collected = true;
            if (RunProgressStore.AddCrystals(amount))
                ProgressionBus.RaiseCrystalCollected(amount, transform.position);
            Destroy(gameObject);
        }
    }
}
