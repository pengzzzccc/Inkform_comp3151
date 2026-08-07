using Inkform.Tool;
using UnityEngine;

namespace Inkform.Fx
{
    /// <summary>
    /// Fragment: look, size, and initial velocity are all written by Shatter at spawn according to the
    /// FragmentCue; self-destructs after lifeTime, fading out over the last fadeTime seconds so shards
    /// do not vanish out of thin air.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(SpriteRenderer))]
    public class Fragment : MonoBehaviour
    {
        [Header("Fragment setting")]
        // Fallback values when dropped into a scene without a Cue; shards spawned by Shatter get
        // overwritten by the Cue in Apply
        [SerializeField] private float lifeTime = 3f;
        [SerializeField] private float fadeTime = 1f;   // fade-out duration at end of life

        private SpriteRenderer sprite;
        private BoxCollider2D box;      // may be absent, allowed to be null
        private Timer LifeTimer;
        private float baseAlpha = 1f;   // alpha carried by the Cue's tint; fade caps at this

        void Awake()
        {
            sprite = GetComponent<SpriteRenderer>();
            TryGetComponent(out box);
            LifeTimer.Set(lifeTime);
        }

        /// <summary>
        /// Called by Shatter immediately after Instantiate: picks a random look and scales to fit the cell.
        /// Must run after Awake — the sprite cache and the life timer are initialized there.
        /// </summary>
        public void Apply(FragmentCue cue, Vector2 cell)
        {
            lifeTime = cue.lifeTime;
            fadeTime = cue.fadeTime;
            LifeTimer.Set(lifeTime);            // Awake already started once with the prefab's old values; restart per the Cue

            Sprite pick = cue.PickSprite();
            if (pick != null) sprite.sprite = pick;     // when the Cue has no sprites configured, keep the prefab's own
            sprite.color = cue.tint;
            baseAlpha = cue.tint.a;

            if (cue.randomFlip)
            {
                sprite.flipX = Random.value < 0.5f;
                sprite.flipY = Random.value < 0.5f;
            }

            // Uniform scale, based on "the largest sprite in the atlas" rather than this shard itself —
            // per-axis stretching distorts hand-drawn silhouettes, and basing it on the shard itself
            // would blow small shards up to match big ones
            Vector2 basis = cue.MaxSpriteSize;
            float s = Mathf.Min(cell.x / basis.x, cell.y / basis.y) * cue.PickScale();
            transform.localScale = new Vector3(s, s, 1f);

            // Collider follows the sprite's actual outline: the prefab's 1×1 box does not fit irregular
            // shards — unchanged, shards would hover above ground and shove each other with far larger
            // invisible boxes
            if (box != null && sprite.sprite != null) box.size = sprite.sprite.bounds.size;
        }

        void Update()
        {
            if (!LifeTimer.IsRunning) { Destroy(gameObject); return; }

            if (fadeTime > 0f && LifeTimer.Remaining < fadeTime)
            {
                Color c = sprite.color;
                c.a = baseAlpha * LifeTimer.Remaining / fadeTime;
                sprite.color = c;
            }
        }
    }
}
