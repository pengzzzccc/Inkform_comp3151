using System;

namespace Inkform.Progression
{
    [Serializable]
    public sealed class RunProgressSnapshot
    {
        public int crystalCount;
        public bool ropeGunUnlocked;
        public bool elevatorControllerAcquired;
        public int controlRoomCharge;
        public int tutorialStep;
        public string[] activatedDoorIds = Array.Empty<string>();
    }
}
