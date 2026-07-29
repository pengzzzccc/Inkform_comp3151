using Inkform.Bus;
using UnityEngine;

/// <summary>
/// 玩家死亡的表现侧：本体瞬间消失、原地炸成一堆碎块，复活时再显回来。
/// 和 PlayerHandler 分开是刻意的 —— 那边只管玩法（停物理、锁输入、瞬移），
/// 这边只管「看起来是什么样」，碎成什么样全写在 Inspector 里那份 FragmentCue 上。
/// 挂在 Player 上（要拿本物体的 SpriteRenderer）。
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class PlayerDeathFx : MonoBehaviour
{
    [Header("Death burst")]
    [SerializeField] private FragmentCue deathPieces;       // 碎成什么样全写在这份资产里
    [SerializeField] private float burstForce = 14f;        // 碎块初速度，再乘 Cue 里的 forceMultiper

    private SpriteRenderer sprite;

    void Awake()
    {
        sprite = GetComponent<SpriteRenderer>();
    }

    void OnEnable()
    {
        LifeBus.Died += OnDied;
        LifeBus.Respawned += OnRespawned;
    }

    void OnDisable()
    {
        LifeBus.Died -= OnDied;
        LifeBus.Respawned -= OnRespawned;
    }

    private void OnDied(GameObject victim, Vector2 from)
    {
        if (victim != gameObject) return;

        // 用 SpriteRenderer.bounds 而不是碰撞体的，两个理由：
        // ① 碎块该照着「看得见的轮廓」切，而玩家碰撞体是 0.5×0.5 的胶囊、比图小一圈；
        // ② 碰撞体的 bounds 会随 PlayerHandler 那边关物理而失效，而两个组件谁先收到
        //    同一个事件是不保证的 —— 用渲染器的包围盒就绕开了这个先后顺序坑
        Bounds bounds = sprite.bounds;

        // 本体和碎块不能重叠显示，关渲染而不是 SetActive(false)：
        // 后者会触发 OnDisable 退订总线，就再也收不到「复活」了
        sprite.enabled = false;

        Shatter.Burst(deathPieces, bounds, from, burstForce);
    }

    private void OnRespawned(GameObject victim, Vector2 pos)
    {
        if (victim != gameObject) return;

        sprite.enabled = true;
    }

    void OnDrawGizmosSelected()
    {
        // 黄色网格 = 碎块怎么切，方便调 Cue 里的 cellsX / cellsY
        // 这里不能用 sprite 缓存：编辑器下 Awake 没跑过，缓存还是空的
        SpriteRenderer sr = GetComponent<SpriteRenderer>();
        if (sr == null || deathPieces == null) return;

        Gizmos.color = Color.yellow;
        Shatter.DrawGrid(sr.bounds, deathPieces.cellsX, deathPieces.cellsY);
    }
}
