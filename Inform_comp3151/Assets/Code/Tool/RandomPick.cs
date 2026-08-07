using UnityEngine;

namespace Inkform.Tool
{
    /// <summary>
    /// Picks a random non-null element from an Inspector array. SoundCue picks audio variants,
    /// FragmentCue picks fragment looks — both used to carry an identical copy of this loop.
    ///
    /// Cannot just use arr[Random.Range(0, arr.Length)]: empty slots are common in the Inspector,
    /// and picking null gives the caller an "nothing configured" result — no sound, one missing shard.
    /// Random start + circular walk: keeps the start uniformly distributed, and guarantees a hit
    /// as long as at least one element is non-null.
    /// </summary>
    public static class RandomPick
    {
        /// <summary>Returns null when everything is empty (or the array itself is null); fallback is up to the caller.</summary>
        public static T FromArray<T>(T[] array) where T : UnityEngine.Object
        {
            if (array == null || array.Length == 0) return null;

            int start = Random.Range(0, array.Length);
            for (int i = 0; i < array.Length; i++)
            {
                T item = array[(start + i) % array.Length];

                // Must cast to UnityEngine.Object before comparing: != null on generic T uses
                // reference comparison, bypassing Unity's overloaded ==, so destroyed assets
                // would be misjudged as "non-null"
                if ((UnityEngine.Object)item != null) return item;
            }
            return null;
        }
    }
}
