using UnityEngine;

namespace Inkform.Life
{
    /// <summary>
    /// The corpse's operation surface: the strategy manipulates the deceased's body through it, never
    /// knowing PlayerDeathFx / SpriteRenderer directly. So "who is the corpse" and "how to handle the
    /// corpse" are decoupled — a future enemy that can die just implements this interface.
    ///
    /// Only currently-used members live here. Add FadeOut(float) when a FadeDeathStrategy (fade
    /// without shattering) arrives — that is when the interface should grow; an uncalled method now
    /// would just be dead code.
    /// </summary>
    public interface IDeathBody
    {
        /// <summary>The body's visible bounds; shards are sliced against it.
        /// Must be captured before Hide() — after rendering/collision are off, bounds degenerate to
        /// zero size at the origin.</summary>
        Bounds VisualBounds { get; }

        /// <summary>Hides the body. Implementors must avoid SetActive(false): that triggers OnDisable
        /// unsubscribing the buses, and "respawn" would never arrive. Disabling rendering suffices.</summary>
        void Hide();

        /// <summary>Shows the body back on respawn.</summary>
        void Show();

        /// <summary>
        /// Flattens the body in place (crush death): the visible body squashes by scaleMultiplier
        /// over duration — x widens, y flattens — and stays visible. No shards, no hide. Implementors
        /// must restore the original scale on respawn, so the squash never leaks into the next life.
        /// </summary>
        void Flatten(Vector2 scaleMultiplier, float duration);
    }
}
