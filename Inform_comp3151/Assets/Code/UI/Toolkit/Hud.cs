using Inkform.Bus;
using Inkform.Item;
using Inkform.Level;
using Inkform.Save;
using Inkform.Settings;
using UnityEngine;
using UnityEngine.UIElements;

namespace Inkform.UI
{
    /// <summary>
    /// The gameplay HUD, rebuilt on the Toolkit tree from the old prefab's five components:
    /// GameTimer (top centre, now in BombSlimeFont), FpsDisplay (top right), InventoryHud
    /// (bottom right), SaveIndicator (the "Saved" toast above it) and the HudRoot visibility
    /// switch. Everything is picking-mode Ignore in the UXML — the HUD never eats a click meant
    /// for the world.
    ///
    /// A plain object driven from UIManager.Update: timer rules are the old GameTimer's (advance
    /// only while Playing and not mid-transition), the toast and FPS counter run on unscaled time
    /// (hitstop must not freeze them), exactly as before.
    /// </summary>
    public sealed class Hud
    {
        private const float ToastHoldSeconds = 1f;
        private const float ToastFadeSeconds = 0.4f;
        private const float FpsUpdateInterval = 0.5f;   // refresh the label text at most this often
        private const float FpsSmoothing = 0.1f;        // exponential smoothing on the instant fps

        private readonly VisualElement gameplay;
        private readonly Label timerLabel;
        private readonly Label fpsLabel;
        private readonly VisualElement inventoryRoot;
        private readonly VisualElement inventoryIcon;
        private readonly Label inventoryCount;
        private readonly Label saveToast;

        private float elapsedTime;
        private bool timerRunning;

        private float toastRemaining;

        private float smoothedFps;
        private float fpsElapsed;

        public Hud(VisualElement root)
        {
            gameplay = root.Q("Gameplay");
            timerLabel = root.Q<Label>("TimerLabel");
            fpsLabel = root.Q<Label>("FpsLabel");
            inventoryRoot = root.Q("Inventory");
            inventoryIcon = root.Q("InventoryIcon");
            inventoryCount = root.Q<Label>("InventoryCount");
            saveToast = root.Q<Label>("SaveToast");

            UpdateTimerDisplay();

            InventoryStore.Changed += RefreshInventory;
            SettingsStore.Changed += ApplyFpsVisibility;
            SaveStore.Saved += ShowToast;

            RefreshInventory();
            ApplyFpsVisibility();
        }

        // ---- Visibility (old HudRoot) ----

        public void SetGameplayVisible(bool visible)
        {
            if (gameplay != null) gameplay.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

            timerRunning = visible;
        }

        public void ResetLevelTimer()
        {
            elapsedTime = 0f;
            UpdateTimerDisplay();
        }

        // ---- Per-frame (old GameTimer / FpsDisplay / SaveIndicator Update loops) ----

        public void Tick(float scaledDelta, float unscaledDelta)
        {
            // Timer advances only while the player can actively play (old GameTimer).
            if (timerRunning
                && GameStateStore.Current == GameStateStore.GameState.Playing
                && !(SceneDirector.Instance != null && SceneDirector.Instance.IsTransitioning)
                && scaledDelta > 0f)
            {
                elapsedTime += scaledDelta;
                UpdateTimerDisplay();
            }

            // "Saved" toast: hold, then fade — unscaled, so Save & Quit's final save flashes the
            // same as any other even while the pause menu holds timeScale at 0 (old SaveIndicator).
            if (toastRemaining > 0f && saveToast != null)
            {
                toastRemaining = Mathf.Max(0f, toastRemaining - unscaledDelta);
                saveToast.style.opacity = new StyleFloat(Mathf.Clamp01(toastRemaining / ToastFadeSeconds));
            }

            // FPS readout: unscaled (hitstop must not zero the reading), smoothed and printed at
            // most twice a second (old FpsDisplay).
            if (fpsLabel != null && SettingsStore.ShowFps && unscaledDelta > 0f)
            {
                smoothedFps += (1f / unscaledDelta - smoothedFps) * FpsSmoothing;
                fpsElapsed += unscaledDelta;
                if (fpsElapsed >= FpsUpdateInterval)
                {
                    fpsElapsed = 0f;
                    fpsLabel.text = $"{Mathf.RoundToInt(smoothedFps)} FPS";
                }
            }
        }

        private void UpdateTimerDisplay()
        {
            if (timerLabel != null) timerLabel.text = FormatElapsedTime(elapsedTime);
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

        // ---- Subscriptions ----

        private void ApplyFpsVisibility()
        {
            if (fpsLabel != null)
                fpsLabel.style.display = SettingsStore.ShowFps ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>Compact readout for the next FIFO item and current count/capacity
        /// (old InventoryHud.Refresh).</summary>
        private void RefreshInventory()
        {
            if (inventoryRoot == null || inventoryIcon == null || inventoryCount == null) return;

            bool hasItem = InventoryStore.TryPeekFirst(out InventoryItemDefinition first);
            Sprite icon = hasItem ? first.Icon : null;
            if (icon != null) inventoryIcon.style.backgroundImage = new StyleBackground(icon);
            inventoryIcon.style.display = icon != null ? DisplayStyle.Flex : DisplayStyle.None;
            inventoryCount.text = $"{InventoryStore.Count}/{InventoryStore.Capacity}";
        }

        private void ShowToast()
        {
            if (saveToast == null) return;
            toastRemaining = ToastHoldSeconds + ToastFadeSeconds;
            saveToast.style.opacity = new StyleFloat(1f);
        }

        /// <summary>Static-event subscriptions must not outlive the UI layer (the spot where the
        /// old MonoBehaviours' OnDisable used to unsubscribe).</summary>
        public void Dispose()
        {
            InventoryStore.Changed -= RefreshInventory;
            SettingsStore.Changed -= ApplyFpsVisibility;
            SaveStore.Saved -= ShowToast;
        }
    }
}
