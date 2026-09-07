using UnityEngine;

namespace Inkform.UI
{
    /// <summary>Persistent prefab-backed container for every gameplay HUD readout.</summary>
    public sealed class HudRoot : MonoBehaviour
    {
        [SerializeField] private GameObject gameplayRoot;
        [SerializeField] private GameTimer levelTimer;

        public void SetGameplayVisible(bool visible)
        {
            if (gameplayRoot != null) gameplayRoot.SetActive(visible);
            if (levelTimer == null) return;

            if (visible) levelTimer.StartTimer();
            else levelTimer.StopTimer();
        }

        public void ResetLevelTimer()
        {
            if (levelTimer != null) levelTimer.ResetTimer();
        }
    }
}
