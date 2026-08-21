using System;
using Inkform.Progression;
using UnityEngine;

namespace Inkform.Bus
{
    /// <summary>One-shot facts produced by Crystal Mine progression objects.</summary>
    public static class ProgressionBus
    {
        public static event Action<ProgressionRewardType> RewardGranted;
        public static event Action<int, Vector2> CrystalCollected;
        public static event Action<Vector2> CrystalFormationDestroyed;
        public static event Action<string, int> DoorActivated;
        public static event Action<int, int> CrystalDeposited;
        public static event Action<int> TutorialAdvanced;

        public static void RaiseRewardGranted(ProgressionRewardType reward) => RewardGranted?.Invoke(reward);
        public static void RaiseCrystalCollected(int amount, Vector2 position) => CrystalCollected?.Invoke(amount, position);
        public static void RaiseCrystalFormationDestroyed(Vector2 position) => CrystalFormationDestroyed?.Invoke(position);
        public static void RaiseDoorActivated(string id, int cost) => DoorActivated?.Invoke(id, cost);
        public static void RaiseCrystalDeposited(int charge, int target) => CrystalDeposited?.Invoke(charge, target);
        public static void RaiseTutorialAdvanced(int step) => TutorialAdvanced?.Invoke(step);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            RewardGranted = null;
            CrystalCollected = null;
            CrystalFormationDestroyed = null;
            DoorActivated = null;
            CrystalDeposited = null;
            TutorialAdvanced = null;
        }
    }
}
