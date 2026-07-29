using UnityEngine;

/// <summary>
/// 碎块：外观、尺寸、初速度全部由 Shatter 在生成时按 FragmentCue 写入，
/// 存活 lifeTime 后自毁，最后 fadeTime 秒淡出，免得碎块凭空消失。
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(SpriteRenderer))]
public class Fragment : MonoBehaviour
{
    [Header("Fragment setting")]
    // 不走 Cue 直接扔进场景时的兜底值；Shatter 生成的碎块会在 Apply 里被 Cue 覆盖
    [SerializeField] private float lifeTime = 3f;
    [SerializeField] private float fadeTime = 1f;   // 生命末尾的淡出时长

    private SpriteRenderer sprite;
    private BoxCollider2D box;      // 可能没有，允许为 null
    private Timer LifeTimer;
    private float baseAlpha = 1f;   // Cue 里 tint 自带的透明度，淡出以它为上限

    void Awake()
    {
        sprite = GetComponent<SpriteRenderer>();
        TryGetComponent(out box);
        LifeTimer.Set(lifeTime);
    }

    /// <summary>
    /// 由 Shatter 在 Instantiate 之后立刻调用：随机换一张外观，等比缩放到塞进格子。
    /// 必须排在 Awake 之后 —— sprite 缓存和寿命计时器都是在那里初始化的。
    /// </summary>
    public void Apply(FragmentCue cue, Vector2 cell)
    {
        lifeTime = cue.lifeTime;
        fadeTime = cue.fadeTime;
        LifeTimer.Set(lifeTime);            // Awake 里已按预制体上的旧值起过一次，这里按 Cue 重来

        Sprite pick = cue.PickSprite();
        if (pick != null) sprite.sprite = pick;     // Cue 里一张图都没配时，保留预制体自带的
        sprite.color = cue.tint;
        baseAlpha = cue.tint.a;

        if (cue.randomFlip)
        {
            sprite.flipX = Random.value < 0.5f;
            sprite.flipY = Random.value < 0.5f;
        }

        // 等比缩放，且基准是「图集里最大那张」而不是本块自己 ——
        // 逐轴拉伸会把手绘轮廓压变形，按本块自己算又会把小碎片放大到和大块一样
        Vector2 basis = cue.MaxSpriteSize;
        float s = Mathf.Min(cell.x / basis.x, cell.y / basis.y) * cue.PickScale();
        transform.localScale = new Vector3(s, s, 1f);

        // 碰撞体按图的实际外框走：预制体上那个 1×1 的方盒配不上不规则碎片，
        // 不改的话碎块会悬在地面上方，还会互相用大得多的隐形盒子顶开
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
