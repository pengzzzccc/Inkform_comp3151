using System;
using Inkform.Level;
using UnityEngine;

namespace Inkform.Bus
{
    /// <summary>
    /// Level bus: scene/level flow signals. The only publisher of Started is SceneDirector (on every
    /// level load); the only publishers of ExitReached are LevelExit triggers placed in scenes.
    /// Neither the exit trigger nor the director need to know the other — the trigger carries its
    /// own destination reference, the director decides whether and when to load it.
    ///
    /// Both are pure signals with no snapshot: Started fires once per load, and ExitReached is
    /// command-style (FxBus style) — it fires once per door, there is no "still exiting" state to
    /// read back.
    /// </summary>
    public static class LevelBus
    {
        /// <summary>A level finished loading (the scene is the active scene). Carries the scene name
        /// (null in the menu / for unregistered scenes) — a plain string is the whole identity of a
        /// room in this project, so subscribers never need to resolve it back to data.</summary>
        public static event Action<string> Started;

        /// <summary>A door fired: destination is the room the door points at, spawnId names the
        /// arrival checkpoint in it ("" = the room's start point). A RoomDefinition rather than a
        /// scene-name string, because the door holds a direct reference now — there is no central
        /// text graph to resolve an exit id against anymore.</summary>
        public static event Action<RoomDefinition, string> ExitReached;

        public static void RaiseStarted(string sceneName)
        {
            Started?.Invoke(sceneName);
        }

        public static void RaiseExitReached(RoomDefinition destination, string spawnId)
        {
            ExitReached?.Invoke(destination, spawnId);
        }

        // Static fields do not clear on scene reload; with Domain Reload off, dead subscribers from
        // the previous run linger (same reason as the other buses' ResetStatics)
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Started = null;
            ExitReached = null;
        }
    }
}
