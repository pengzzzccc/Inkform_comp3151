using UnityEngine;

/// <summary>
/// 一种碎裂表现的配置：碎块预制体 + 多张随机外观 + 切分/受力/寿命参数，纯数据资产。
/// 在 Assets > Create > Fx > Fragment Cue 创建，由 Bomb / BreakAbleWall 在 Inspector 里引用。
/// 生成动作本身由 Shatter 负责，本类只描述「该碎成什么样」。
/// </summary>
[CreateAssetMenu(menuName = "Fx/Fragment Cue")]
public class FragmentCue : ScriptableObject
{
    [Header("Look")]
    [Tooltip("碎块预制体，需带 Rigidbody2D + SpriteRenderer + Fragment")]
    public GameObject prefab;
    [Tooltip("多张外观，每块碎片随机取一张防重样。留空槽位会被跳过，整个数组为空则沿用预制体自带的图")]
    public Sprite[] sprites;
    [Tooltip("在「等比塞进格子」的基础上再乘一次随机系数，让同一次碎裂的块大小不齐")]
    public Vector2 scaleRange = new Vector2(0.85f, 1.1f);
    [Tooltip("随机水平/垂直翻转，进一步打散重复感")]
    public bool randomFlip = true;
    public Color tint = Color.white;

    [Header("Shatter")]
    public int cellsX = 2;                                  // 横向切几块
    public int cellsY = 3;                                  // 纵向切几块
    [Tooltip("碎块速度 = 爆炸推力 × 本系数")]
    [Range(0f, 2f)] public float forceMultiper = 0.6f;
    [Tooltip("碎块随机自转的角速度上限")]
    public float spinSpeed = 180f;

    [Header("Life")]
    public float lifeTime = 3f;
    public float fadeTime = 1f;                             // 生命末尾的淡出时长

    // 缩放基准的缓存。ScriptableObject 是资产，实例常驻编辑器内存，所以这份缓存
    // 不随退出播放模式清零 —— 靠下面的 OnValidate 在 sprites 被改动时作废。
    [System.NonSerialized] private Vector2 maxSpriteSize;
    [System.NonSerialized] private bool measured;

    /// <summary>随机取一张外观。数组里的空槽位会被跳过，全空则返回 null（调用方保留预制体原图）。</summary>
    public Sprite PickSprite()
    {
        if (sprites == null || sprites.Length == 0) return null;

        // 不能直接 sprites[Random.Range(...)]：Inspector 里留空槽位很常见，
        // 取到 null 之后 SpriteRenderer 整块不可见，碎块就凭空少了一片
        int start = Random.Range(0, sprites.Length);
        for (int i = 0; i < sprites.Length; i++)
        {
            Sprite s = sprites[(start + i) % sprites.Length];
            if (s != null) return s;
        }
        return null;
    }

    public float PickScale() =>
        Random.Range(scaleRange.x, scaleRange.y);

    /// <summary>
    /// 图集里最大那张切片的世界尺寸，碎块缩放以它为基准：
    /// 最大的块刚好塞满格子，小块按美术画的比例保持小。
    /// 若改成每块各自塞满格子，6×7 的小碎片会被放大到和 27×20 的一样大，像素明显变糊。
    /// </summary>
    public Vector2 MaxSpriteSize
    {
        get
        {
            if (measured) return maxSpriteSize;
            measured = true;

            // 种子必须是 zero 而不是 one：切片全都小于 1 单位时，
            // 从 one 起累积的最大值会永远卡在 1，碎块被整体缩小
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

    // 在 Inspector 里改完 sprites 后基准必须重算，否则新图还按旧尺寸缩放
    void OnValidate() => measured = false;
}
