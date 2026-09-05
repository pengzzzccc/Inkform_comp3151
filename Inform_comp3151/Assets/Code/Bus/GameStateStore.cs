using System;
using UnityEngine;

namespace Inkform.Bus
{
    /// <summary>
    /// Game flow state: the one place that answers "what phase is the game in right now" — Boot,
    /// MainMenu, Transition, Playing or Paused. Written only by the two components that already own
    /// those transitions (SceneDirector for scene switches, UIManager for pause); everything else
    /// may read Current or subscribe to Changed.
    ///
    /// Deliberately a read side, not a control side: the older per-system flags (UIManager.paused
    /// and IsInMainMenu, SceneDirector.transitionInProgress, LifeBus.IsDead, SaveStore.ActiveSlot)
    /// still exist and still drive behaviour. This store just gives that scattered picture one
    /// queryable, subscribable source, so new code does not have to patch five fields across three
    /// classes together to ask "can I do this right now?".
    ///
    /// Setting the value it already holds is a no-op, so re-asserting state at a scene landing is
    /// free. Same static lifecycle pattern as the buses.
    /// </summary>
    public static class GameStateStore
    {
        public enum GameState { Boot, MainMenu, Transition, Playing, Paused }

        public static GameState Current { get; private set; } = GameState.Boot;

        public static event Action Changed;

        public static void Set(GameState state)
        {
            if (state == Current) return;
            Current = state;
            Changed?.Invoke();
        }

        // Static fields do not clear on scene reload; with Domain Reload off, dead subscribers from
        // the previous run linger
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Current = GameState.Boot;
            Changed = null;
        }
    }
}
