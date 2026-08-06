using Inkform.Core;
using NUnit.Framework;

namespace Inkform.Core.Tests
{
    public class TimerTests
    {
        [Test]
        public void Set_Then_IsRunning_True_Until_Expiry()
        {
            var clock = new FakeTimePort();
            var t = new Timer(clock);

            Assert.IsFalse(t.IsRunning);

            clock.Now = 10f;
            t.Set(2f);
            Assert.IsTrue(t.IsRunning);

            clock.Now = 11.9f;
            Assert.IsTrue(t.IsRunning);
            Assert.AreEqual(0.1f, t.Remaining, 1e-4f);

            clock.Now = 12f;
            Assert.IsFalse(t.IsRunning);
            Assert.AreEqual(0f, t.Remaining);
        }

        [Test]
        public void Remaining_Never_Negative()
        {
            var clock = new FakeTimePort();
            var t = new Timer(clock);

            t.Set(1f);
            clock.Now = 100f;
            Assert.AreEqual(0f, t.Remaining);
        }

        [Test]
        public void Clear_Stops_Running()
        {
            var clock = new FakeTimePort();
            var t = new Timer(clock);

            t.Set(1f);
            t.Clear();
            Assert.IsFalse(t.IsRunning);
        }

        [Test]
        public void UnscaledTimer_Uses_UnscaledClock()
        {
            var clock = new FakeTimePort();
            var t = new UnscaledTimer(clock);

            clock.Now = 5f;
            clock.NowUnscaled = 5f;
            t.Set(1f);

            // hitstop：缩放时钟冻结，非缩放照常推进 → 非缩放计时器必须仍然到期
            clock.AdvanceUnscaledOnly(2f);
            Assert.IsFalse(t.IsRunning);
        }

        [Test]
        public void ScaledTimer_Freezes_During_Hitstop()
        {
            var clock = new FakeTimePort();
            var t = new Timer(clock);

            clock.Now = 0f;
            t.Set(1f);
            clock.AdvanceUnscaledOnly(5f);      // 缩放时钟不动
            Assert.IsTrue(t.IsRunning);
        }
    }
}
