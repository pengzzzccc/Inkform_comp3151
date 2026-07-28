using UnityEngine;

/// <summary>
/// 所有可拾取/可吃物品的基类：持有物品数据和 SpriteRenderer 缓存。
/// 注意：MonoBehaviour 不能用构造函数（Unity 自己负责实例化），数据一律用 SerializeField 在 Inspector 里配。
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

    protected void SetVisible(bool visible)
    {
        if (_visible == visible) return;
        _visible = visible;
        if (sprite != null) sprite.enabled = visible;

    }

    // 和 SetVisible 一样自带去重：逐帧驱动的动画每帧都会调进来，但大多数帧是同一张图
    protected void SetSprite(Sprite s)
    {
        if (_current == s) return;
        _current = s;
        if (sprite != null) sprite.sprite = s;
    }
}
