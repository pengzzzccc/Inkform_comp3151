using Inkform.Bus;
using UnityEngine;

/// <summary>
/// 尖刺：玩家碰到即死，不分方向。只要求本物体上有个勾了 Is Trigger 的碰撞体，
/// 所以手摆的单块刺和整张画满刺的 Tilemap（TilemapCollider2D）用的是同一个脚本。
/// 本类只发「有人死了」这个事实，碎块/震屏/音效分别由 PlayerDeathFx、FxDirector、AudioDirector 翻译。
/// 注意必须放在 Hazard 层：工程里 Physics2D.QueriesHitTriggers = 1，
/// 放 Terrain/Breakable 的话玩家的四向 OverlapCircle 会把刺当成能站的地面。
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class Spike : MonoBehaviour
{
    private Collider2D hitBox;

    void Awake()
    {
        hitBox = GetComponent<Collider2D>();
    }

    // Stay 也要接：玩家卡在刺里不动时 Enter 不会重发，复活点万一贴着刺也一样
    void OnTriggerEnter2D(Collider2D other) => Kill(other);
    void OnTriggerStay2D(Collider2D other) => Kill(other);

    private void Kill(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;

        // 致死点取碰撞体上离玩家最近的那个点，不能用 transform.position ——
        // 一整排刺画在同一张 Tilemap 上时那是网格原点，碎块会齐刷刷朝几十格外飞
        LifeBus.RaiseDied(other.gameObject, hitBox.ClosestPoint(other.bounds.center));
    }
}
