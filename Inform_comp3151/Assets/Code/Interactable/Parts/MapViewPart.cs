using System.Collections;
using System.Collections.Generic;
using Inkform.Bus;
using Inkform.Fx;
using Inkform.Input;
using Inkform.Tool;
using Inkform.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Inkform.Interactable.Parts
{
    /// <summary>
    /// World-space wall-map inspection. Confirm in range freezes gameplay (without pausing audio),
    /// suppresses the prompt and asks CamHandler to glide/zoom onto the authored map anchor. A
    /// second confirm returns to the live player before time and controls are released.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MapViewPart : MonoBehaviour, IInteractablePart
    {
        [Header("Map view")]
        [Tooltip("The always-visible world-space SpriteRenderer containing the wall-map artwork.")]
        [SerializeField] private SpriteRenderer mapContent;
        [Tooltip("The world-space centre the camera frames while inspecting the map. z is ignored.")]
        [SerializeField] private Transform viewAnchor;
        [Tooltip("Orthographic size used while the wall map is focused. The gameplay camera defaults to 5.")]
        [SerializeField, Min(0.01f)] private float focusOrthoSize = 2.5f;
        [Tooltip("Seconds for both the focus and return transitions.")]
        [SerializeField, Min(0f)] private float glideSeconds = 0.6f;

        private static MapViewPart activeView;

        private Interactable root;
        private InteractionPromptPart prompt;
        private readonly HashSet<Collider2D> players = new HashSet<Collider2D>();

        private CamHandler cam;
        private InputHandler input;
        private GameTimeController gameTime;
        private InputAction exitAction;
        private bool exitHooked;
        private bool viewing;
        private bool gliding;
        private bool worldFrozen;
        private bool inputLocked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => activeView = null;

        public void Attach(Interactable interactable)
        {
            root = interactable;
            prompt = root.GetComponentInChildren<InteractionPromptPart>(true);

            Collider2D rootCollider = root.GetComponent<Collider2D>();
            if (rootCollider != null && !rootCollider.isTrigger)
                Debug.LogWarning($"{root.name} map view collider should be a Trigger", root);
        }

        public bool HandleContact(ContactPhase phase, Collider2D other)
        {
            if (other == null || !other.CompareTag(Tags.Player)) return false;

            if (phase == ContactPhase.Enter) players.Add(other);
            else if (phase == ContactPhase.Exit) players.Remove(other);
            return false;
        }

        void OnEnable() => PlayerBus.InteractPressed += OnInteractPressed;

        void OnDisable()
        {
            PlayerBus.InteractPressed -= OnInteractPressed;
            StopAllCoroutines();
            AbortView();
        }

        void OnDestroy()
        {
            StopAllCoroutines();
            AbortView();
        }

        private void OnInteractPressed()
        {
            if (viewing || gliding || root == null || LifeBus.IsDead) return;
            if (activeView != null && activeView != this) return;
            if (GameStateStore.Current != GameStateStore.GameState.Playing) return;

            players.RemoveWhere(collider => collider == null);
            if (players.Count == 0 || PlayerBus.Player == null) return;
            if (!ResolveDependencies()) return;

            activeView = this;
            StartCoroutine(EnterView());
        }

        private bool ResolveDependencies()
        {
            if (viewAnchor == null)
            {
                Debug.LogError($"{name} MapViewPart has no View Anchor; inspection was not started", this);
                return false;
            }
            if (mapContent == null || mapContent.sprite == null || !mapContent.enabled
                || !mapContent.gameObject.activeInHierarchy)
            {
                Debug.LogError($"{name} MapViewPart has no visible map Sprite; inspection was not started", this);
                return false;
            }

            cam = FindAnyObjectByType<CamHandler>();
            if (cam == null)
            {
                Debug.LogError($"{name} MapViewPart found no CamHandler; inspection was not started", this);
                return false;
            }
            if (cam.IsFollowHeld)
            {
                Debug.LogWarning($"{name} MapViewPart cannot take a camera already owned by another presentation", this);
                return false;
            }

            input = FindAnyObjectByType<InputHandler>();
            if (input == null)
            {
                Debug.LogError($"{name} MapViewPart found no InputHandler; inspection was not started", this);
                return false;
            }

            gameTime = GameTimeController.Instance;
            if (gameTime == null)
            {
                Debug.LogError($"{name} MapViewPart found no GameTimeController; inspection was not started", this);
                return false;
            }

            return true;
        }

        private IEnumerator EnterView()
        {
            viewing = true;
            gliding = true;
            prompt?.SetSuppressed(true);

            // Clear the last movement sample before InputHandler stops forwarding values. The scoped
            // freeze then stops physics/AI while leaving AudioListener and music untouched.
            PlayerBus.Player?.Move(Vector2.zero);
            input.SetGameplayInputLocked(this, true);
            inputLocked = true;
            gameTime.SetWorldFrozen(this, true);
            worldFrozen = true;

            PrepareExitAction();
            yield return cam.FocusAt(viewAnchor, focusOrthoSize, glideSeconds);

            if (!viewing) yield break;
            gliding = false;

            // Enabling an InputAction while the initiating key is still held can immediately perform
            // it again. Do not attach the exit callback until that original press has been released.
            while (viewing && exitAction != null && exitAction.IsPressed())
                yield return null;

            if (!viewing || exitAction == null) yield break;
            exitAction.performed += OnExitPressed;
            exitHooked = true;
        }

        private void PrepareExitAction()
        {
            ReleaseExitAction();
            exitAction = InputActions.Wrapper.Player.Interact;
            exitAction.Enable();
        }

        private void OnExitPressed(InputAction.CallbackContext ctx)
        {
            if (UIManager.Instance != null && UIManager.Instance.IsPaused) return;
            Exit();
        }

        private void Exit()
        {
            if (!viewing || gliding) return;
            ReleaseExitAction();
            StartCoroutine(ExitView());
        }

        private IEnumerator ExitView()
        {
            gliding = true;
            if (cam != null) yield return cam.ReturnToFollow(glideSeconds);
            CompleteView();
        }

        private void CompleteView()
        {
            ReleaseExitAction();
            ReleaseWorldFreeze();
            prompt?.SetSuppressed(false);

            viewing = false;
            gliding = false;
            if (activeView == this) activeView = null;
            ReleaseInputLock();
        }

        private void AbortView()
        {
            bool owned = viewing || worldFrozen || inputLocked || activeView == this;
            ReleaseExitAction();
            if (!owned) return;

            if (cam != null && cam.IsFollowHeld) cam.ResumeFollow(false);
            ReleaseWorldFreeze();
            prompt?.SetSuppressed(false);

            viewing = false;
            gliding = false;
            if (activeView == this) activeView = null;
            ReleaseInputLock();
        }

        private void ReleaseExitAction()
        {
            if (exitAction == null) return;
            if (exitHooked) exitAction.performed -= OnExitPressed;
            exitHooked = false;
            exitAction.Disable();
            exitAction = null;
        }

        private void ReleaseWorldFreeze()
        {
            if (!worldFrozen) return;
            if (gameTime != null) gameTime.SetWorldFrozen(this, false);
            worldFrozen = false;
        }

        private void ReleaseInputLock()
        {
            if (!inputLocked) return;
            if (input != null) input.SetGameplayInputLocked(this, false);
            inputLocked = false;
        }
    }
}
