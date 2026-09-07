using System.Collections;
using UnityEngine;

namespace Inkform.Audio
{
    /// <summary>
    /// Per-scene audio request: drop one in a scene and it wires two things up. The music cue
    /// crossfades in when the scene loads (Start, not OnEnable: on the boot scene the AudioManager
    /// may not have run Awake yet, and ordering must not decide whether music plays). Re-requesting
    /// the already-playing track is a no-op, so re-entering a scene never restarts its music.
    /// There is deliberately no stop on disable: the outgoing track simply crossfades when the
    /// next scene brings its own request, and a scene without one inherits the previous track.
    ///
    /// The ambient cue is the second feature: a pool of background noises (drips, wind, creaks)
    /// where one random variant plays at a random interval rolled between min and max seconds, for
    /// as long as the scene lives. The loop's lifetime is the component's — scene unload destroys
    /// it and the scheduling stops with it; a one-shot already sounding simply finishes.
    /// </summary>
    public class SceneMusic : MonoBehaviour
    {
        [Tooltip("Music Cue for this scene: category Music, loop on, spatial off")]
        [SerializeField] private SoundCue music;

        [Tooltip("Crossfade seconds on track change")]
        [SerializeField] private float fade = 1.5f;

        [Tooltip("When this scene has no Music Cue assigned: on = fade the incoming track out (the main menu wants silence, or will bring its own cue later); off = inherit it, so a gameplay room without its own track keeps the previous one playing")]
        [SerializeField] private bool silenceWhenNoMusic;

        [Header("Ambient one-shots")]
        [Tooltip("Ambient Cue for this scene: several background noises as variants (category Sfx, loop off, spatial off). Every interval one random variant plays — leave empty for scenes without ambient noises")]
        [SerializeField] private SoundCue ambient;

        [Tooltip("Interval range in seconds: every roll waits a random time between min and max, then plays one random variant")]
        [SerializeField] private Vector2 ambientInterval = new Vector2(4f, 9f);

        private Coroutine ambientLoop;

        void Start()
        {
            if (AudioManager.Instance == null) return;

            if (music != null) AudioManager.Instance.PlayMusic(music, fade);
            else if (silenceWhenNoMusic) AudioManager.Instance.StopMusic(fade);

            if (ambient != null) ambientLoop = StartCoroutine(AmbientLoop());
        }

        void OnDisable()
        {
            // The scene going away (or the object being toggled off) ends the scheduling; a
            // one-shot already sounding is the AudioSource's own business and simply finishes
            if (ambientLoop != null)
            {
                StopCoroutine(ambientLoop);
                ambientLoop = null;
            }
        }

        // Wait first, play second: a scene should not open with a noise the instant it appears,
        // and every later roll lands somewhere inside the min..max window
        private IEnumerator AmbientLoop()
        {
            while (true)
            {
                yield return new WaitForSeconds(Random.Range(ambientInterval.x, ambientInterval.y));
                AudioManager.Instance.Play(ambient);
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            // Tiny marker so a scene's audio request is visible in the hierarchy view
            Gizmos.DrawIcon(transform.position, "AudioManager", true);
        }
#endif
    }
}
