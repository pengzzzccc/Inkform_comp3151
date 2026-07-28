using UnityEngine;

/// <summary>
/// Timer 的非缩放孪生版：走 Time.unscaledTime，不受 Time.timeScale 影响。
/// hitstop 会把 timeScale 压到 0，此时普通 Timer 的 Time.time 停止推进、永不到期，
/// 拿它计时恢复就会把游戏永久冻死——所有特效计时必须用这个。
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
