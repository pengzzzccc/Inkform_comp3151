using System.Collections.Generic;
using Inkform.Bus;
using Inkform.Tool;
using UnityEngine;

namespace Inkform.Level
{
    /// <summary>
    /// Rope gun range override zone: entering it changes the rope gun's range to the zone's value via
    /// RopeGunBus, leaving restores the default. Needs an Is-Trigger collider on this object
    /// (suggested: cover the whole special area).
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class RopeRangeZone : MonoBehaviour
    {
        [SerializeField] private float maxRange = 4f;      // rope gun max range inside the zone
        private readonly HashSet<Collider2D> playerColliders = new HashSet<Collider2D>();

        void OnTriggerEnter2D(Collider2D other)
        {
            if (!other.CompareTag(Tags.Player)) return;
            if (playerColliders.Add(other) && playerColliders.Count == 1)
                RopeGunBus.RaiseRangeOverride(this, maxRange);
        }

        void OnTriggerExit2D(Collider2D other)
        {
            if (!other.CompareTag(Tags.Player)) return;
            if (playerColliders.Remove(other) && playerColliders.Count == 0)
                RopeGunBus.RaiseRangeRestored(this);
        }

        private void OnDisable()
        {
            if (playerColliders.Count > 0) RopeGunBus.RaiseRangeRestored(this);
            playerColliders.Clear();
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.9f, 0.2f, 0.4f);
            Gizmos.DrawWireCube(transform.position, transform.localScale);
        }
    }
}
