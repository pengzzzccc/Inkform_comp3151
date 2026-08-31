using Inkform.Tool;
using UnityEngine;

namespace Inkform.Audio
{
    /// <summary>
    /// A volume of space with its own acoustic (its AudioZoneProfile). The player entering pushes
    /// the profile onto the listener's zone stack, leaving pops it. Muffling and volume scale
    /// blend strictest-wins into every zoned sound — one-shots snapshot them at post time,
    /// persistent loops re-read them each frame — while the zone's reverb wet drives one global
    /// filter on the AudioListener, so everything heard inside the cave carries a tail that rings
    /// out naturally. The player is identified by the Player tag, same convention as Checkpoint;
    /// the AudioListener rides on the camera, which has no collider of its own.
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
        // Always-on, size-accurate volume: the shape drawn is the collider's actual world shape
        // (rotation and scale included through the local-to-world matrix), dim when unselected so
        // a level full of zones stays readable, bright with an outline and the profile name when
        // selected. What designers see is exactly what the trigger covers
        private void OnDrawGizmos() => DrawZoneGizmo(false);

        private void OnDrawGizmosSelected()
        {
            DrawZoneGizmo(true);
            if (profile != null)
                UnityEditor.Handles.Label(transform.position + Vector3.up * 1.5f, profile.name);
        }

        private void DrawZoneGizmo(bool selected)
        {
            // Gizmo code runs before Awake ever does in edit mode, so the cached collider is not
            // available here — fetch it per call, editor-only cost
            Collider2D collider = GetComponent<Collider2D>();
            if (collider == null) return;

            // Blue = dry/open, violet = wet; grey = no profile wired yet
            Gizmos.color = profile != null
                ? new Color(0.45f, 0.35f + 0.5f * profile.reverbWet, 1f, selected ? 0.5f : 0.13f)
                : new Color(0.5f, 0.5f, 0.5f, selected ? 0.4f : 0.1f);

            // Local-space drawing: the matrix carries the transform, the shape comes from the
            // collider itself — no fixed radius, no stale AABB
            Gizmos.matrix = transform.localToWorldMatrix;
            if (collider is BoxCollider2D box)
            {
                Vector3 center = box.offset;
                Gizmos.DrawCube(center, box.size);
                if (selected) Gizmos.DrawWireCube(center, box.size);
            }
            else if (collider is CircleCollider2D circle)
            {
                Vector3 center = circle.offset;
                Gizmos.DrawSphere(center, circle.radius);
                if (selected) Gizmos.DrawWireSphere(center, circle.radius);
            }
            else
            {
                // Composite/polygon/edge: fall back to the world-space bounds box
                Gizmos.matrix = Matrix4x4.identity;
                Gizmos.DrawCube(collider.bounds.center, collider.bounds.size);
            }
            Gizmos.matrix = Matrix4x4.identity;
        }
#endif
    }
}
