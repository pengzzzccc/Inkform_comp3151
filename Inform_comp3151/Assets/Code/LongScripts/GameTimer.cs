using UnityEngine;
using TMPro;

public class GameTimer : MonoBehaviour
{
    // Reference to the TextMeshPro UI component used to display the time string on screen
    [SerializeField] private TextMeshProUGUI timerText;

    // Tracks the total elapsed time in seconds (can be adjusted or viewed in the Inspector)
    [SerializeField] private float elapsedTime;

    // Flag to control whether the timer is actively counting or paused
    [SerializeField] private bool isRunning = true;

    private void Update()
    {
        // Exit early if the timer is paused/stopped
        if (!isRunning) return;

        // Add the time passed during the current frame to the total elapsed time
        elapsedTime += Time.deltaTime;

        // Update the UI text to reflect the new elapsed time
        UpdateTimerDisplay();
    }

    private void UpdateTimerDisplay()
    {
        // Convert the floating-point seconds into whole integer seconds
        int totalSeconds = Mathf.FloorToInt(elapsedTime);

        // Break down total seconds into hours, minutes, and remaining seconds
        int hours = totalSeconds / 3600;
        int minutes = (totalSeconds % 3600) / 60;
        int seconds = totalSeconds % 60;

        // Switch formatting based on whether 1 hour has been crossed
        if (hours > 0)
        {
            // Format as 00:00:00 (Hours:Minutes:Seconds)
            timerText.text = $"{hours:00}:{minutes:00}:{seconds:00}";
        }
        else
        {
            // Format as 00:00 (Minutes:Seconds)
            timerText.text = $"{minutes:00}:{seconds:00}";
        }
    }

    // Resumes or starts the timer counting process
    public void StartTimer() => isRunning = true;

    // Pauses the timer counting process
    public void StopTimer() => isRunning = false;

    // Resets the elapsed time back to zero and immediately updates the UI display
    public void ResetTimer()
    {
        elapsedTime = 0f;
        UpdateTimerDisplay();
    }
}