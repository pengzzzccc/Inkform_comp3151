using System;
using System.Numerics;

namespace Inkform.Core
{
    /// <summary>
    /// 一次性到期计时器（时钟注入式）：Set 一个时长，之后靠 IsRunning / Remaining 查询。
    /// 时间源由 ITimePort 提供 —— 缩放/非缩放语义由端口实现决定，测试可注入假时钟。
    /// </summary>
    [Serializable]
    public struct Timer
    {
        private readonly ITimePort clock;
        private float endTime;

        public Timer(ITimePort clock)
        {
            this.clock = clock;
            endTime = float.MinValue;
        }

        public void Set(float duration) => endTime = clock.Time + duration;
        public bool IsRunning => clock.Time < endTime;
        public float Remaining => Math.Max(0f, endTime - clock.Time);
        public void Clear() => endTime = float.MinValue;
    }

    /// <summary>
    /// 非缩放孪生版：走 UnscaledTime，hitstop 期间照常推进。
    /// 两个类型拆开而不是合成一个带 flag 的 Timer —— 用错时钟在编译期就不可能发生。
    /// </summary>
    [Serializable]
    public struct UnscaledTimer
    {
        private readonly ITimePort clock;
        private float endTime;

        public UnscaledTimer(ITimePort clock)
        {
            this.clock = clock;
            endTime = float.MinValue;
        }

        public void Set(float duration) => endTime = clock.UnscaledTime + duration;
        public bool IsRunning => clock.UnscaledTime < endTime;
        public float Remaining => Math.Max(0f, endTime - clock.UnscaledTime);
        public void Clear() => endTime = float.MinValue;
    }
}
