using System.Collections.Generic;
using Inkform.Bus;
using UnityEngine;

namespace Inkform.Fx
{
    /// <summary>Single authority for gameplay time. User pause, scoped world freezes and hitstop
    /// are independent reasons; only a user pause also pauses the AudioListener.</summary>
    [DefaultExecutionOrder(-9000)]
    public sealed class GameTimeController : MonoBehaviour
    {
        public static GameTimeController Instance { get; private set; }

        [SerializeField, Min(0f)] private float maxHitStop = 0.25f;

        private bool userPaused;
        private float hitStopRemaining;
        private readonly HashSet<Object> worldFreezeOwners = new HashSet<Object>();

        public bool IsUserPaused => userPaused;
        public bool IsWorldFrozen => worldFreezeOwners.Count > 0;
        public bool IsHitStopped => hitStopRemaining > 0f;
        public bool IsFrozen => userPaused || IsWorldFrozen || IsHitStopped;

        /// <summary>Unscaled effect time that freezes for a real pause, but advances through hitstop
        /// and scoped world freezes so presentation transitions can still complete.</summary>
        public static float PresentationDeltaTime =>
            Instance != null && Instance.userPaused ? 0f : Time.unscaledDeltaTime;

        void Awake()
        {
            if (Instance != null && Instance != this) { enabled = false; return; }
            Instance = this;
            Apply();
        }

        void OnEnable() => FxBus.HitStopRequested += RequestHitStop;

        void OnDisable()
        {
            FxBus.HitStopRequested -= RequestHitStop;
            if (Instance != this) return;

            userPaused = false;
            hitStopRemaining = 0f;
            worldFreezeOwners.Clear();
            AudioListener.pause = false;
            Time.timeScale = 1f;
            Instance = null;
        }

        void Update()
        {
            // A scene unload may destroy an owner before its OnDestroy cleanup reaches us. Unity's
            // fake-null objects must not leave gameplay frozen forever.
            worldFreezeOwners.RemoveWhere(owner => owner == null);

            if (!userPaused && hitStopRemaining > 0f)
                hitStopRemaining = Mathf.Max(0f, hitStopRemaining - Time.unscaledDeltaTime);
            Apply();
        }

        public void SetUserPaused(bool value)
        {
            if (userPaused == value) return;
            userPaused = value;
            AudioListener.pause = value;
            Apply();
        }

        /// <summary>
        /// Adds or removes a scoped world freeze without pausing the AudioListener. This is for
        /// presentation modes such as a wall-map inspection: physics/gameplay time stops while
        /// music and unscaled camera motion continue. Multiple owners stack independently.
        /// </summary>
        public void SetWorldFrozen(Object owner, bool frozen)
        {
            // ReferenceEquals lets an owner's OnDestroy remove its entry even after Unity has begun
            // reporting that object as fake-null.
            if (ReferenceEquals(owner, null)) return;

            bool changed = frozen
                ? worldFreezeOwners.Add(owner)
                : worldFreezeOwners.Remove(owner);
            if (changed) Apply();
        }

        public void RequestHitStop(float seconds)
        {
            if (seconds <= 0f || maxHitStop <= 0f) return;
            hitStopRemaining = Mathf.Min(maxHitStop, Mathf.Max(seconds, hitStopRemaining));
            Apply();
        }

        public void ClearHitStop()
        {
            hitStopRemaining = 0f;
            Apply();
        }

        private void Apply() => Time.timeScale = IsFrozen ? 0f : 1f;
    }
}
