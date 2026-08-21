using System.Collections;
using Inkform.Bus;
using Inkform.Input;
using Inkform.Interactable.Parts;
using Inkform.Life;
using Inkform.Progression;
using Inkform.Tool;
using UnityEngine;

namespace Inkform.Level.CrystalMine
{
    /// <summary>Small coordinator for the control-room camera reveal and two progression conditions.</summary>
    [RequireComponent(typeof(Collider2D))]
    public sealed class ControlRoomSequenceController : MonoBehaviour
    {
        [SerializeField] private CrystalIntakePart intake;
        [SerializeField] private ElevatorEncounterController elevator;
        [SerializeField] private Transform intakeFocusPoint;
        [SerializeField, Min(0.1f)] private float focusOrthoSize = 4f;
        [SerializeField, Min(0f)] private float focusBlendTime = 0.45f;
        [SerializeField, Min(0f)] private float revealHoldTime = 1.2f;
        [SerializeField] private string depositGuidance = "Deposit crystals into the intake to charge the elevator.";
        [SerializeField] private string controllerGuidance = "Find the Elevator Controller in the Equipment Room, then return here.";

        private bool playerInside;
        private bool introPlaying;
        private bool introShown;
        private Coroutine introRoutine;

        private void Reset()
        {
            Collider2D trigger = GetComponent<Collider2D>();
            if (trigger != null) trigger.isTrigger = true;
        }

        private void OnEnable()
        {
            RunProgressStore.Changed += Evaluate;
            LifeBus.Died += OnDied;
        }

        private void OnDisable()
        {
            RunProgressStore.Changed -= Evaluate;
            LifeBus.Died -= OnDied;
            if (introRoutine != null) StopCoroutine(introRoutine);
            introRoutine = null;
            introPlaying = false;
            introShown = false;
            FxBus.ReleaseFocus(this);
            GameplayControlGate.Release(this);
            GuidanceBus.Clear(this);
        }

        private void OnDied(DeathContext context)
        {
            playerInside = false;
            if (introRoutine != null) StopCoroutine(introRoutine);
            introRoutine = null;
            introPlaying = false;
            introShown = false;
            FxBus.ReleaseFocus(this);
            GameplayControlGate.Release(this);
            GuidanceBus.Clear(this);
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (!other.CompareTag(Tags.Player)) return;
            playerInside = true;
            if (!introShown)
            {
                introShown = true;
                introRoutine = StartCoroutine(RevealIntake());
            }
            else Evaluate();
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            if (!other.CompareTag(Tags.Player)) return;
            playerInside = false;
            GuidanceBus.Clear(this);
        }

        private IEnumerator RevealIntake()
        {
            introPlaying = true;
            GameplayControlGate.Acquire(this);
            Vector2 point = intakeFocusPoint != null ? intakeFocusPoint.position
                : intake != null ? intake.transform.position : transform.position;
            FxBus.RequestFocus(this, point, focusOrthoSize, focusBlendTime);
            GuidanceBus.Show(this, depositGuidance);

            float remaining = focusBlendTime + revealHoldTime;
            while (remaining > 0f)
            {
                remaining -= Time.deltaTime;
                yield return null;
            }

            FxBus.ReleaseFocus(this);
            GameplayControlGate.Release(this);
            introPlaying = false;
            introRoutine = null;
            Evaluate();
        }

        private void Evaluate()
        {
            if (!playerInside || introPlaying || intake == null) return;
            if (!intake.IsCharged)
            {
                GuidanceBus.Show(this, depositGuidance);
                return;
            }
            if (!RunProgressStore.ElevatorControllerAcquired)
            {
                GuidanceBus.Show(this, controllerGuidance);
                return;
            }

            GuidanceBus.Clear(this);
            elevator?.BeginEncounter();
        }
    }
}
