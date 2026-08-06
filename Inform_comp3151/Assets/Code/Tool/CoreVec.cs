using System.Numerics;
using UnityEngine;

namespace Inkform.Tool
{
    /// <summary>
    /// UnityEngine ⇄ System.Numerics 向量转换扩展。
    /// Core 与 Shell 之间所有 Vector2 往来都必须走这里。
    /// </summary>
    public static class CoreVec
    {
        public static Vector2 ToUnity(this System.Numerics.Vector2 v) => new Vector2(v.X, v.Y);

        public static System.Numerics.Vector2 ToCore(this Vector2 v) => new System.Numerics.Vector2(v.x, v.y);

        public static Vector3 ToUnity3(this System.Numerics.Vector2 v) => new Vector3(v.X, v.Y, 0f);
    }
}
