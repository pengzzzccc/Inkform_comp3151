using System.Collections;
using UnityEngine;

namespace Inkform.Audio
{
    /// <summary>
    /// Per-scene audio request: drop one in a scene and it wires two things up. The music cue
    /// fades in when the scene loads (Start, not OnEnable: on the boot scene the AudioService
    /// may not have run Awake yet, and ordering must not decide whether music plays). Scene
    /// transitions fall silent first — SceneDirector stops the music as the screen fades out —
    /// so this fade-in always starts from silence: each scene plays only its own track, and a
    /// scene without a cue simply stays silent. There is deliberately no stop on disable: the
    /// transition that unloads the scene already faded the outgoing track out.
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

        [Tooltip("Fade-in seconds after the scene loads (scene transitions are silent before this)")]
        [SerializeField] private float fade = 1.5f;

        [Tooltip("When this scene has no Music Cue assigned: on = fade the music channel out on arrival (defense for arrivals that skipped a transition, e.g. pressing Play directly in a room scene); off = leave the music channel as-is. Transitions stop the music themselves, so a scene without a cue is silent either way")]
        [SerializeField] private bool silenceWhenNoMusic;

        [Header("Ambient one-shots")]
        [Tooltip("Ambient Cue for this scene: several background noises as variants (category Sfx, loop off, spatial off). Every interval one random variant plays — leave empty for scenes without ambient noises")]
        [SerializeField] private SoundCue ambient;

        [Tooltip("Interval range in seconds: every roll waits a random time between min and max, then plays one random variant")]
        [SerializeField] private Vector2 ambientInterval = new Vector2(4f, 9f);

        private Coroutine ambientLoop;

        void Start()
        {
            if (music != null) AudioService.PlayMusic(music, fade);
            else if (silenceWhenNoMusic) AudioService.StopMusic(fade);

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
                AudioService.Play(ambient);
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            // Tiny marker so a scene's audio request is visible in the scene view
            Gizmos.DrawWireCube(transform.position, Vector3.one * 0.5f);
        }
#endif
    }
}
