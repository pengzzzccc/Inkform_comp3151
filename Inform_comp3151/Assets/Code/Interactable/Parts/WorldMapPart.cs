using Inkform.Bus;
using Inkform.Input;
using Inkform.Life;
using Inkform.Player;
using UnityEngine;

namespace Inkform.Interactable.Parts
{
    /// <summary>Toggles an in-world map view without pausing simulation or blocking Interact.</summary>
    public sealed class WorldMapPart : PlayerInteractablePart
    {
        [Header("Map view")]
        [SerializeField] private Transform focusPoint;
        [SerializeField, Min(0.1f)] private float orthographicSize = 4f;
        [SerializeField, Min(0f)] private float blendTime = 0.35f;

        private bool viewing;

        protected override string GetActionText(PlayerHandler player) => viewing ? "Close Map" : "View Map";

        protected override bool PerformInteraction(PlayerHandler player)
        {
            if (viewing) ExitView();
            else EnterView();
            return true;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            LifeBus.Died += OnDied;
        }

        private void EnterView()
        {
            viewing = true;
            GameplayControlGate.Acquire(this);
            Transform target = focusPoint != null ? focusPoint : transform;
            FxBus.RequestFocus(this, target.position, orthographicSize, blendTime);
        }

        private void ExitView()
        {
            if (!viewing) return;
            viewing = false;
            FxBus.ReleaseFocus(this);
            GameplayControlGate.Release(this);
        }

        protected override void OnDisable()
        {
            LifeBus.Died -= OnDied;
            ExitView();
            base.OnDisable();
        }

        private void OnDied(DeathContext context) => ExitView();
    }
}
