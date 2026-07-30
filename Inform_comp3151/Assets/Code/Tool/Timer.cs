using UnityEngine;

namespace Inkform.Tool
{
    /// <summary>
    /// 一次性到期计时器：Set 一个时长，之后靠 IsRunning / Remaining 查询，不需要每帧递减。
    /// 走 Time.time，受 Time.timeScale 影响 —— 玩法计时（跳跃缓冲、冲刺、引信、寿命）都用这个。
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
    /// Timer 的非缩放孪生版：走 Time.unscaledTime，不受 Time.timeScale 影响。
    /// hitstop 会把 timeScale 压到 0，此时普通 Timer 的 Time.time 停止推进、永不到期，
    /// 拿它计时恢复就会把游戏永久冻死 —— 所有特效计时和复活等待必须用这个。
    ///
    /// 两者只差一个时间源，看着该合成一个「带 flag 的 Timer」，但那样一旦漏传参数就会静默
    /// 退化成会冻死游戏的那一版。拆成两个类型，用错时钟在编译期就不可能发生 ——
    /// 放在同一个文件里是为了让这对孪生关系一眼可见。
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
