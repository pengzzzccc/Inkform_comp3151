using System.Collections;
using Inkform.Audio;
using Inkform.Fx;
using Inkform.Player;
using UnityEngine;

namespace Inkform.Level
{
    /// <summary>
    /// New-game establishing shot. SceneDirector owns the lifecycle so the bars and camera are
    /// prepared while SceneFader is still opaque, the authored delay starts only after FadeIn has
    /// completed, and player control is restored before the bars leave. Merely placing this component
    /// in a room does not make editor cold starts, continues, or door arrivals replay the cinematic.
    /// </summary>
    public sealed class RoomIntro : MonoBehaviour
    {
        [Header("Shot")]
        [SerializeField] private Transform camAnchor;

        [Header("Audio")]
        [Tooltip("Played when the fade-in has completed and the pre-spawn wait begins")]
        [SerializeField] private SoundCue introCue;

        [Header("Timing")]
        [Min(0f)]
        [SerializeField] private float spawnDelay = 1f;
        [Min(0f)]
        [SerializeField] private float barsSeconds = 1f;

        private CinematicBars bars;
        private CamHandler cam;
        private RespawnDirector respawn;
        private bool prepared;

        public bool SpawnSucceeded { get; private set; }

        /// <summary>Called under the opaque scene fader, before the new room is revealed.</summary>
        public void PrepareBeforeReveal(RespawnDirector respawn)
        {
            if (prepared) return;
            prepared = true;
            SpawnSucceeded = false;
            this.respawn = respawn;

            bars = GetComponent<CinematicBars>();
            if (bars == null) bars = gameObject.AddComponent<CinematicBars>();
            bars.ShowInstant();

            cam = FindAnyObjectByType<CamHandler>();
            if (cam == null)
                Debug.LogError("RoomIntro: no CamHandler exists in the scene; the intro will continue without a staged camera", this);
            else
            {
                if (camAnchor == null)
                    Debug.LogError("RoomIntro: camAnchor is unassigned; holding the camera at its current position", this);
                cam.HoldAt(camAnchor);
            }

            if (respawn == null)
                Debug.LogError("RoomIntro: no RespawnDirector is available; the player cannot be spawned", this);
            else
                respawn.HoldSceneInitialization();
        }

        /// <summary>Starts after SceneFader.FadeIn has fully completed.</summary>
        public IEnumerator WaitAndSpawn(RespawnDirector respawn)
        {
            // This is a direct presentation post, not a LifeBus respawn: a fresh entrance must not
            // trigger death-respawn listeners merely to make its authored intro sound audible.
            AudioManager.Instance?.Play(introCue);

            float remaining = Mathf.Max(0f, spawnDelay);
            while (remaining > 0f)
            {
                remaining -= Time.unscaledDeltaTime;
                yield return null;
            }

            PlayerHandler player = respawn != null
                ? respawn.SpawnPlayerAtSceneStart(false)
                : null;
            SpawnSucceeded = player != null;
            if (!SpawnSucceeded)
                Debug.LogError("RoomIntro: failed to instantiate the player; releasing the presentation safely", this);
        }

        /// <summary>Runs after SceneDirector has restored gameplay state and player input.</summary>
        public IEnumerator FinishAfterControl()
        {
            // Both actions start in this frame: the player is already controllable, the camera eases
            // out of its establishing position while the movie bars retract around the live scene.
            if (cam != null) cam.ResumeFollow(false);
            if (bars != null) yield return bars.Hide(barsSeconds);
            prepared = false;
        }

        private void OnDestroy()
        {
            // A forced scene switch or play-mode stop must never leave a surviving camera held.
            if (prepared && cam != null) cam.ResumeFollow(false);
            if (prepared && respawn != null) respawn.ReleaseSceneInitialization();
            prepared = false;
        }
    }
}
