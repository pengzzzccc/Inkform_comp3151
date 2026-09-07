using Inkform.Bus;
using Inkform.Level;
using TMPro;
using UnityEngine;

namespace Inkform.UI
{
    /// <summary>Per-level gameplay timer. It advances only while the player can actively play.</summary>
    public class GameTimer : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI timerText;
        [SerializeField] private float elapsedTime;
        [SerializeField] private bool isRunning = true;

        public float ElapsedTime => elapsedTime;

        private void Awake() => UpdateTimerDisplay();

        private void Update()
        {
            if (!isRunning || GameStateStore.Current != GameStateStore.GameState.Playing) return;
            if (SceneDirector.Instance != null && SceneDirector.Instance.IsTransitioning) return;

            Advance(Time.deltaTime);
        }

        internal void Advance(float deltaTime)
        {
            if (deltaTime <= 0f) return;
            elapsedTime += deltaTime;
            UpdateTimerDisplay();
        }

        private void UpdateTimerDisplay()
        {
            if (timerText != null) timerText.text = FormatElapsedTime(elapsedTime);
        }

        public static string FormatElapsedTime(float secondsValue)
        {
            int totalSeconds = Mathf.Max(0, Mathf.FloorToInt(secondsValue));
            int hours = totalSeconds / 3600;
            int minutes = (totalSeconds % 3600) / 60;
            int seconds = totalSeconds % 60;
            return hours > 0
                ? $"{hours:00}:{minutes:00}:{seconds:00}"
                : $"{minutes:00}:{seconds:00}";
        }

        public void StartTimer() => isRunning = true;

        public void StopTimer() => isRunning = false;

        public void ResetTimer()
        {
            elapsedTime = 0f;
            UpdateTimerDisplay();
        }
    }
}
