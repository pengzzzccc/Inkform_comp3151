using System;
using System.Numerics;
using Inkform.Core;
using NUnit.Framework;

namespace Inkform.Core.Tests
{
    public class VerletRopeTests
    {
        private const float SegLen = 0.25f;

        private static VerletRope NewRope(out FakePhysicsPort physics)
        {
            var rope = new VerletRope();
            physics = new FakePhysicsPort { Gravity = Vector2.Zero };
            rope.Physics = physics;
            return rope;
        }

        private static VerletRope.Settings DefaultSettings()
        {
            var s = VerletRope.Settings.Default();
            s.segmentLength = SegLen;
            s.maxSegments = 32;
            s.collisionMask = 0;
            return s;
        }

        [Test]
        public void SolveFixed_Without_PhysicsPort_Throws()
        {
            var rope = new VerletRope();
            rope.Init(DefaultSettings(), Vector2.Zero, new Vector2(1f, 0f));
            Assert.Throws<InvalidOperationException>(
                () => rope.SolveFixed(0.02f, Vector2.Zero, rope.SegmentCount, null, null));
        }

        [Test]
        public void Init_Straight_Line_Then_Constraints_Converge()
        {
            var rope = NewRope(out _);
            rope.Init(DefaultSettings(), Vector2.Zero, new Vector2(2.5f, 0f));
            Assert.AreEqual(10, rope.SegmentCount);

            for (int step = 0; step < 200; step++)
                rope.SolveFixed(0.02f, Vector2.Zero, rope.SegmentCount, null, null);

            for (int i = 0; i < rope.SegmentCount; i++)
            {
                float len = (rope.GetPoint(i + 1) - rope.GetPoint(i)).Length();
                Assert.AreEqual(SegLen, len, 1e-2f, $"段 {i} 未收敛");
            }
            AssertAllFinite(rope);
        }

        [Test]
        public void Free_Hanging_Does_Not_Diverge()
        {
            var rope = NewRope(out FakePhysicsPort physics);
            physics.Gravity = new Vector2(0f, -9.81f);

            // 斜拉直绳，自由悬垂 300 步
            rope.Init(DefaultSettings(), Vector2.Zero, new Vector2(1.5f, -1.5f));
            for (int step = 0; step < 300; step++)
                rope.SolveFixed(0.02f, Vector2.Zero, rope.SegmentCount, null, null);

            AssertAllFinite(rope);
            float maxLen = 0f;
            for (int i = 0; i < rope.SegmentCount; i++)
                maxLen = Math.Max(maxLen, (rope.GetPoint(i + 1) - rope.GetPoint(i)).Length());
            Assert.Less(maxLen, SegLen * 1.5f, "悬垂发散");
        }

        [Test]
        public void SetLength_Shorten_Then_Lengthen()
        {
            var rope = NewRope(out _);
            rope.Init(DefaultSettings(), Vector2.Zero, new Vector2(2.5f, 0f));
            Assert.AreEqual(10, rope.SegmentCount);

            rope.SetLength(1.25f);
            Assert.AreEqual(5, rope.SegmentCount);

            rope.SetLength(3f);
            Assert.AreEqual(12, rope.SegmentCount);
        }

        [Test]
        public void SetLength_Shorten_Keeps_EndPoint_At_Old_Tip()
        {
            var rope = NewRope(out _);
            rope.Init(DefaultSettings(), Vector2.Zero, new Vector2(2.5f, 0f));
            Vector2 oldTip = rope.EndPoint;

            rope.SetLength(1.25f);
            Assert.AreEqual(oldTip, rope.EndPoint, "缩短后末端必须留在原处（速度不瞬移）");
        }

        [Test]
        public void Attached_Body_Is_Held_By_Rope_Not_Falling()
        {
            var rope = NewRope(out FakePhysicsPort physics);
            physics.Gravity = new Vector2(0f, -9.81f);

            var body = new FakeAttachedBody { Position = new Vector2(0f, -2f) };
            rope.Init(DefaultSettings(), Vector2.Zero, body.Position);

            for (int step = 0; step < 200; step++)
            {
                rope.SolveFixed(0.02f, Vector2.Zero, rope.SegmentCount, body, null);
                body.Integrate(0.02f);      // 模拟引擎积分
            }

            // 绳长 2.0：附体应被吊在链端（不会坠出绳长，也不会被吸进锚点）
            float dist = body.Position.Length();
            Assert.Greater(dist, 1.0f, "附体不应被拉进绳长 1.0 以内");
            Assert.Less(dist, 2.6f, "附体不应坠出绳长范围");
            Assert.Less(body.Velocity.Length(), 10f, "速度应被绳约束（不发散）");
            AssertAllFinite(rope);
        }

