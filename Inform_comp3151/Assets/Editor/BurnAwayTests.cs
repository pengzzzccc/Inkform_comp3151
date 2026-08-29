#if UNITY_INCLUDE_TESTS
using Inkform.Fx;
using NUnit.Framework;
using UnityEngine;

namespace Inkform.Tests
{
    /// <summary>
    /// Pins the pure math BurnAwayFx uses to place the shader's burn front: world → sprite UV
    /// conversion (including the flipX/flipY mirroring) and the farthest-corner distance that
    /// decides when the whole body has burned through.
    /// </summary>
    public sealed class BurnAwayTests
    {
        private static readonly Bounds Unit = new Bounds(new Vector3(0.5f, 0.5f, 0f), new Vector3(1f, 1f, 0f));

        [Test]
        public void Center_MapsToUvCenter()
        {
            Vector2 uv = BurnAwayFx.WorldToSpriteUV(new Vector2(0.5f, 0.5f), Unit, flipX: false, flipY: false);

            Assert.AreEqual(0.5f, uv.x, 1e-4f);
            Assert.AreEqual(0.5f, uv.y, 1e-4f);
        }

        [Test]
        public void Corners_MapToUvCorners()
        {
            Vector2 bottomLeft = BurnAwayFx.WorldToSpriteUV(Vector2.zero, Unit, flipX: false, flipY: false);
            Vector2 topRight = BurnAwayFx.WorldToSpriteUV(new Vector2(1f, 1f), Unit, flipX: false, flipY: false);

            Assert.AreEqual(0f, bottomLeft.x, 1e-4f);
            Assert.AreEqual(0f, bottomLeft.y, 1e-4f);
            Assert.AreEqual(1f, topRight.x, 1e-4f);
            Assert.AreEqual(1f, topRight.y, 1e-4f);
        }

        [Test]
        public void FlipX_MirrorsU()
        {
            // The world-space left edge wears texture u=1 when the sprite is flipped — a contact
            // point on the player's left must ignite the texture's left as it appears on screen
            Vector2 left = BurnAwayFx.WorldToSpriteUV(Vector2.zero, Unit, flipX: true, flipY: false);

            Assert.AreEqual(1f, left.x, 1e-4f);
            Assert.AreEqual(0f, left.y, 1e-4f);
        }

        [Test]
        public void FlipY_MirrorsV()
        {
            Vector2 bottom = BurnAwayFx.WorldToSpriteUV(Vector2.zero, Unit, flipX: false, flipY: true);

            Assert.AreEqual(0f, bottom.x, 1e-4f);
            Assert.AreEqual(1f, bottom.y, 1e-4f);
        }

        [Test]
        public void OutsidePoints_ClampIntoUv()
        {
            // The kill point sits on the hazard's surface, often a hair outside the body's bounds
            Vector2 uv = BurnAwayFx.WorldToSpriteUV(new Vector2(-0.25f, 1.5f), Unit, flipX: false, flipY: false);

            Assert.AreEqual(0f, uv.x, 1e-4f);
            Assert.AreEqual(1f, uv.y, 1e-4f);
        }

        [Test]
        public void MaxCornerDistance_CornerOrigin_IsDiagonal()
        {
            // Igniting at the bottom-left of a square sprite: the farthest corner is the
            // top-right, a full √2 away — the front must travel that far to finish the body
            float d = BurnAwayFx.MaxCornerDistance(new Vector2(0f, 0f), aspect: 1f);

            Assert.AreEqual(Mathf.Sqrt(2f), d, 1e-4f);
        }

        [Test]
        public void MaxCornerDistance_AspectWidensTravel()
        {
            // A 2:1 wide sprite: corners sit at x ∈ {0, 2}, so the travel is longer than square
            float d = BurnAwayFx.MaxCornerDistance(new Vector2(0f, 0.5f), aspect: 2f);

            Assert.AreEqual(Mathf.Sqrt(2f * 2f + 0.5f * 0.5f), d, 1e-4f);
        }
    }
}
#endif
