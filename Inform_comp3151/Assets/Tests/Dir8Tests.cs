using System.Numerics;
using Inkform.Core;
using NUnit.Framework;

namespace Inkform.Core.Tests
{
    public class Dir8Tests
    {
        [Test]
        public void Snap_Cardinal_Directions()
        {
            AssertVec(Dir8.Snap(new Vector2(1f, 0f)), 1f, 0f);
            AssertVec(Dir8.Snap(new Vector2(-1f, 0f)), -1f, 0f);
            AssertVec(Dir8.Snap(new Vector2(0f, 1f)), 0f, 1f);
            AssertVec(Dir8.Snap(new Vector2(0f, -1f)), 0f, -1f);
        }

        [Test]
        public void Snap_Diagonal_Directions()
        {
            float k = 0.70710678f;
            AssertVec(Dir8.Snap(new Vector2(1f, 1f)), k, k);
            AssertVec(Dir8.Snap(new Vector2(-1f, 1f)), -k, k);
            AssertVec(Dir8.Snap(new Vector2(1f, -1f)), k, -k);
            AssertVec(Dir8.Snap(new Vector2(-1f, -1f)), -k, -k);
        }

        [Test]
        public void Snap_OffAxis_Rounds_To_Nearest()
        {
            // 接近对角方向 → 吸附对角
            AssertVec(Dir8.Snap(new Vector2(0.9f, 0.7f)), 0.70710678f, 0.70710678f);
        }

        [Test]
        public void Snap_ZeroVector_FallsBack_To_Up()
        {
            AssertVec(Dir8.Snap(Vector2.Zero), 0f, 1f);
        }

        private static void AssertVec(Vector2 v, float x, float y)
        {
            Assert.AreEqual(x, v.X, 1e-4f, "X");
            Assert.AreEqual(y, v.Y, 1e-4f, "Y");
        }
    }
}
