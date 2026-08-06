using Inkform.Core;
using System.Numerics;
using UnityEngine;

namespace Inkform.Tool
{
    /// <summary>
    /// IAttachedBody 的 Unity 实现：包 Rigidbody2D。
    /// 位置/旋转直接读刚体（工程 AutoSyncTransforms=0，刚体才是物理权威）。
    /// </summary>
    public sealed class Rigidbody2DAdapter : IAttachedBody
    {
        private readonly Rigidbody2D body;

        public Rigidbody2DAdapter(Rigidbody2D body) => this.body = body;

        public Vector2 Position => new Vector2(body.position.x, body.position.y);

        /// <summary>旋转（度）。</summary>
        public float Rotation => body.rotation;

        public Vector2 Velocity => new Vector2(body.linearVelocity.x, body.linearVelocity.y);

        public void AddVelocity(Vector2 delta)
            => body.linearVelocity += new Vector2(delta.X, delta.Y);
    }
}
