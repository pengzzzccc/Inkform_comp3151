using Inkform.Tool;
using UnityEngine;

namespace Inkform.Audio
{
    /// <summary>
    /// A volume of space with its own acoustic (its AudioZoneProfile). The player entering pushes
    /// the profile onto the listener's zone stack, leaving pops it, and AudioManager blends the
    /// strictest-wins mix into every zoned sound — one-shots snapshot it at post time, persistent
    /// loops re-read it each frame. The player is identified by the Player tag, same convention
    /// as Checkpoint; the AudioListener rides on the camera, which has no collider of its own.
    /// The collider must be a trigger; Reset enforces it in the editor.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class AudioZone : MonoBehaviour
    {
        [Tooltip("What this place sounds like")]
        [SerializeField] private AudioZoneProfile profile;

        private Collider2D bounds;

        // Guard against enter-without-exit pairs from physics quirks (teleports, disable toggles):
        // a zone either holds one push or none, never two
        private bool pushed;

        public ZoneMix Mix => profile != null ? profile.Mix : ZoneMix.Neutral;

        public bool Contains(Vector3 position) =>
            bounds != null && bounds.OverlapPoint(position);

        void Awake() => bounds = GetComponent<Collider2D>();

        // Registration is for the emitter-side lookup (AudioManager.StateAt): every live zone in
        // the scene can be asked whether a sound's origin lies inside it
        void OnEnable() => AudioManager.Instance?.RegisterZone(this);

        void OnDisable()
        {
            AudioManager.Instance?.UnregisterZone(this);
            if (pushed) PopFromMixer();     // disabled while the player was inside: still leave cleanly
        }

        void OnTriggerEnter2D(Collider2D other)
        {
            if (pushed || profile == null) return;
            if (!other.CompareTag(Tags.Player)) return;
            pushed = true;
            AudioManager.Instance?.PushListenerZone(this, profile.Mix, profile.blendIn);
        }

        void OnTriggerExit2D(Collider2D other)
        {
            if (!pushed || !other.CompareTag(Tags.Player)) return;
            PopFromMixer();
        }

        private void PopFromMixer()
        {
            pushed = false;
            if (profile != null)
                AudioManager.Instance?.PopListenerZone(this, profile.blendOut);
        }

        void Reset()
        {
            Collider2D collider = GetComponent<Collider2D>();
            if (collider != null) collider.isTrigger = true;    // zones observe, never collide
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            // Blue = dry/open, towards violet = wet; the shape shown is the exact trigger volume
            Gizmos.color = profile != null
                ? new Color(0.45f, 0.35f + 0.5f * profile.reverbWet, 1f, 0.55f)
                : new Color(0.5f, 0.5f, 0.5f, 0.4f);
            Collider2D collider = GetComponent<Collider2D>();
            if (collider != null) Gizmos.DrawCube(collider.bounds.center, collider.bounds.size);
        }
#endif
    }
}
