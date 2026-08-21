using System.Collections.Generic;
using Inkform.Bus;
using Inkform.Fx;
using Inkform.Interactable;
using UnityEngine;

namespace Inkform.Player
{
    [RequireComponent(typeof(PlayerHandler))]
    public sealed class PlayerInteractor : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float holdDelay = 0.35f;
        [SerializeField, Min(0.02f)] private float repeatInterval = 0.12f;

        private PlayerHandler player;
        private PlayerInteractablePart current;
        private PlayerInteractablePart held;
        private float heldFor;
        private float repeatRemaining;

        private void Awake() => player = GetComponent<PlayerHandler>();

        private void OnDisable()
        {
            held = null;
            current = null;
            InteractionBus.Clear();
        }

        private void Update()
        {
            SelectClosest();
            UpdateRepeat();
        }

        public void InteractStarted()
        {
            if (GameTimeController.Instance != null && GameTimeController.Instance.IsFrozen) return;
            if (current == null) return;
            current.TryInteract(player);
            if (!current.RepeatWhileHeld) return;
            held = current;
            heldFor = 0f;
            repeatRemaining = 0f;
        }

        public void InteractReleased()
        {
            held = null;
            heldFor = 0f;
            repeatRemaining = 0f;
        }

        private void SelectClosest()
        {
            PlayerInteractablePart best = null;
            float bestDistance = float.MaxValue;
            IReadOnlyList<PlayerInteractablePart> targets = PlayerInteractablePart.Active;

            for (int i = 0; i < targets.Count; i++)
            {
                PlayerInteractablePart target = targets[i];
                if (target == null || !target.isActiveAndEnabled) continue;
                InteractionPrompt prompt = target.GetPrompt(player);
                if (!prompt.IsVisible) continue;
                float distance = Vector2.Distance(transform.position, target.InteractionPoint.position);
                if (distance > target.InteractionRange || distance >= bestDistance) continue;
                best = target;
                bestDistance = distance;
            }

            if (current != best && held != null) InteractReleased();
            current = best;
            InteractionBus.Set(current != null ? current.GetPrompt(player) : default);
        }

        private void UpdateRepeat()
        {
            if (held == null || held != current) return;
            heldFor += Time.deltaTime;
            if (heldFor < holdDelay) return;

            repeatRemaining -= Time.deltaTime;
            while (repeatRemaining <= 0f && held != null && held == current)
            {
                held.TryInteract(player);
                repeatRemaining += repeatInterval;
            }
        }
    }
}
