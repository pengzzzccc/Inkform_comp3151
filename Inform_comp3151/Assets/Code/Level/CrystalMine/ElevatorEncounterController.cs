using Inkform.Bus;
using Inkform.Life;
using UnityEngine;

namespace Inkform.Level.CrystalMine
{
    /// <summary>Restorable elevator movement and one-shot LevelGraph exit publication.</summary>
    public sealed class ElevatorEncounterController : MonoBehaviour, IRestorable
    {
        public enum EncounterState { Idle, Rising, Completed }

        [SerializeField] private Transform elevatorPlatform;
        [SerializeField] private Transform startPoint;
        [SerializeField] private Transform endPoint;
        [SerializeField, Min(0.1f)] private float ascentDuration = 14f;
        [SerializeField] private HazardRainSpawner hazardRain;
        [SerializeField] private string exitId = "elevator";

        private EncounterState state;
        private float elapsed;
        private bool completionPublished;
        private Vector3 initialPosition;
        private Rigidbody2D platformBody;

        public EncounterState State => state;

        private void Awake()
        {
            if (elevatorPlatform == null) elevatorPlatform = transform;
            platformBody = elevatorPlatform.GetComponent<Rigidbody2D>();
            initialPosition = startPoint != null ? startPoint.position : elevatorPlatform.position;
            if (state == EncounterState.Idle) SetPlatformPosition(initialPosition, true);
        }

        private void OnEnable() => LifeBus.Died += OnDied;
        private void OnDisable()
        {
            LifeBus.Died -= OnDied;
            hazardRain?.StopAndClear();
        }

        private void FixedUpdate()
        {
            if (state != EncounterState.Rising || LifeBus.IsDead) return;
            elapsed = Mathf.Min(ascentDuration, elapsed + Time.fixedDeltaTime);
            float t = ascentDuration > 0f ? elapsed / ascentDuration : 1f;
            Vector3 destination = endPoint != null ? endPoint.position : initialPosition;
            SetPlatformPosition(Vector3.Lerp(initialPosition, destination, t), false);
            if (t >= 1f) Complete();
        }

        private void OnDied(DeathContext context) => hazardRain?.StopAndClear();

        public bool BeginEncounter()
        {
            if (state != EncounterState.Idle) return false;
            state = EncounterState.Rising;
            elapsed = 0f;
            completionPublished = false;
            hazardRain?.Begin();
            return true;
        }

        private void Complete()
        {
            if (state == EncounterState.Completed) return;
            state = EncounterState.Completed;
            hazardRain?.StopAndClear();
            if (completionPublished) return;
            completionPublished = true;
            if (string.IsNullOrWhiteSpace(exitId))
                Debug.LogWarning($"{name}: elevator completed without a configured exit ID.", this);
            else
                LevelBus.RaiseCompleted(exitId);
        }

        public IMemento Capture() => new ElevatorMemento(
            this, state, elapsed, completionPublished,
            elevatorPlatform != null ? elevatorPlatform.position : transform.position);

        private void Restore(EncounterState restoredState, float restoredElapsed,
            bool restoredCompletionPublished, Vector3 restoredPosition)
        {
            state = restoredState;
            elapsed = restoredElapsed;
            completionPublished = restoredCompletionPublished;
            SetPlatformPosition(restoredPosition, true);
            hazardRain?.StopAndClear();
            if (state == EncounterState.Rising) hazardRain?.Begin(elapsed);
        }

        private void SetPlatformPosition(Vector3 position, bool teleport)
        {
            if (elevatorPlatform == null) return;
            if (platformBody != null)
            {
                if (teleport) platformBody.position = position;
                else platformBody.MovePosition(position);
                return;
            }
            elevatorPlatform.position = position;
        }

        private sealed class ElevatorMemento : IMemento
        {
            private readonly ElevatorEncounterController owner;
            private readonly EncounterState state;
            private readonly float elapsed;
            private readonly bool published;
            private readonly Vector3 position;

            public ElevatorMemento(ElevatorEncounterController owner, EncounterState state,
                float elapsed, bool published, Vector3 position)
            {
                this.owner = owner;
                this.state = state;
                this.elapsed = elapsed;
                this.published = published;
                this.position = position;
            }

            public void Restore()
            {
                if (owner != null) owner.Restore(state, elapsed, published, position);
            }
        }
    }
}
