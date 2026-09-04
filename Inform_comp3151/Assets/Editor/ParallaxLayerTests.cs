#if UNITY_INCLUDE_TESTS
using Inkform.Fx;
using NUnit.Framework;
using UnityEngine;

namespace Inkform.Tests
{
    /// <summary>
    /// Wrap algebra of ParallaxLayer: shifting a seamless tiling by whole tiles is invisible,
    /// so wrapping must (a) only ever shift by whole tiles and (b) keep the layer within half a
    /// tile of the camera — that is what makes one 2x2-tile quad cover a level of any length.
    /// </summary>
    public class ParallaxLayerTests
    {
        private const float Tile = 20f;      // one repeat period, world units
        private const float HalfView = 8.9f; // half the camera view at ortho 5, 16:9 — must stay < Tile / 2

        [Test]
        public void WrapBranch_LandsOnBranchNearestCamera()
        {
            Assert.AreEqual(20f, ParallaxLayer.WrapBranch(0f, 25f, Tile));    // k = +1
            Assert.AreEqual(-20f, ParallaxLayer.WrapBranch(0f, -25f, Tile));  // k = -1
            Assert.AreEqual(0f, ParallaxLayer.WrapBranch(0f, 5f, Tile));      // already nearest
        }

        [Test]
        public void WrapBranch_ZeroTile_IsNoOp()
        {
            Assert.AreEqual(7.3f, ParallaxLayer.WrapBranch(7.3f, 500f, 0f));
            Assert.AreEqual(-2f, ParallaxLayer.WrapBranch(-2f, 500f, -1f));
        }

        [Test]
        public void WrapBranch_ShiftsOnlyByWholeTiles()
        {
            for (float desired = -45f; desired <= 45f; desired += 3.7f)
            {
                float wrapped = ParallaxLayer.WrapBranch(desired, 12f, Tile);
                float shiftInTiles = (wrapped - desired) / Tile;
                Assert.AreEqual(0f, shiftInTiles - Mathf.Round(shiftInTiles), 1e-3f,
                    $"desired={desired} shifted by a fractional tile — the pattern would jump");
            }
        }

        [Test]
        public void WrapBranch_StaysWithinHalfTileOfCamera_AnyTravel()
        {
            for (float travel = -300f; travel <= 300f; travel += 7.3f)
            {
                float desired = 13f + travel * 0.37f;
                float wrapped = ParallaxLayer.WrapBranch(desired, travel, Tile);
                Assert.LessOrEqual(Mathf.Abs(wrapped - travel), Tile * 0.5f + 1e-3f,
                    $"travel={travel}: quad edge would enter the view");
            }
        }

        [Test]
        public void ComputePosition_FactorZero_WorldFixed()
        {
            Vector2 pos = ParallaxLayer.ComputePosition(
                new Vector2(3f, 4f), Vector2.zero, new Vector2(500f, -200f),
                Vector2.zero, new Vector2(Tile, 10f), false, false);
            Assert.AreEqual(new Vector2(3f, 4f), pos);
        }

        [Test]
        public void ComputePosition_FactorOne_TracksCameraExactly()
        {
            Vector2 start = new Vector2(-7f, 2f);
            Vector2 camStart = new Vector2(11f, -3f);
            Vector2 camPos = new Vector2(60f, 8f);
            Vector2 pos = ParallaxLayer.ComputePosition(start, camStart, camPos,
                Vector2.one, new Vector2(Tile, 10f), false, false);
            Assert.AreEqual(start + (camPos - camStart), pos);   // screen-pinned
        }

        [Test]
        public void ComputePosition_Wrap_KeepsSlowLayerNearCameraOnLongLevel()
        {
            // The longest authored level geometry sits around x ~ 358 (Stones_0); walk the whole
            // thing with the slowest background (factor 0.3) and the wrapped layer must stay
            // within half a tile of the camera.
            Vector2 pos = ParallaxLayer.ComputePosition(Vector2.zero, Vector2.zero,
                new Vector2(358f, 40f), new Vector2(0.3f, 1f), new Vector2(Tile, 11.25f), true, true);
            Assert.LessOrEqual(Mathf.Abs(pos.x - 358f), Tile * 0.5f + 1e-3f);
            Assert.LessOrEqual(Mathf.Abs(pos.y - 40f), 11.25f * 0.5f + 1e-3f);
        }

        [Test]
        public void ComputePosition_WrappedPosition_StaysOutsideHalfViewWhenHopping()
        {
            // Wrap hops happen at half a tile; the half view must be smaller so hops occur
            // off-screen. This is a design invariant of the whole system — if it breaks, the
            // tile size or the camera view changed and the quad needs resizing.
            Assert.Less(HalfView, Tile * 0.5f);
        }

        [Test]
        public void ComputePosition_ForegroundFactor_MovesFasterThanCamera()
        {
            Vector2 pos = ParallaxLayer.ComputePosition(Vector2.zero, Vector2.zero,
                new Vector2(10f, 0f), new Vector2(1.15f, 1f), new Vector2(Tile, 11.25f), false, false);
            Assert.AreEqual(11.5f, pos.x, 1e-4f);   // 10 * 1.15 — passes by faster than the camera
        }
    }
}
#endif
