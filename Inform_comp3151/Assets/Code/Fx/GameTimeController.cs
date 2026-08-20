using Inkform.Bus;
using UnityEngine;

namespace Inkform.Fx
{
    /// <summary>Single authority for gameplay time. Pause and hitstop are independent freeze reasons.</summary>
    [DefaultExecutionOrder(-9000)]
    public sealed class GameTimeController : MonoBehaviour
    {
        public static GameTimeController Instance { get; private set; }

        [SerializeField, Min(0f)] private float maxHitStop = 0.25f;

        private bool userPaused;
        private float hitStopRemaining;

        public bool IsUserPaused => userPaused;
        public bool IsHitStopped => hitStopRemaining > 0f;
        public bool IsFrozen => userPaused || IsHitStopped;

        /// <summary>Unscaled effect time that freezes for a real pause, but advances through hitstop.</summary>
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
            AudioListener.pause = false;
            Time.timeScale = 1f;
            Instance = null;
        }

        void Update()
        {
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
