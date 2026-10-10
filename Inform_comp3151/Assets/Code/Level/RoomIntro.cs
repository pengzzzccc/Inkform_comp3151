using System.Collections;
using Inkform.Audio;
using Inkform.Fx;
using Inkform.Player;
using Inkform.Tool;
using UnityEngine;

namespace Inkform.Level
{
    /// <summary>
    /// New-game establishing shot. SceneDirector owns the lifecycle so the bars and camera are
    /// prepared while SceneFader is still opaque, the intro cue and the authored delay start together
    /// as the curtain begins to open, and player control is restored before the bars leave. Merely
    /// placing this component in a room does not make editor cold starts, continues, or door
    /// arrivals replay the cinematic.
    /// </summary>
    public sealed class RoomIntro : MonoBehaviour
    {
        [Header("Shot")]
        [SerializeField] private Transform camAnchor;

        [Header("Audio")]
        [Tooltip("Played as the scene curtain starts to open, when the pre-spawn wait begins")]
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
        private bool holding;        // intro cue posted, spawn countdown running
        private float holdElapsed;

        /// <summary>Called under the opaque scene fader, before the new room is revealed.</summary>
        public void PrepareBeforeReveal(RespawnDirector respawn)
        {
            if (prepared) return;
            prepared = true;
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

        /// <summary>Starts the intro cue and the spawn countdown. Called by SceneDirector right as
        /// the curtain starts to open, so the cue begins while the screen is still mostly covered —
        /// the cue is authored to land spawnDelay seconds later, together with the spawn.</summary>
        public void BeginHold()
        {
            if (holding) return;
            holding = true;
            holdElapsed = 0f;

            // This is a direct presentation post, not a LifeBus respawn: a fresh entrance must not
            // trigger death-respawn listeners merely to make its authored intro sound audible.
            AudioService.Play(introCue);
        }

        private void Update()
        {
            // Capped step: load hitches must not eat the hold
            if (holding) holdElapsed += PresentationTime.UnscaledStep;
        }

        /// <summary>Runs after SceneFader.FadeIn has completed; waits out whatever of the hold the
        /// reveal did not already cover, then spawns the player.</summary>
        public IEnumerator WaitAndSpawn(RespawnDirector respawn)
        {
            BeginHold();   // idempotent: covers callers that skipped the reveal-time start

            while (holdElapsed < spawnDelay) yield return null;
            holding = false;

            PlayerHandler player = respawn != null
                ? respawn.SpawnPlayerAtSceneStart()
                : null;
            if (player == null)
                Debug.LogError("RoomIntro: failed to instantiate the player; releasing the presentation safely", this);
        }

        /// <summary>Runs after SceneDirector has restored gameplay state and player input.</summary>
        public IEnumerator FinishAfterControl()
        {
            // Both actions start in this frame: the player is already controllable, the camera eases
            // out of its establishing position while the movie bars retract around the live scene.
            if (cam != null) cam.ResumeFollow();
            if (bars != null) yield return bars.Hide(barsSeconds);
            prepared = false;
        }

        private void OnDestroy()
        {
            // A forced scene switch or play-mode stop must never leave a surviving camera held.
            if (prepared && cam != null) cam.ResumeFollow();
            if (prepared && respawn != null) respawn.ReleaseSceneInitialization();
            prepared = false;
        }
    }
}
