using System.Collections.Generic;
using Inkform.Bus;
using Inkform.Player;
using UnityEngine;

namespace Inkform.Interactable
{
    /// <summary>Base for an E/Y interaction composed onto an Interactable node.</summary>
    public abstract class PlayerInteractablePart : MonoBehaviour, IPlayerInteractablePart
    {
        private static readonly List<PlayerInteractablePart> active = new List<PlayerInteractablePart>();

        [Header("Player interaction")]
        [SerializeField] private string actionText = "Interact";
        [SerializeField] private Transform interactionPoint;
        [SerializeField, Min(0.1f)] private float interactionRange = 1.6f;

        protected Interactable Root { get; private set; }
        public static IReadOnlyList<PlayerInteractablePart> Active => active;
        public Transform InteractionPoint => interactionPoint != null ? interactionPoint : transform;
        public float InteractionRange => interactionRange;
        public virtual bool RepeatWhileHeld => false;

        public virtual void Attach(Interactable root) => Root = root;
        public virtual bool HandleContact(ContactPhase phase, Collider2D other) => false;

        protected virtual void OnEnable()
        {
            if (!active.Contains(this)) active.Add(this);
        }

        protected virtual void OnDisable() => active.Remove(this);

        public InteractionPrompt GetPrompt(PlayerHandler player)
        {
            bool available = CanInteract(player, out string reason);
            return new InteractionPrompt(GetActionText(player), available, reason);
        }

        public bool TryInteract(PlayerHandler player)
        {
            if (!CanInteract(player, out _)) return false;
            return PerformInteraction(player);
        }

        protected virtual string GetActionText(PlayerHandler player) => actionText;
        protected virtual bool CanInteract(PlayerHandler player, out string unavailableReason)
        {
            unavailableReason = string.Empty;
            return player != null && Root != null;
        }

        protected abstract bool PerformInteraction(PlayerHandler player);

        protected virtual void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.65f);
            Vector3 point = interactionPoint != null ? interactionPoint.position : transform.position;
            Gizmos.DrawWireSphere(point, interactionRange);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => active.Clear();
    }
}
