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

        // The current scene's live player, registered by PlayerHandler on Awake and cleared on
        // destroy. Persistent managers must read the player from here instead of a serialized
        // reference: the GameManager survives scene switches (AudioManager's DontDestroyOnLoad), so a
        // reference to the spawning scene's player goes stale the moment the scene changes.
        public static PlayerHandler Player { get; private set; }

        public static void RegisterPlayer(PlayerHandler player) => Player = player;

        // The new scene's player registers (Awake) before the old one is destroyed, so the guard
        // keeps the live player in place during a scene switch
        public static void UnregisterPlayer(PlayerHandler player)
        {
            if (Player == player) Player = null;
        }

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
            Player = null;
        }
    }
}
