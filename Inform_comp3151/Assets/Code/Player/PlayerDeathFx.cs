using Inkform.Bus;
using Inkform.Life;
using UnityEngine;

namespace Inkform.Player
{
    /// <summary>
    /// The player death's presentation side: exposes "the body" for the death strategy to manipulate.
    /// This class only answers three questions — how big the body looks, how to hide it, how to show
    /// it; whether hiding is followed by bursting into shards or a slow fade is the DeathStrategy's
    /// algorithm, unknown to this class.
    ///
    /// Being separate from PlayerHandler is deliberate — that side handles gameplay only (stop physics,
    /// lock input, teleport), this side handles the body's visibility only. Attach to the Player (needs
    /// its SpriteRenderer).
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class PlayerDeathFx : MonoBehaviour, IDeathBody
    {
        private SpriteRenderer sprite;

        void Awake()
        {
            sprite = GetComponent<SpriteRenderer>();
        }

        void OnEnable()
        {
            LifeBus.Respawned += OnRespawned;
        }

        void OnDisable()
        {
            LifeBus.Respawned -= OnRespawned;
        }

        // SpriteRenderer.bounds rather than the collider's, for two reasons:
        // ① shards should be sliced along the "visible silhouette", and the player collider is a
        //    0.5×0.5 capsule, a size smaller than the art;
        // ② the collider's bounds die once PlayerHandler disables physics, and which of the two
        //    components receives the same event first is not guaranteed — the renderer's bounds avoid
        //    that ordering pitfall entirely
        public Bounds VisualBounds => sprite.bounds;

        // The body and the shards must not overlap visually; disable rendering rather than
        // SetActive(false): the latter triggers OnDisable unsubscribing the buses, and "respawn"
        // would never arrive
        public void Hide() => sprite.enabled = false;

        public void Show() => sprite.enabled = true;

        // Death is driven by DeathDirector (which has the strategy); respawn needs no strategy — no
        // matter how the player died, showing the body back is the same thing, so this half still
        // listens to the bus directly
        private void OnRespawned(GameObject victim, Vector2 pos)
        {
            if (victim != gameObject) return;

            Show();
        }
    }
}
