using UnityEngine;

namespace Inkform.Audio
{
    /// <summary>
    /// Per-scene music request: drop one in a scene with a Music Cue and the track crossfades in
    /// when the scene loads (Start, not OnEnable: on the boot scene the AudioManager may not have
    /// run Awake yet, and ordering must not decide whether music plays). Re-requesting the
    /// already-playing track is a no-op, so re-entering a scene never restarts its music.
    /// There is deliberately no stop on disable: the outgoing track simply crossfades when the
    /// next scene brings its own request, and a scene without one inherits the previous track.
    /// </summary>
    public class SceneMusic : MonoBehaviour
    {
        [Tooltip("Music Cue for this scene: category Music, loop on, spatial off")]
        [SerializeField] private SoundCue music;

        [Tooltip("Crossfade seconds on track change")]
        [SerializeField] private float fade = 1.5f;

        void Start()
        {
            if (music == null || AudioManager.Instance == null) return;
            AudioManager.Instance.PlayMusic(music, fade);
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            // Tiny marker so a scene's music request is visible in the hierarchy view
            Gizmos.DrawIcon(transform.position, "AudioManager", true);
        }
#endif
    }
}
