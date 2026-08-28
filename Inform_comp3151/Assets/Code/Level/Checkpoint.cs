using Inkform.Audio;
using Inkform.Bus;
using Inkform.Item;
using Inkform.Tool;
using UnityEngine;

namespace Inkform.Level
{
    /// <summary>
    /// Checkpoint: a timecard machine. Touching it with an identity card in the inventory stamps the
    /// card and records the respawn position here; touching it without the card only plays a denial
    /// sound and changes nothing. The one with isStartPoint checked also serves as the level spawn
    /// point — at startup RespawnDirector teleports the player there, so "spawn point" and "checkpoint"
    /// remain the same kind of object, just gated behind the card now.
    ///
    /// Presentation is a hand-driven sprite sequence rather than an Animator: frame 0 stands resident
    /// while idle, activation steps through every frame once and freezes on the last one — which falls
    /// out of simply never writing another sprite afterwards (same manual sprite-swapping pattern as
    /// ExplodePart's trigger frames). Needs an Is-Trigger collider.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class Checkpoint : MonoBehaviour
    {
        public const string TimeCardItemId = "time-card";

        [Header("Checkpoint setting")]
        [SerializeField] private bool isStartPoint = false;                     // checked = also the level spawn point; only one per level
        [SerializeField] private Vector2 spawnOffset = new Vector2(0f, 0.5f);   // raised a bit so respawn feet do not sink into the ground and get pushed out

        [Header("Timecard machine look")]
        [Tooltip("Activation sequence: first frame = idle resident, last frame = stamped freeze-frame")]
        [SerializeField] private Sprite[] frames;
        [SerializeField] private float framesPerSecond = 12f;
        [Tooltip("Played when a player without an identity card touches the machine")]
        [SerializeField] private SoundCue deniedCue;

        private SpriteRenderer spriteRenderer;
        private Coroutine playingAnimation;
        private bool active;    // stamped: passing through again must not re-raise feedback

        public bool IsStartPoint => isStartPoint;
        public Vector2 SpawnPos => (Vector2)transform.position + spawnOffset;

        void Awake()
        {
            // The idle look IS the first animation frame, so editor authoring and runtime can never drift apart
            spriteRenderer = GetComponent<SpriteRenderer>();
            ShowFrame(0);
        }

        void OnTriggerEnter2D(Collider2D other)
        {
            if (active) return;
            if (!other.CompareTag(Tags.Player)) return;

            // No identity card: the machine refuses — a sound and nothing else. Re-entering replays it,
            // which mirrors how a failed dash keeps its denied cue on every attempt.
            if (!InventoryStore.Owns(TimeCardItemId))
            {
                PlayCue(deniedCue);
                return;
            }

            active = true;
            LifeBus.RaiseCheckpointSet(SpawnPos);
            PlayActivationSequence();
        }

        private void PlayActivationSequence()
        {
            if (playingAnimation != null) StopCoroutine(playingAnimation);
            playingAnimation = StartCoroutine(PlayFrames());
        }

        private System.Collections.IEnumerator PlayFrames()
        {
            if (frames != null && frames.Length > 0)
            {
                float step = 1f / Mathf.Max(framesPerSecond, 0.01f);
                for (int i = 0; i < frames.Length; i++)
                {
                    ShowFrame(i);
                    yield return new WaitForSeconds(step);
                }
                // Ending mid-sequence leaves the final frame written — the freeze-frame comes for free
            }
            playingAnimation = null;
        }

        private void ShowFrame(int index)
        {
            if (spriteRenderer == null) return;
            if (frames == null || frames.Length == 0) return;
            spriteRenderer.sprite = frames[Mathf.Clamp(index, 0, frames.Length - 1)];
        }

        // Same guard as AudioDirector: a missing cue or missing manager degrades to silence, not errors
        private static void PlayCue(SoundCue cue)
        {
            if (cue == null || AudioManager.Instance == null) return;
            AudioManager.Instance.Play(cue);
        }

        // OnDrawGizmos rather than ...Selected: while placing levels you must be able to scan all
        // respawn points at a glance and spot a missing spawn point — clicking each one is too slow
        void OnDrawGizmos()
        {
            bool touched = Application.isPlaying && active;
            Color c = isStartPoint ? new Color(0.35f, 1f, 0.45f) : new Color(0.35f, 0.85f, 1f);

            // Trigger range: the player must enter this box to count as touching. Read the collider
            // directly, so the gizmo and the actual check never diverge — tuning one and forgetting the other
            Collider2D box = GetComponent<Collider2D>();
            if (box != null && box.bounds.size.sqrMagnitude > 0f)
            {
                Gizmos.color = touched ? c : c * 0.55f;
                Gizmos.DrawWireCube(box.bounds.center, box.bounds.size);
            }

            // The true respawn coordinate. A line to it shows at a glance how high spawnOffset is —
            // if this point is buried in the ground, the player gets physically pushed out at respawn
            Gizmos.color = c;
            Gizmos.DrawLine(transform.position, SpawnPos);

            if (touched) Gizmos.DrawSphere(SpawnPos, 0.14f);        // solid = stepped on this run
            else Gizmos.DrawWireSphere(SpawnPos, 0.14f);

            // The spawn point gets an extra cross to distinguish it from plain checkpoints (only one per level)
            if (!isStartPoint) return;
            Gizmos.DrawLine(SpawnPos + Vector2.left * 0.3f, SpawnPos + Vector2.right * 0.3f);
            Gizmos.DrawLine(SpawnPos + Vector2.down * 0.3f, SpawnPos + Vector2.up * 0.3f);
        }
    }
}
