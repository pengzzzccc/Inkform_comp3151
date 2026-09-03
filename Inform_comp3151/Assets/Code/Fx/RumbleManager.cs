using System.Collections;
using System.Collections.Generic;
using Inkform.Bus;
using Inkform.Player;
using Inkform.Tool;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Inkform.Fx
{
    /// <summary>
    /// Gamepad haptics: translates "what happened in the game" into left/right motor rumble. Pure
    /// event-driven — subscribes to buses, never polls. Same director pattern as FxDirector /
    /// AudioDirector: tuning for "how strong each event rumbles" is centralized in this one Inspector;
    /// RopeGun / PlayerHandler never need to know haptics exist.
    ///
    /// Motor direction semantics (Xbox / most gamepads: big motor left, small motor right):
    ///  - land (directionless): both motors split the strength evenly
    ///  - rope fired / hit (directional): strength split by the horizontal component of the direction
    ///  - blast: shockwave model — the left/right motor triggers in the order the blast front sweeps
    ///    across the player's body (near side first, far side delayed by the travel-time difference),
    ///    with the overall strength attenuated by distance from the blast center
    ///
    /// Implementation: a scheduled queue + active list driven by a single coroutine. All timing uses
    /// unscaled time — a blast triggers hitstop (timeScale = 0), and WaitForSeconds would freeze.
    /// Overlapping rumbles take the per-motor max, so a short rumble never cuts a longer one.
    /// While timeScale <= 0 (pause / hitstop) the motors are zeroed but the remaining duration is
    /// kept, so a paused game never buzzes.
    /// Attach to GameManager (the DontDestroyOnLoad host), survives scene switches.
    /// </summary>
    public class RumbleManager : MonoBehaviour
    {
        [Header("Master")]
        [SerializeField] private bool enableRumble = true;

        /// <summary>Runtime toggle: turning off stops any ongoing rumble immediately.</summary>
        public bool EnableRumble
        {
            get => enableRumble;
            set
            {
                enableRumble = value;
                if (!value) StopRumble();
            }
        }

        [Header("Land")]
        [SerializeField] private float landStrength = 0.35f;
        [SerializeField] private float landDuration = 0.12f;

        [Header("Blast (shockwave)")]
        [SerializeField] private float blastStrength = 1f;
        [SerializeField] private float blastDuration = 0.22f;
        [SerializeField] private float blastFalloff = 20f;                 // blasts farther than this are completely unfelt
        [Tooltip("Shockwave speed: drives the left/right motor delay. Lower = stronger sweep feel")]
        [SerializeField] private float shockSpeed = 30f;
        [Tooltip("Player body half-width, used to compute the shockwave's arrival-time difference across the body")]
        [SerializeField] private float playerHalfWidth = 0.5f;

        [Header("Rope fired")]
        [SerializeField] private float fireStrength = 0.2f;
        [SerializeField] private float fireDuration = 0.08f;

        [Header("Rope hit")]
        [SerializeField] private float hitStrength = 0.55f;
        [SerializeField] private float hitDuration = 0.15f;

        private struct ScheduledRumble
        {
            public float remainingDelay;
            public float low;
            public float high;
            public float duration;
        }

        private struct ActiveRumble
        {
            public float remainingDuration;
            public float low;
            public float high;
        }

        private readonly List<ScheduledRumble> scheduled = new List<ScheduledRumble>();
        private readonly List<ActiveRumble> active = new List<ActiveRumble>();
        private Coroutine driveLoop;

        void OnEnable()
        {
            PlayerBus.StateChanged += OnPlayerState;
            HazardBus.Blast += OnBlast;
            RopeGunBus.Fired += OnRopeFired;
            RopeGunBus.Hit += OnRopeHit;
        }

        void OnDisable()
        {
            PlayerBus.StateChanged -= OnPlayerState;
            HazardBus.Blast -= OnBlast;
            RopeGunBus.Fired -= OnRopeFired;
            RopeGunBus.Hit -= OnRopeHit;
            StopAllCoroutines();
            driveLoop = null;
            StopRumble();
        }

        // Clears every queued/active rumble and zeroes the motors
        private void StopRumble()
        {
            scheduled.Clear();
            active.Clear();
            Gamepad.current?.SetMotorSpeeds(0f, 0f);
        }

        private void OnPlayerState(PlayerState state)
        {
            if (state == PlayerState.Land) AddNow(landStrength, landStrength, landDuration);
        }

        // Shockwave: the blast front reaches the near half of the body first, so that motor fires
        // first; the far half fires after the travel-time difference. Overall strength attenuates by
        // distance from the blast center.
        private void OnBlast(Vector2 center, float radius, float force)
        {
            Transform p = Player;
            if (p == null) return;

            float dist = Vector2.Distance(center, p.position);
            float k = blastFalloff <= 0f
                ? 1f
                : Mathf.Clamp01(1f - dist / blastFalloff);
            if (k <= 0f) return;

            float strength = blastStrength * k;
            float w = Mathf.Max(0f, playerHalfWidth);

            // Distances to the left/right half of the body; their difference is the sweep delay
            float dL = Vector2.Distance(center, p.position + new Vector3(-w, 0f, 0f));
            float dR = Vector2.Distance(center, p.position + new Vector3(w, 0f, 0f));

            float speed = Mathf.Max(0.01f, shockSpeed);
            if (dL <= dR)
            {
                AddAfter(dL / speed, strength, 0f, blastDuration);   // left half first
                AddAfter(dR / speed, 0f, strength, blastDuration);   // right half later
            }
            else
            {
                AddAfter(dR / speed, 0f, strength, blastDuration);   // right half first
                AddAfter(dL / speed, strength, 0f, blastDuration);   // left half later
            }
        }

        private void OnRopeFired(Vector2 dir) => AddNow(dir, fireStrength, fireDuration);

        private void OnRopeHit(Vector2 dir) => AddNow(dir, hitStrength, hitDuration);

        // Immediate rumble with explicit per-motor strengths (directionless events split evenly)
        private void AddNow(float low, float high, float duration)
        {
            if (!enableRumble || Gamepad.current == null || duration <= 0f) return;
            active.Add(new ActiveRumble { remainingDuration = duration, low = low, high = high });
            EnsureLoop();
        }

        // Immediate rumble from a direction: split the strength by the horizontal component
        private void AddNow(Vector2 dir, float strength, float duration)
        {
            if (!enableRumble || Gamepad.current == null || strength <= 0f || duration <= 0f) return;
            float x = dir.sqrMagnitude > 0.0001f ? dir.normalized.x : 0f;
            AddNow(strength * (0.5f - 0.5f * x), strength * (0.5f + 0.5f * x), duration);
        }

        // Delayed rumble: the shockwave's left/right trigger ordering
        private void AddAfter(float delay, float low, float high, float duration)
        {
            if (!enableRumble || Gamepad.current == null || duration <= 0f) return;
            scheduled.Add(new ScheduledRumble
            {
                remainingDelay = Mathf.Max(0f, delay), low = low, high = high, duration = duration
            });
            EnsureLoop();
        }

        private void EnsureLoop()
        {
            if (driveLoop != null) return;
            driveLoop = StartCoroutine(Drive());
        }

        private IEnumerator Drive()
        {
            while (scheduled.Count > 0 || active.Count > 0)
            {
                bool frozen = GameTimeController.Instance != null && GameTimeController.Instance.IsFrozen;
                float dt = frozen ? 0f : Time.unscaledDeltaTime;

                // Countdown fields preserve the exact remaining delay/duration while frozen.
                for (int i = scheduled.Count - 1; i >= 0; i--)
                {
                    ScheduledRumble pending = scheduled[i];
                    pending.remainingDelay -= dt;
                    if (pending.remainingDelay <= 0f)
                    {
                        active.Add(new ActiveRumble
                        {
                            remainingDuration = pending.duration,
                            low = pending.low,
                            high = pending.high,
                        });
                        scheduled.RemoveAt(i);
                    }
                    else scheduled[i] = pending;
                }

                for (int i = active.Count - 1; i >= 0; i--)
                {
                    ActiveRumble rumble = active[i];
                    rumble.remainingDuration -= dt;
                    if (rumble.remainingDuration <= 0f) active.RemoveAt(i);
                    else active[i] = rumble;
                }

                if (scheduled.Count == 0 && active.Count == 0)
                {
                    Gamepad.current?.SetMotorSpeeds(0f, 0f);
                    driveLoop = null;
                    yield break;
                }

                // Pause / hitstop: zero the motors but keep the remaining durations
                if (frozen)
                {
                    Gamepad.current?.SetMotorSpeeds(0f, 0f);
                }
                else
                {
                    float low = 0f, high = 0f;
                    foreach (ActiveRumble r in active)
                    {
                        low = Mathf.Max(low, r.low);
                        high = Mathf.Max(high, r.high);
                    }
                    Gamepad.current?.SetMotorSpeeds(low, high);
                }

                yield return null;
            }
        }

        // Player transform: the GameManager hosting this survives scene switches, so after a change
        // the old reference is a Unity fake-null and is re-looked-up on next use (same pattern as
        // PlayerBus.Player / AudioManager.Listener)
        private static Transform playerCache;

        private static Transform Player
        {
            get
            {
                if (playerCache == null)
                {
                    GameObject go = GameObject.FindGameObjectWithTag(Tags.Player);
                    playerCache = go != null ? go.transform : null;
                }
                return playerCache;
            }
        }
    }
}
