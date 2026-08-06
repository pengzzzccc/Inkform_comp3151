namespace Inkform.Core
{
    /// <summary>
    /// 从数组里随机取一个非空元素：随机起点 + 环形遍历，
    /// 既保持起点等概率，又保证只要有一个非空就一定取得到。
    /// </summary>
    public static class RandomPick
    {
        /// <summary>全空（或数组本身为 null）时返回 null，兜底行为由调用方决定。</summary>
        public static T FromArray<T>(T[] array, IRngPort rng) where T : class
        {
            if (array == null || array.Length == 0) return null;

            int start = rng.Next(array.Length);
            for (int i = 0; i < array.Length; i++)
            {
                T item = array[(start + i) % array.Length];
                if (item != null) return item;
            }
            return null;
        }
    }
}
