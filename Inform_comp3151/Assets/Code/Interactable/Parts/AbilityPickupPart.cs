using System.Collections.Generic;
using Inkform.Ability;
using Inkform.Audio;
using Inkform.Bus;
using Inkform.Tool;
using UnityEngine;

namespace Inkform.Interactable.Parts
{
    /// <summary>
    /// Ability pickup (reusable): standing inside this Interactable's trigger and pressing confirm
    /// (E / gamepad north, delivered as PlayerBus.InteractPressed) grants a save-level ability —
    /// AbilityStore persists it to the slot immediately, so it holds for the rest of the save, and an
    /// already-earned instance removes itself when its scene loads again, exactly like the capacity
    /// pickups. Unlike those it needs an explicit key press: the card is a deliberate grab, not a
    /// walk-over. Pair it with InteractionPromptPart for the glow + key icon.
    ///
    /// Both parts track player contact independently off the same trigger — neither knows about the
    /// other, so each stays droppable alone on any Interactable.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AbilityPickupPart : MonoBehaviour, IInteractablePart
    {
        [Header("Ability Pickup")]
        [Tooltip("AbilityIds value granted on pickup, e.g. 'checkpoint'.")]
        [SerializeField] private string abilityId = AbilityIds.Checkpoint;
        [Tooltip("Played when the ability is granted.")]
        [SerializeField] private SoundCue collectCue;

        private Interactable root;
        private readonly List<Collider2D> colliders = new List<Collider2D>();
        private readonly List<Renderer> renderers = new List<Renderer>();
        private readonly HashSet<Collider2D> players = new HashSet<Collider2D>();
        private bool consumed;

        public string AbilityId => abilityId;

        public void Attach(Interactable interactable)
        {
            root = interactable;
            colliders.Clear();
            colliders.AddRange(root.GetComponentsInChildren<Collider2D>(true));
            renderers.Clear();
            renderers.AddRange(root.GetComponentsInChildren<Renderer>(true));

            Collider2D rootCollider = root.GetComponent<Collider2D>();
            if (rootCollider != null && !rootCollider.isTrigger)
                Debug.LogWarning($"{root.name} ability pickup collider should be a Trigger", root);
            if (string.IsNullOrWhiteSpace(abilityId))
                Debug.LogWarning($"{root.name} ability pickup has no ability id", this);

            // Save-level: once earned, never again — the world copy is gone for this whole slot
            if (AbilityStore.Owns(abilityId)) ConsumeWorldObject();
        }

        public bool HandleContact(ContactPhase phase, Collider2D other)
        {
            if (other == null || !other.CompareTag(Tags.Player)) return false;

            if (phase == ContactPhase.Enter) players.Add(other);
            else if (phase == ContactPhase.Exit) players.Remove(other);
            return false;   // presence only — never claims the contact
        }

        void OnEnable() => PlayerBus.InteractPressed += OnInteractPressed;
        void OnDisable() => PlayerBus.InteractPressed -= OnInteractPressed;

        private void OnInteractPressed()
        {
            if (consumed || root == null || LifeBus.IsDead) return;

            // Destroyed colliders must not keep an empty range flagging true (player torn down mid-contact)
            if (players.Count > 0) players.RemoveWhere(collider => collider == null);
            if (players.Count == 0) return;

            if (!AbilityStore.Unlock(abilityId)) return;

            PlayCue(collectCue);
            ItemBus.RaiseAbilityUnlocked(root.transform.position, abilityId);
            ConsumeWorldObject();
        }

        // Same guard as Checkpoint: a missing cue or missing manager degrades to silence, not errors
        private static void PlayCue(SoundCue cue)
        {
            if (cue == null || AudioManager.Instance == null) return;
            AudioManager.Instance.Play(cue);
        }

        private void ConsumeWorldObject()
        {
            if (consumed) return;
            consumed = true;
            foreach (Collider2D itemCollider in colliders)
            {
                if (itemCollider != null) itemCollider.enabled = false;
            }
            foreach (Renderer itemRenderer in renderers)
            {
                if (itemRenderer != null) itemRenderer.enabled = false;
            }

            if (root == null) return;
#if UNITY_EDITOR
            if (!Application.isPlaying)
                DestroyImmediate(root.gameObject);
            else
#endif
                Destroy(root.gameObject);
        }
    }
}