        [Test]
        public void ReelIn_Shortens_Rope_And_Pulls_Body_Toward_Anchor()
        {
            var rope = NewRope(out FakePhysicsPort physics);
            physics.Gravity = Vector2.Zero;

            var body = new FakeAttachedBody { Position = new Vector2(0f, -2f) };
            rope.Init(DefaultSettings(), Vector2.Zero, body.Position);
            float ropeLen = 2f;

            for (int step = 0; step < 100; step++)
            {
                ropeLen = Math.Max(0.2f, ropeLen - 0.02f);   // 模拟收绳：绳长逐帧缩短
                rope.SetLength(ropeLen);
                rope.SolveFixed(0.02f, Vector2.Zero, rope.SegmentCount, body, null);
                body.Integrate(0.02f);
            }

            // 绳长收到 0.2 → 附体应被平滑拉近锚点（链端 ≈ 0.45 内）
            Assert.Less(body.Position.Length(), 1.0f, "收绳后附体应被拉近锚点");
            AssertAllFinite(rope);
        }

        [Test]
        public void EndPin_Stays_At_Pin_Each_Iteration()
        {
            var rope = NewRope(out _);
            Vector2 pin = new Vector2(1f, 0.5f);
            rope.Init(DefaultSettings(), Vector2.Zero, new Vector2(1f, 0f));

            for (int step = 0; step < 30; step++)
                rope.SolveFixed(0.02f, Vector2.Zero, rope.SegmentCount, null, pin);

            Assert.AreEqual(pin, rope.EndPoint, "钉点模式末端必须钉在 pin 上");
        }

        [Test]
        public void CircleCast_Hit_Clamps_Segment_Before_Contact()
        {
            var rope = NewRope(out FakePhysicsPort physics);
            physics.Gravity = Vector2.Zero;
            physics.AlwaysHit = true;
            physics.HitDistance = 0.2f;

            var s = DefaultSettings();
            s.collisionMask = new LayerKey(1);      // 开启段碰撞
            s.collisionRadius = 0.04f;
            rope.Init(s, Vector2.Zero, new Vector2(2.5f, 0f));

            rope.SolveFixed(0.02f, Vector2.Zero, rope.SegmentCount, null, null);

            // 第一段起点 (0,0)：命中距离 0.2 → 钳到 0.2 - 0.04 = 0.16
            Assert.Greater(physics.CastCount, 0, "碰撞检测应被调用");
            Assert.AreEqual(0.16f, rope.GetPoint(1).X, 1e-3f, "第一段末端应被钳到 0.16");
        }

        [Test]
        public void Anchor_Coincident_With_EndPin_No_NaN()
        {
            var rope = NewRope(out _);
            Vector2 at = new Vector2(3f, 3f);
            rope.Init(DefaultSettings(), at, at);   // 零距离 → 段数钳到 1

            for (int step = 0; step < 10; step++)
                rope.SolveFixed(0.02f, at, rope.SegmentCount, null, at);

            AssertAllFinite(rope);
        }

        [Test]
        public void Rotate_Matches_90_Degrees()
        {
            Vector2 v = VerletRope.Rotate(new Vector2(1f, 0f), 90f);
            Assert.AreEqual(0f, v.X, 1e-4f);
            Assert.AreEqual(1f, v.Y, 1e-4f);
        }

        private static void AssertAllFinite(VerletRope rope)
        {
            for (int i = 0; i <= rope.SegmentCount; i++)
            {
                Vector2 p = rope.GetPoint(i);
                Assert.IsTrue(float.IsFinite(p.X) && float.IsFinite(p.Y), $"点 {i} 含 NaN/Inf");
            }
        }
    }
}
