using Inkform.Save;
using UnityEngine;
using UnityEngine.UI;

namespace Inkform.UI
{
    /// <summary>
    /// "Saved" toast: the corner flash that tells the player an autosave just landed on disk (a room
    /// entered, a checkpoint stamped, an item picked up, Save &amp; Quit). SaveStore writes silently
    /// otherwise, and in a game whose whole loop is checkpoints, "did it save?" is worth one glance.
    ///
    /// Subscribes to SaveStore.Saved — not Changed, which also fires for in-memory updates like
    /// BeginNewRun/Delete that write nothing to confirm. Its label is serialized in HudRoot.prefab,
    /// which UIManager owns alongside the menu layer.
    ///
    /// Timed with unscaled time: Save &amp; Quit happens while the pause menu has timeScale at 0, and
    /// its final save deserves the same flash as any other.
    /// </summary>
    public sealed class SaveIndicator : MonoBehaviour
    {
        private const float HoldSeconds = 1f;
        private const float FadeSeconds = 0.4f;

        [SerializeField] private Text label;
        private float remaining;

        private void Awake()
        {
            if (label != null) label.enabled = false;
        }

        private void OnEnable()
        {
            SaveStore.Saved += Show;
        }

        private void OnDisable()
        {
            SaveStore.Saved -= Show;
        }

        private void Show()
        {
            if (label == null) return;
            remaining = HoldSeconds + FadeSeconds;
            label.enabled = true;
            SetAlpha(1f);
        }

        private void Update()
        {
            if (remaining <= 0f) return;

            remaining = Mathf.Max(0f, remaining - Time.unscaledDeltaTime);
            SetAlpha(Mathf.Clamp01(remaining / FadeSeconds));
            if (remaining <= 0f) label.enabled = false;
        }

        private void SetAlpha(float alpha)
        {
            Color color = label.color;
            color.a = alpha;
            label.color = color;
        }
    }
}
