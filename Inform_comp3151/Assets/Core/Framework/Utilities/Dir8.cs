using System;
using System.Numerics;

namespace Inkform.Core
{
    /// <summary>
    /// 把任意方向吸附到 8 个方向之一（上下左右 + 四个对角）。
    /// 用 cos/sin 还原成单位向量，8 个方向长度一致。
    /// </summary>
    public static class Dir8
    {
        public static Vector2 Snap(Vector2 v)
        {
            if (v.LengthSquared() < 0.0001f) return Vector2.UnitY;   // 零向量兜底：往上顶

            float deg = (float)Math.Round(Math.Atan2(v.Y, v.X) * 180.0 / Math.PI / 45.0) * 45f;
            float rad = deg * (float)Math.PI / 180f;
            return new Vector2((float)Math.Cos(rad), (float)Math.Sin(rad));
        }
    }
}
