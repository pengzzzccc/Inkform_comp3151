using UnityEngine;

namespace Inkform.Item
{
    /// <summary>
    /// Base class of all pickable/eatable items: holds item data and the SpriteRenderer cache.
    /// Note: MonoBehaviours cannot use constructors (Unity instantiates them itself); data is always
    /// configured via SerializeField in the Inspector.
    /// </summary>
    public class ItemSuper : MonoBehaviour
    {
        //Item data
        [SerializeField] private bool eatAble = true;
        private bool _visible = true;
        private Sprite _current;

        //Item component
        private SpriteRenderer sprite;

        public bool EatAble => eatAble;

        protected virtual void Awake()
        {
            sprite = this.gameObject.GetComponent<SpriteRenderer>();
        }

        /// <summary>
        /// Puts the held item back into the world when the player dies. The default implementation only
        /// shows it; subclasses that need physics restored (like Bomb) must override — otherwise the
        /// item stays in the world forever with its simulation disabled.
        /// </summary>
        public virtual void DropAt(Vector2 pos)
        {
            SetVisible(true);
        }

        protected void SetVisible(bool visible)
        {
            if (_visible == visible) return;
            _visible = visible;
            if (sprite != null) sprite.enabled = visible;

        }

        // Like SetVisible, self-deduplicating: per-frame-driven animation calls in every frame, but
        // most frames are the same sprite
        protected void SetSprite(Sprite s)
        {
            if (_current == s) return;
            _current = s;
            if (sprite != null) sprite.sprite = s;
        }
    }
}
