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
    /// The gameplay HUD, rebuilt on the Toolkit tree from the old prefab's components: the run
    /// stats (top left: the run timer beside its clock, the death count beside its skull),
    /// FpsDisplay (top right), InventoryHud (bottom right), SaveIndicator (the "Saved" toast above
    /// it) and the HudRoot visibility switch — plus the tutorial hint line (bottom centre,
    /// TutorialHintView). Everything is picking-mode Ignore in the UXML — the HUD never eats a
    /// click meant for the world.
    ///
    /// A plain object driven from UIManager.Update. The run has one clock, SaveStore.PlaySeconds
    /// — the same number the save slot stores and the end sheet reports — and the HUD is what
    /// advances it: every frame the player is in play (in a level, state Playing, no scene
    /// transition), on unscaled time so hitstop counts and pause does not. The timer reads
    /// minutes:seconds:milliseconds; Show Timer only hides it. The toast and FPS counter run on
    /// unscaled time too (hitstop must not freeze them).
    /// </summary>
    public sealed class Hud
    {
        private const float ToastHoldSeconds = 1f;
        private const float ToastFadeSeconds = 0.4f;
        private const float FpsUpdateInterval = 0.5f;   // refresh the label text at most this often
        private const float FpsSmoothing = 0.1f;        // exponential smoothing on the instant fps

        private readonly VisualElement gameplay;
        private readonly VisualElement timerRow;
        private readonly VisualElement clockIcon;
        private readonly Label timerLabel;
        private readonly VisualElement deathIcon;
        private readonly Label deathLabel;
        private readonly Label fpsLabel;
        private readonly VisualElement inventoryRoot;
        private readonly VisualElement inventoryIcon;
        private readonly Label inventoryCount;
        private readonly Label saveToast;
        private readonly TutorialHintView tutorialHint;

        private bool timerRunning;
        private long shownWholeSecond = -1;   // the clock rocks as each new second comes up
        private int shownDeaths = -1;         // the skull pops as the count goes up

        private float toastRemaining;

        private float smoothedFps;
        private float fpsElapsed;

        public Hud(VisualElement root)
        {
            gameplay = root.Q("Gameplay");
            timerRow = root.Q("TimerRow");
            clockIcon = root.Q("ClockIcon");
            timerLabel = root.Q<Label>("TimerLabel");
            deathIcon = root.Q("DeathIcon");
            deathLabel = root.Q<Label>("DeathLabel");
            fpsLabel = root.Q<Label>("FpsLabel");
            inventoryRoot = root.Q("Inventory");
            inventoryIcon = root.Q("InventoryIcon");
            inventoryCount = root.Q<Label>("InventoryCount");
            saveToast = root.Q<Label>("SaveToast");
            tutorialHint = new TutorialHintView(root);

            RefreshRunStats();

            InventoryStore.Changed += RefreshInventory;
            SettingsStore.Changed += ApplyVisibility;
            SaveStore.Saved += ShowToast;

            RefreshInventory();
            ApplyVisibility();
        }

        // ---- Visibility (old HudRoot) ----

        public void SetGameplayVisible(bool visible)
        {
            if (gameplay != null) gameplay.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

            timerRunning = visible;
        }

        // ---- Per-frame (old GameTimer / FpsDisplay / SaveIndicator Update loops) ----

        public void Tick(float unscaledDelta)
        {
            // The run's clock advances only while the player can actively play (old GameTimer's
            // rule), on unscaled time: hitstop is play, the pause menu is not
            if (timerRunning
                && GameStateStore.Current == GameStateStore.GameState.Playing
                && !(SceneDirector.Instance != null && SceneDirector.Instance.IsTransitioning))
            {
                SaveStore.AdvancePlayTime(unscaledDelta);
            }
            RefreshRunStats();

            // "Saved" toast: hold, then fade — unscaled, so Save & Quit's final save flashes the
            // same as any other even while the pause menu holds timeScale at 0 (old SaveIndicator).
            if (toastRemaining > 0f && saveToast != null)
            {
                toastRemaining = Mathf.Max(0f, toastRemaining - unscaledDelta);
                saveToast.style.opacity = new StyleFloat(Mathf.Clamp01(toastRemaining / ToastFadeSeconds));
            }

            // Tutorial hint fades on unscaled time too: hitstop must not stall it mid-fade
            tutorialHint.Tick(unscaledDelta);

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

        // Timer text every frame (the milliseconds move), the clock's rock once a second, the
        // death count only when it changes
        private void RefreshRunStats()
        {
            float seconds = SaveStore.PlaySeconds;
            if (timerLabel != null) timerLabel.text = RunTimeFormat.Format(seconds);

            long whole = (long)seconds;
            if (whole != shownWholeSecond)
            {
                if (shownWholeSecond >= 0 && clockIcon != null && SettingsStore.ShowTimer) UiFx.Wobble(clockIcon);
                shownWholeSecond = whole;
            }

            int deaths = SaveStore.RunDeaths;
            if (deaths == shownDeaths) return;
            if (shownDeaths >= 0 && deaths > shownDeaths && deathIcon != null) UiFx.Bounce(deathIcon);
            shownDeaths = deaths;
            if (deathLabel != null) deathLabel.text = deaths.ToString();
        }

        // ---- Subscriptions ----

        private void ApplyVisibility()
        {
            if (fpsLabel != null)
                fpsLabel.style.display = SettingsStore.ShowFps ? DisplayStyle.Flex : DisplayStyle.None;
            if (timerRow != null)
                timerRow.style.display = SettingsStore.ShowTimer ? DisplayStyle.Flex : DisplayStyle.None;
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
            SettingsStore.Changed -= ApplyVisibility;
            SaveStore.Saved -= ShowToast;
            tutorialHint.Dispose();
        }
    }
}
