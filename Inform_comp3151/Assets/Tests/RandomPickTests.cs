using Inkform.Core;
using NUnit.Framework;

namespace Inkform.Core.Tests
{
    public class RandomPickTests
    {
        [Test]
        public void Null_Array_Returns_Null()
        {
            var rng = new FakeRng(0f);
            Assert.IsNull(RandomPick.FromArray<string>(null, rng));
        }

        [Test]
        public void Empty_Array_Returns_Null()
        {
            var rng = new FakeRng(0f);
            Assert.IsNull(RandomPick.FromArray(new string[0], rng));
        }

        [Test]
        public void All_Null_Slots_Returns_Null()
        {
            var rng = new FakeRng(0f);
            Assert.IsNull(RandomPick.FromArray(new string[] { null, null, null }, rng));
        }

        [Test]
        public void Start_On_Valid_Slot_Returns_It()
        {
            // Next(3) = 1 → 直接命中
            var rng = new FakeRng(1f / 3f);
            string[] array = { "A", "B", "C" };
            Assert.AreEqual("B", RandomPick.FromArray(array, rng));
        }

        [Test]
        public void Start_On_Null_Slot_Skips_To_Valid()
        {
            // Next(3) = 0 → array[0] 为空 → 环形遍历到 array[1]
            var rng = new FakeRng(0f / 3f);
            string[] array = { null, "B", null };
            Assert.AreEqual("B", RandomPick.FromArray(array, rng));
        }
    }
}
