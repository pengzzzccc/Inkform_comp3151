using System.Numerics;

namespace Inkform.Core
{
    /// <summary>
    /// 附刚体抽象：Verlet 绳索末端所绑刚体的最小操作面（引擎无关）。
    /// Shell 侧用 Rigidbody2DAdapter 包 Unity 刚体。
    /// </summary>
    public interface IAttachedBody
    {
        /// <summary>世界位置。</summary>
        Vector2 Position { get; }

        /// <summary>旋转（度）。</summary>
        float Rotation { get; }

        /// <summary>当前速度。</summary>
        Vector2 Velocity { get; }

        /// <summary>累加速度（可为负 = 减速）。</summary>
        void AddVelocity(Vector2 delta);
    }
}
