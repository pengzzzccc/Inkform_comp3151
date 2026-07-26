using UnityEngine;

/// <summary>
/// 所有可拾取/可吃物品的基类：持有物品数据和 SpriteRenderer 缓存。
/// 注意：MonoBehaviour 不能用构造函数（Unity 自己负责实例化），数据一律用 SerializeField 在 Inspector 里配。
/// </summary>
public class ItemSuper : MonoBehaviour
{
    //Item data
    [SerializeField] private string itemID;
    [SerializeField] private int lifeCount;
    [SerializeField] private bool eatAble = true;
    private bool _visible = true;

    //Item component
    private SpriteRenderer sprite;

    public string ItemID => itemID;
    public int LifeCount => lifeCount;
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
}
