using System;
using Inkform.Level;
using UnityEngine;

namespace Inkform.Bus
{
    /// <summary>
    /// Level bus: scene/level flow signals. The only publisher of Started is SceneDirector (on every
    /// level load); the only publishers of Completed are LevelExit triggers placed in scenes. Neither
    /// the exit trigger nor the director need to know the other — the trigger says "this level was
    /// completed at exit X", the director decides where that leads by consulting the LevelFlow topology.
    ///
    /// Started keeps a snapshot (Current) like PlayerBus — subscribers can sync on first enable without
    /// needing a SceneDirector reference; Completed is a pure command-style signal (FxBus style): it
    /// fires once per completion, there is no "still completing" state to read back.
    /// </summary>
    public static class LevelBus
    {
        /// <summary>A level finished loading (the scene is the active scene). Carries the LevelScene
        /// asset so subscribers (HUD, audio) never need to resolve a scene name back to data.</summary>
        public static event Action<LevelScene> Started;

        /// <summary>The level was completed at the exit whose id matches LevelExit.exitId. What happens
        /// next (which level loads) is the SceneDirector's decision via the topology, not the trigger's.</summary>
        public static event Action<string> Completed;

        /// <summary>Current snapshot: the LevelScene asset for the active gameplay scene, null while in
        /// the menu. Subscribers may read it anytime instead of tracking their own copy.</summary>
        public static LevelScene Current { get; private set; }

        public static void RaiseStarted(LevelScene level)
        {
            Current = level;
            Started?.Invoke(level);
        }

        public static void RaiseCompleted(string exitId)
        {
            Completed?.Invoke(exitId);
        }

        // Static fields do not clear on scene reload; with Domain Reload off, dead subscribers from
        // the previous run linger (same reason as the other buses' ResetStatics)
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Started = null;
            Completed = null;
            Current = null;
        }
    }
}
