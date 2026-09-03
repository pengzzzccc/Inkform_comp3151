using UnityEngine;

namespace Inkform.Tool
{
    /// <summary>
    /// One-shot timer: Set a duration, then query IsRunning / Remaining — no per-frame countdown needed.
    /// Uses Time.time, affected by Time.timeScale — gameplay timers (jump buffer, dash, fuse, lifetime) use this.
    /// </summary>
    [System.Serializable]
    public struct Timer
    {
        private float endTime;

        public void Set(float duration) => endTime = Time.time + duration;
        public bool IsRunning => Time.time < endTime;
        public float Remaining => Mathf.Max(0f, endTime - Time.time);
        public void Clear() => endTime = -999f;
    }

    /// <summary>
    /// The unscaled twin of Timer: uses Time.unscaledTime, unaffected by Time.timeScale.
    /// Hitstop crushes timeScale to 0, freezing Time.time — a plain Timer would never expire and
    /// permanently freeze the game. All effect timers and respawn waits must use this one.
    ///
    /// The two differ only in time source; they could be merged into a "Timer with a flag", but a
    /// missed parameter would silently degrade into the game-freezing version. Two types make a wrong
    /// clock a compile-time impossibility — kept in one file so the twin relationship is visible at once.
    /// </summary>
    [System.Serializable]
    public struct UnscaledTimer
    {
        private float endTime;

        public void Set(float duration) => endTime = Time.unscaledTime + duration;
        public bool IsRunning => Time.unscaledTime < endTime;
        public float Remaining => Mathf.Max(0f, endTime - Time.unscaledTime);
        public void Clear() => endTime = -999f;
    }
}
