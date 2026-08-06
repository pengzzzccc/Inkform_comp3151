using Inkform.Core;
using System.Numerics;
using UnityEngine;

namespace Inkform.Tool
{
    /// <summary>ITimePort 的 Unity 实现：包 Time 静态类。</summary>
    public sealed class UnityTimePort : ITimePort
    {
        public float Time => UnityEngine.Time.time;
        public float UnscaledTime => UnityEngine.Time.unscaledTime;
        public float DeltaTime => UnityEngine.Time.deltaTime;
    }

    /// <summary>IRngPort 的 Unity 实现：包 UnityEngine.Random。</summary>
    public sealed class UnityRngPort : IRngPort
    {
        public float Next01() => UnityEngine.Random.value;
        public float Range(float min, float max) => UnityEngine.Random.Range(min, max);
        public int Next(int max) => UnityEngine.Random.Range(0, max);
    }

    /// <summary>
    /// IPhysicsPort 的 Unity 实现（M0 最小面）：重力 + 圆形胶囊射线。
    /// M2 引入玩家运动学时在此扩展速度/接触探测等成员。
    /// </summary>
    public sealed class UnityPhysicsPort : IPhysicsPort
    {
        public Vector2 Gravity => new Vector2(Physics2D.gravity.x, Physics2D.gravity.y);

        public bool CastCircle(Vector2 from, float radius, Vector2 dir, float dist,
                               LayerKey mask, out float hitDistance)
        {
            RaycastHit2D hit = Physics2D.CircleCast(from.ToUnity(), radius, dir.ToUnity(), dist, mask.Mask);
            hitDistance = hit.distance;
            return hit.collider != null;
        }
    }
}
