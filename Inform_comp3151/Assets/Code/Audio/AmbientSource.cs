using UnityEngine;

namespace Inkform.Audio
{
    /// <summary>
    /// A looping world sound: campfire crackle, waterfall, cave wind. Registers a persistent
    /// voice with AudioManager on enable and releases it on disable — the manager's per-frame
    /// refresh does the rest: volume swells as the listener approaches, the low-pass cutoff and
    /// reverb wet follow the zones between emitter and listener. This is a point emitter; the
    /// audible radius comes from the Cue (spatial + falloffRange), same knob one-shots use.
    /// </summary>
    public class AmbientSource : MonoBehaviour
    {
        [Tooltip("Looping Cue for this emitter. Keep spatial on and set falloffRange to the " +
            "audible radius; loop is forced on by the manager either way")]
        [SerializeField] private SoundCue cue;

        [Tooltip("Emitter offset from this transform, for placing the sound beside the visual")]
        [SerializeField] private Vector3 offset;

        private AudioSource voice;
        private float retryAt;

        // Guarded like every other audio caller: a missing Cue slot or a scene without the
        // manager must never throw — silence is the degradation, not an error
        void OnEnable()
        {
            voice = null;
            retryAt = 0f;
            if (cue == null || AudioManager.Instance == null) return;
            voice = AudioManager.Instance.RegisterAmbient(cue, transform.position + offset);
        }

        // A loop can lose its voice without anyone telling this component: the arbiter steals it
        // under pool pressure, and teardown can recycle it. Without this poll the emitter stays
        // silent for the rest of the scene — with several lasers in a room that is the normal case,
        // not the exception. The retry cooldown keeps a full pool from being hammered every frame.
        void Update()
        {
            if (cue == null) return;
            AudioManager manager = AudioManager.Instance;
            if (manager == null) return;
            if (voice != null && manager.IsVoiceActive(voice)) return;
            if (Time.unscaledTime < retryAt) return;

            voice = manager.RegisterAmbient(cue, transform.position + offset);
            retryAt = Time.unscaledTime + 0.5f;
        }

        // Explicit release, not isPlaying-based recycling: loops never finish "naturally", and the
        // voice must return to the pool the moment this emitter leaves the scene, not linger
        void OnDisable()
        {
            if (voice == null) return;
            if (AudioManager.Instance != null) AudioManager.Instance.ReleaseAmbient(voice);
            voice = null;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (cue == null) return;
            // Same radius the runtime premix uses, so what level designers see is what players hear
            Gizmos.color = new Color(1f, 0.55f, 0.15f, 0.8f);
            Gizmos.DrawWireSphere(transform.position + offset, cue.spatial ? cue.falloffRange : 1f);
        }
#endif
    }
}
