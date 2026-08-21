using System;
using System.Collections.Generic;
using Inkform.Bus;
using UnityEngine;

namespace Inkform.Progression
{
    /// <summary>Single in-memory authority for persistent run progression outside the item inventory.</summary>
    public static class RunProgressStore
    {
        public static event Action Changed;

        private static readonly HashSet<string> activatedDoors = new HashSet<string>(StringComparer.Ordinal);
        private static int crystalCount;
        private static bool ropeGunUnlocked;
        private static bool elevatorControllerAcquired;
        private static int controlRoomCharge;
        private static int tutorialStep;

        public static int CrystalCount => crystalCount;
        public static bool RopeGunUnlocked => ropeGunUnlocked;
        public static bool ElevatorControllerAcquired => elevatorControllerAcquired;
        public static int ControlRoomCharge => controlRoomCharge;
        public static int TutorialStep => tutorialStep;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Changed = null;
            ClearInternal(false);
        }

        public static bool AddCrystals(int amount)
        {
            if (amount <= 0) return false;
            crystalCount = (int)Math.Min(int.MaxValue, (long)crystalCount + amount);
            Changed?.Invoke();
            return true;
        }

        public static bool GrantReward(ProgressionRewardType reward)
        {
            bool changed;
            switch (reward)
            {
                case ProgressionRewardType.RopeGun:
                    changed = !ropeGunUnlocked;
                    ropeGunUnlocked = true;
                    break;
                case ProgressionRewardType.ElevatorController:
                    changed = !elevatorControllerAcquired;
                    elevatorControllerAcquired = true;
                    break;
                default:
                    return false;
            }

            if (!changed) return false;
            Changed?.Invoke();
            ProgressionBus.RaiseRewardGranted(reward);
            return true;
        }

        public static bool IsDoorActivated(string id) =>
            !string.IsNullOrWhiteSpace(id) && activatedDoors.Contains(id);

        public static bool TryActivateDoor(string id, int cost)
        {
            if (string.IsNullOrWhiteSpace(id) || cost < 0) return false;
            if (activatedDoors.Contains(id)) return true;
            if (crystalCount < cost) return false;

            crystalCount -= cost;
            activatedDoors.Add(id);
            Changed?.Invoke();
            ProgressionBus.RaiseDoorActivated(id, cost);
            return true;
        }

        public static bool TryDepositOne(int targetCharge)
        {
            if (targetCharge <= 0 || controlRoomCharge >= targetCharge || crystalCount <= 0) return false;
            crystalCount--;
            controlRoomCharge++;
            Changed?.Invoke();
            ProgressionBus.RaiseCrystalDeposited(controlRoomCharge, targetCharge);
            return true;
        }

        public static bool SetTutorialStep(int step)
        {
            step = Mathf.Max(0, step);
            if (step <= tutorialStep) return false;
            tutorialStep = step;
            Changed?.Invoke();
            ProgressionBus.RaiseTutorialAdvanced(step);
            return true;
        }

        public static RunProgressSnapshot Snapshot()
        {
            var doors = new string[activatedDoors.Count];
            activatedDoors.CopyTo(doors);
            Array.Sort(doors, StringComparer.Ordinal);
            return new RunProgressSnapshot
            {
                crystalCount = crystalCount,
                ropeGunUnlocked = ropeGunUnlocked,
                elevatorControllerAcquired = elevatorControllerAcquired,
                controlRoomCharge = controlRoomCharge,
                tutorialStep = tutorialStep,
                activatedDoorIds = doors
            };
        }

        public static void Restore(RunProgressSnapshot snapshot)
        {
            ClearInternal(false);
            if (snapshot != null)
            {
                crystalCount = Mathf.Max(0, snapshot.crystalCount);
                ropeGunUnlocked = snapshot.ropeGunUnlocked;
                elevatorControllerAcquired = snapshot.elevatorControllerAcquired;
                controlRoomCharge = Mathf.Max(0, snapshot.controlRoomCharge);
                tutorialStep = Mathf.Max(0, snapshot.tutorialStep);
                if (snapshot.activatedDoorIds != null)
                {
                    foreach (string id in snapshot.activatedDoorIds)
                        if (!string.IsNullOrWhiteSpace(id)) activatedDoors.Add(id);
                }
            }
            Changed?.Invoke();
        }

        public static void Clear() => ClearInternal(true);

        private static void ClearInternal(bool notify)
        {
            crystalCount = 0;
            ropeGunUnlocked = false;
            elevatorControllerAcquired = false;
            controlRoomCharge = 0;
            tutorialStep = 0;
            activatedDoors.Clear();
            if (notify) Changed?.Invoke();
        }
    }
}
