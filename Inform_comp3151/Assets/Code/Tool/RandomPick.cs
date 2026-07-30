using UnityEngine;

namespace Inkform.Tool
{
    /// <summary>
    /// 从 Inspector 数组里随机取一个非空元素。SoundCue 取音频变体、FragmentCue 取碎块外观，
    /// 两边原本各写了一份一模一样的循环。
    ///
    /// 不能直接 arr[Random.Range(0, arr.Length)]：Inspector 里留空槽位很常见，
    /// 取到 null 之后调用方就得到一个「什么都没配」的结果 —— 音效不响、碎块凭空少一片。
    /// 随机起点 + 环形遍历：既保持起点等概率，又保证只要数组里有一个非空就一定取得到。
    /// </summary>
    public static class RandomPick
    {
        /// <summary>全空（或数组本身为 null）时返回 null，兜底行为由调用方决定。</summary>
        public static T FromArray<T>(T[] array) where T : UnityEngine.Object
        {
            if (array == null || array.Length == 0) return null;

            int start = Random.Range(0, array.Length);
            for (int i = 0; i < array.Length; i++)
            {
                T item = array[(start + i) % array.Length];

                // 必须先转成 UnityEngine.Object 再比：泛型 T 上的 != null 走的是引用比较，
                // 绕开了 Unity 重载的 ==，已销毁的资产会被误判成「非空」
                if ((UnityEngine.Object)item != null) return item;
            }
            return null;
        }
    }
}
