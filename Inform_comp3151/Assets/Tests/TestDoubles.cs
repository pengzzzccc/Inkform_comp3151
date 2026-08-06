using Inkform.Core;
using System.Numerics;

namespace Inkform.Core.Tests
{
    /// <summary>可控时钟：测试里手动推进。</summary>
    public sealed class FakeTimePort : ITimePort
    {
        public float Now;
        public float NowUnscaled;
        public float Dt;

        float ITimePort.Time => Now;
        float ITimePort.UnscaledTime => NowUnscaled;
        float ITimePort.DeltaTime => Dt;

        /// <summary>同时推进两个时钟。</summary>
        public void Advance(float dt) { Now += dt; NowUnscaled += dt; }

        /// <summary>只推进非缩放时钟（模拟 hitstop：缩放时钟冻结）。</summary>
        public void AdvanceUnscaledOnly(float dt) => NowUnscaled += dt;
    }

    /// <summary>确定性随机源：Next01/Next 返回固定序列。</summary>
    public sealed class FakeRng : IRngPort
    {
        private readonly float[] sequence;
        private int index;

        public FakeRng(params float[] sequence) => this.sequence = sequence;

        private float NextValue() => sequence.Length == 0 ? 0f : sequence[index++ % sequence.Length];

        public float Next01() => NextValue();

        public float Range(float min, float max) => min + (max - min) * NextValue();

        public int Next(int max)
        {
            if (max <= 0) return 0;
            int v = (int)(NextValue() * max);
            return v >= max ? max - 1 : v;
        }
    }

    /// <summary>可预设命中行为的物理端口：默认永不命中。</summary>
    public sealed class FakePhysicsPort : IPhysicsPort
    {
        public Vector2 Gravity = new Vector2(0f, -9.81f);
        public bool AlwaysHit;
        public float HitDistance = 0.2f;
        public int CastCount;

        Vector2 IPhysicsPort.Gravity => Gravity;

        public bool CastCircle(Vector2 from, float radius, Vector2 dir, float dist,
                               LayerKey mask, out float hitDistance)
        {
            CastCount++;
            hitDistance = AlwaysHit ? HitDistance : 0f;
            return AlwaysHit;
        }
    }

    /// <summary>可控附体：位置/速度由测试手动积分。</summary>
    public sealed class FakeAttachedBody : IAttachedBody
    {
        public Vector2 Position;
        public float Rotation;
        public Vector2 Velocity;

        Vector2 IAttachedBody.Position => Position;
        float IAttachedBody.Rotation => Rotation;
        Vector2 IAttachedBody.Velocity => Velocity;

        public void AddVelocity(Vector2 delta) => Velocity += delta;

        /// <summary>模拟引擎积分：把速度写回位置。</summary>
        public void Integrate(float dt) => Position += Velocity * dt;
    }
}
