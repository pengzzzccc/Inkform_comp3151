using UnityEngine;

/// <summary>
/// 墙体碎块：速度由 BreakAbleWall 在生成时写入，存活 lifeTime 后自毁，
/// 最后 fadeTime 秒淡出，免得碎块凭空消失。
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(SpriteRenderer))]
public class Fragment : MonoBehaviour
{
    [Header("Fragment setting")]
    [SerializeField] private float lifeTime = 3f;
    [SerializeField] private float fadeTime = 1f;   // 生命末尾的淡出时长

    private SpriteRenderer sprite;
    private Timer LifeTimer;

    void Awake()
    {
        sprite = GetComponent<SpriteRenderer>();
        LifeTimer.Set(lifeTime);
    }

    void Update()
    {
        if (!LifeTimer.IsRunning) { Destroy(gameObject); return; }

        if (fadeTime > 0f && LifeTimer.Remaining < fadeTime)
        {
            Color c = sprite.color;
            c.a = LifeTimer.Remaining / fadeTime;
            sprite.color = c;
        }
    }
}
