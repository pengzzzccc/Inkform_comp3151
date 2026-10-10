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
}
