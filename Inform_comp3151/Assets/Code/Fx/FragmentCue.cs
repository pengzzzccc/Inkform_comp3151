using Inkform.Tool;
using UnityEngine;

namespace Inkform.Fx
{
    /// <summary>
    /// Config for one shatter look: fragment prefab + multiple random looks + slicing/force/lifetime
    /// parameters, pure data asset. Created via Assets > Create > Fx > Fragment Cue, referenced by
    /// BreakablePart / PlayerDeathFx in the Inspector. The spawning itself is done by Shatter;
    /// this class only describes "what the shatter should look like".
    /// </summary>
    [CreateAssetMenu(menuName = "Fx/Fragment Cue")]
    public class FragmentCue : ScriptableObject
    {
        [Header("Look")]
        [Tooltip("Fragment prefab, must carry Rigidbody2D + SpriteRenderer + Fragment")]
        public GameObject prefab;
        [Tooltip("Multiple looks; each shard picks one at random to avoid repetition. Empty slots are skipped; an entirely empty array keeps the prefab's own sprite")]
        public Sprite[] sprites;
        [Tooltip("Extra random factor on top of 'uniform fit into the cell', so shards from one shatter vary in size")]
        public Vector2 scaleRange = new Vector2(0.85f, 1.1f);
        [Tooltip("Random horizontal/vertical flip, further breaking up repetition")]
        public bool randomFlip = true;
        public Color tint = Color.white;

        [Header("Shatter")]
        public int cellsX = 2;                                  // how many slices horizontally
        public int cellsY = 3;                                  // how many slices vertically
        [Tooltip("Shard velocity = blast force × this factor")]
        [Range(0f, 2f)] public float forceMultiplier = 0.6f;
        [Tooltip("Upper bound of random spin angular velocity")]
        public float spinSpeed = 180f;

        [Header("Life")]
        public float lifeTime = 3f;
        public float fadeTime = 1f;                             // fade-out duration at end of life

        // Scale baseline cache. A ScriptableObject is an asset, its instance persists in editor memory,
        // so this cache does not clear when exiting play mode — invalidated by OnValidate below whenever
        // sprites change.
        [System.NonSerialized] private Vector2 maxSpriteSize;
        [System.NonSerialized] private bool measured;

        /// <summary>Picks a random look. Empty slots are skipped; returns null when all are empty (the caller keeps the prefab's original sprite).</summary>
        public Sprite PickSprite() => RandomPick.FromArray(sprites);

        public float PickScale() =>
            Random.Range(scaleRange.x, scaleRange.y);

        /// <summary>
        /// World size of the largest sprite in the atlas; shard scale is based on it:
        /// the largest shard exactly fills the cell, smaller shards stay small per the art's proportions.
        /// If each shard filled its own cell, a 6×7 tiny shard would blow up to match a 27×20 one —
        /// visibly blurry pixels.
        /// </summary>
        public Vector2 MaxSpriteSize
        {
            get
            {
                if (measured) return maxSpriteSize;
                measured = true;

                // Seed must be zero, not one: when every slice is under 1 unit, a running max starting
                // from one would forever sit at 1, shrinking all shards
                Vector2 max = Vector2.zero;
                if (sprites != null)
                {
                    foreach (Sprite s in sprites)
                    {
                        if (s == null) continue;
                        max = Vector2.Max(max, s.bounds.size);
                    }
                }

                maxSpriteSize = (max.x > 0f && max.y > 0f) ? max : Vector2.one;
                return maxSpriteSize;
            }
        }

        // After editing sprites in the Inspector the baseline must recompute, or new art still scales
        // against the old size
        void OnValidate() => measured = false;
    }
}
