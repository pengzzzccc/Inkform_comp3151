using Inkform.Bus;
using Inkform.Tool;
using UnityEngine;

namespace Inkform.Level
{
    /// <summary>
    /// 检查点：玩家碰到就把复活点记到这里。勾上 isStartPoint 的那个兼任关卡出生点 ——
    /// 开局由 RespawnDirector 直接把玩家放过去，于是「出生点」和「检查点」是同一种物件。
    /// 需要一个勾了 Is Trigger 的碰撞体。
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class Checkpoint : MonoBehaviour
    {
        [Header("Checkpoint setting")]
        [SerializeField] private bool isStartPoint = false;                     // 勾上 = 兼任关卡出生点，一关只该勾一个
        [SerializeField] private Vector2 spawnOffset = new Vector2(0f, 0.5f);   // 抬高一点，免得复活时脚陷进地里被顶出来

        private bool active;    // 反复穿过同一个点不重复发反馈

        public bool IsStartPoint => isStartPoint;
        public Vector2 SpawnPos => (Vector2)transform.position + spawnOffset;

        void OnTriggerEnter2D(Collider2D other)
        {
            if (active) return;
            if (!other.CompareTag(Tags.Player)) return;

            active = true;
            LifeBus.RaiseCheckpointSet(SpawnPos);
        }

        // 用 OnDrawGizmos 而不是 ...Selected：摆关卡时要能一眼扫出复活点都在哪、
        // 有没有漏勾出生点，逐个点开检查太慢
        void OnDrawGizmos()
        {
            bool touched = Application.isPlaying && active;
            Color c = isStartPoint ? new Color(0.35f, 1f, 0.45f) : new Color(0.35f, 0.85f, 1f);

            // 触发范围：玩家得走进这个盒子才算踩到。直接读碰撞体，
            // 免得 Gizmo 和实际判定范围各画各的、调了一个忘了另一个
            Collider2D box = GetComponent<Collider2D>();
            if (box != null && box.bounds.size.sqrMagnitude > 0f)
            {
                Gizmos.color = touched ? c : c * 0.55f;
                Gizmos.DrawWireCube(box.bounds.center, box.bounds.size);
            }

            // 真正的复活坐标。连一根线过去，spawnOffset 抬了多高一眼可见 ——
            // 这个点要是埋在地里，复活瞬间玩家会被物理顶出来
            Gizmos.color = c;
            Gizmos.DrawLine(transform.position, SpawnPos);

            if (touched) Gizmos.DrawSphere(SpawnPos, 0.14f);        // 实心 = 本次运行已经踩过
            else Gizmos.DrawWireSphere(SpawnPos, 0.14f);

            // 出生点再加个十字，和普通检查点区分开（一关只该勾一个）
            if (!isStartPoint) return;
            Gizmos.DrawLine(SpawnPos + Vector2.left * 0.3f, SpawnPos + Vector2.right * 0.3f);
            Gizmos.DrawLine(SpawnPos + Vector2.down * 0.3f, SpawnPos + Vector2.up * 0.3f);
        }
    }
}
