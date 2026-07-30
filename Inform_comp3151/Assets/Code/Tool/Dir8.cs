using UnityEngine;

namespace Inkform.Tool
{
    /// <summary>
    /// 把任意方向吸附到 8 个方向之一（上下左右 + 四个对角）。
    /// 用 cos/sin 还原成单位向量，8 个方向长度一致，乘同一个力度就得到同样强的击退。
    /// </summary>
    public static class Dir8
    {
        public static Vector2 Snap(Vector2 v)
        {
            if (v.sqrMagnitude < 0.0001f) return Vector2.up;   // 两者完全重合时的兜底：往上顶

            float deg = Mathf.Round(Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg / 45f) * 45f;
            float rad = deg * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
        }
    }
}
