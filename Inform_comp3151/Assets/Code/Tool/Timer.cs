using UnityEngine;

[System.Serializable]
public struct Timer
{
    private float endTime;

    public void Set(float duration) => endTime = Time.time + duration;
    public bool IsRunning => Time.time < endTime;
    public float Remaining => Mathf.Max(0f, endTime - Time.time);
    public void Clear() => endTime = -999f;
}