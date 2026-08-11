using System;
using UnityEngine;
using Inkform.Player;

namespace Inkform.Bus
{
    /// <summary>
    /// Player state bus: the only publisher is PlayerHandler; subscribers need no PlayerHandler reference.
    /// The bus dedups (only broadcasts when a value actually changes) and keeps the current snapshot
    /// for subscribers' first-time sync.
    /// </summary>
    public static class PlayerBus
    {
        public static event Action<PlayerState> StateChanged;
        public static event Action<FaceDirection> FaceChanged;

        // Current snapshot: subscribers can read it in OnEnable to complete initial sync
        public static PlayerState State { get; private set; }
        public static FaceDirection Face { get; private set; }

        public static void RaiseState(PlayerState state)
        {
            if (state == State) return;      // dedup: broadcast only on change
            State = state;
            StateChanged?.Invoke(state);
        }

        public static void RaiseFace(FaceDirection face)
        {
            if (face == Face) return;
            Face = face;
            FaceChanged?.Invoke(face);
        }

        // Static fields do not clear on scene reload; with Domain Reload off, dead subscribers from
        // the previous run linger
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            StateChanged = null;
            FaceChanged = null;
            // Match PlayerHandler's field defaults (Idle / R) to avoid an extra broadcast at startup
            State = PlayerState.Idle;
            Face = FaceDirection.R;
        }
    }
}
