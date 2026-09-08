using System.Collections;
using System.Collections.Generic;
using Inkform.Bus;
using Inkform.Fx;
using Inkform.Input;
using Inkform.Player;
using Inkform.Tool;
using Inkform.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Inkform.Interactable.Parts
{
    /// <summary>
    /// Map view (reusable): the scene-map inspect mode. Pair with InteractionPromptPart on the same
    /// Interactable — the prompt (glow + confirm-key icon) is identical to the timecard machines,
    /// and the key is the same one. Pressing confirm in range locks all input, detaches the follow
    /// camera and glides it to the view anchor; pressing confirm again glides back, reattaches the
    /// follow and hands control over.
    ///
    /// The exit press cannot ride PlayerBus.InteractPressed: closing the input gate makes
    /// InputHandler disable every action AND unsubscribe its own performed handler, so the bus stays
    /// dead until the gate reopens. Instead, entering hooks this part's own handler directly onto
    /// the Interact action and enables just that one action — rebinding-aware (the shared wrapper is
    /// the same asset the prompt icon reads), and other interactables stay silent: the bus never
    /// sees the exit press at all.
    ///
    /// While viewing the player cannot move, act or die (input is gated and the body stands still),
    /// so the camera can leave without the world drifting out from under it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MapViewPart : MonoBehaviour, IInteractablePart
    {
        [Header("Map view")]
        [Tooltip("Where the camera glides while the map is inspected — park it framing the map art. z is ignored (the follow camera's z is kept).")]
        [SerializeField] private Transform viewAnchor;
        [Tooltip("Seconds for the glide to and from the anchor.")]
        [SerializeField, Min(0.01f)] private float glideSeconds = 0.6f;

        private Interactable root;
        private readonly HashSet<Collider2D> players = new HashSet<Collider2D>();

        private CamHandler cam;
        private InputAction hookedInteract;    // the action our exit handler is attached to, while viewing
        private bool viewing;                  // inspect mode active: input gated, follow detached
        private bool gliding;                  // a glide coroutine is mid-flight — presses are ignored

        public void Attach(Interactable interactable)
        {
            root = interactable;

            Collider2D rootCollider = root.GetComponent<Collider2D>();
            if (rootCollider != null && !rootCollider.isTrigger)
                Debug.LogWarning($"{root.name} map view collider should be a Trigger", root);
        }

        public bool HandleContact(ContactPhase phase, Collider2D other)
        {
            if (other == null || !other.CompareTag(Tags.Player)) return false;

            if (phase == ContactPhase.Enter) players.Add(other);
            else if (phase == ContactPhase.Exit) players.Remove(other);
            return false;   // presence only — never claims the contact
        }

        void OnEnable() => PlayerBus.InteractPressed += OnInteractPressed;
        void OnDisable()
        {
            PlayerBus.InteractPressed -= OnInteractPressed;
            UnhookExit();

            // Disabling this component also kills its coroutines mid-glide; whatever state the
            // view was in, the room must not keep a locked gate or a detached camera
            if (viewing)
            {
                Restore();
                viewing = false;
                gliding = false;
            }
        }

        private void OnDestroy()
        {
            UnhookExit();
            if (viewing) Restore();
        }

        // Entry only. While viewing the bus is quiet (InputHandler unsubscribed with the gate
        // closed) — the exit arrives through the directly hooked action handler instead
        private void OnInteractPressed()
        {
            if (viewing || gliding || root == null || LifeBus.IsDead) return;

            // Destroyed colliders must not keep an empty range flagging true (player torn down mid-contact)
            if (players.Count > 0) players.RemoveWhere(collider => collider == null);
            if (players.Count == 0 || PlayerBus.Player == null) return;

            StartCoroutine(EnterView());
        }

        private void OnExitPressed(InputAction.CallbackContext ctx) => Exit();

        private void Exit()
        {
            if (!viewing || gliding) return;
            UnhookExit();
            StartCoroutine(ExitView());
        }

        private void UnhookExit()
        {
            if (hookedInteract == null) return;
            hookedInteract.performed -= OnExitPressed;
            hookedInteract = null;
        }

        private IEnumerator EnterView()
        {
            viewing = true;
            gliding = true;

            // Lock every gameplay action, then hook the exit directly onto the Interact action and
            // enable just that one (the shared wrapper is the same asset InputHandler reads, so a
            // remapped confirm binding keeps working and matches the prompt icon)
            InputGate(false);
            hookedInteract = InputActions.Wrapper.Player.Interact;
            hookedInteract.performed += OnExitPressed;
            hookedInteract.Enable();

            cam = FindAnyObjectByType<CamHandler>();
            if (cam != null) cam.enabled = false;

            Vector2 anchor = viewAnchor != null ? (Vector2)viewAnchor.position : PlayerAnchor();
            yield return GlideTo(anchor);
            gliding = false;
        }

        private IEnumerator ExitView()
        {
            gliding = true;

            yield return GlideTo(PlayerAnchor());
            Restore();

            gliding = false;
            viewing = false;
        }

        // Follow resumes from wherever the glide ended: SmoothDamp continues from the current
        // position, so there is no snap even though the follow was off the whole time. SetPlaying
        // also runs InputHandler's EnableActions, which re-subscribes its own interact handler —
        // the bus is alive again and the next confirm press can re-enter the view
        private void Restore()
        {
            if (cam != null) cam.enabled = true;
            InputGate(true);
        }

        // Same guard as SceneDirector: a pause that raced the view keeps the gate closed
        private void InputGate(bool open)
        {
            InputHandler input = FindAnyObjectByType<InputHandler>();
            if (input == null) return;
            input.SetPlaying(open && (UIManager.Instance == null || !UIManager.Instance.IsPaused));
        }

        // Where the follow camera would centre anyway — the glide hands it back from exactly there
        private static Vector2 PlayerAnchor()
        {
            PlayerHandler player = PlayerBus.Player;
            return player != null ? (Vector2)player.transform.position : Vector2.zero;
        }

        private IEnumerator GlideTo(Vector2 target)
        {
            if (cam == null) yield break;

            Transform camTransform = cam.transform;
            Vector3 start = camTransform.position;
            for (float t = 0f; ; t += Time.deltaTime / glideSeconds)
            {
                if (t >= 1f)
                {
                    camTransform.position = new Vector3(target.x, target.y, start.z);
                    yield break;
                }
                camTransform.position = Vector3.Lerp(start, target, Mathf.SmoothStep(0f, 1f, t));
                yield return null;
            }
        }
    }
}
