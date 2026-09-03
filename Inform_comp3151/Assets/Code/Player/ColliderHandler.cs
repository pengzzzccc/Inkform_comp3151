using Inkform.Bus;
using UnityEngine;

namespace Inkform.Player
{
    /// <summary>
    /// Event-driven body collider switching: sets the BoxCollider2D's size/offset by PlayerState.
    /// Switches by "state" rather than per frame — stable, no jitter; the standard body is the default,
    /// only states configured in overrides change the body.
    /// </summary>
    public class ColliderHandler : MonoBehaviour
    {
        [System.Serializable]
        public struct StateCollider
        {
            public PlayerState state;
            public Vector2 size;
            public Vector2 offset;
        }

        [SerializeField] private BoxCollider2D body;
        [SerializeField] private Vector2 defaultSize = Vector2.one;
        [SerializeField] private Vector2 defaultOffset = Vector2.zero;
        [SerializeField] private StateCollider[] overrides;

        void OnEnable()
        {
            PlayerBus.StateChanged += HandleState;
            HandleState(PlayerBus.State);    // initial sync from the snapshot
        }

        void OnDisable()
        {
            PlayerBus.StateChanged -= HandleState;
        }

        private void HandleState(PlayerState state)
        {
            if (body == null) return;

            Vector2 size = defaultSize;
            Vector2 offset = defaultOffset;
            if (overrides != null)
            {
                foreach (var o in overrides)
                {
                    if (o.state == state)
                    {
                        size = o.size;
                        offset = o.offset;
                        break;
                    }
                }
            }

            body.size = size;
            body.offset = offset;
        }
    }
}
