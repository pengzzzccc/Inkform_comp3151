using Inkform.Bus;
using Inkform.Life;
using Inkform.Tool;
using UnityEngine;

namespace Inkform.Interactable.Parts
{
    /// <summary>
    /// 危险物行为：接触玩家即死（不分方向）。所有「碰到就死」的关卡物件共用本组件 ——
    /// 尖刺直接挂它，以后定时激光柱 / 旋转齿轮 / 定时尖刺只需再组合显隐 / 移动类 part。
    /// 只要求主节点（Interactable）上有勾了 Is Trigger 的碰撞体，
    /// 所以手摆的单块刺和整张画满刺的 Tilemap（TilemapCollider2D）用的是同一套。
    /// 本类只发「有人被刺死了」这个事实，碎块/震屏/音效由 DeathDirector 选出的 DeathStrategy 统一演。
    /// </summary>
    public class HarmOnTouch : MonoBehaviour, IInteractablePart
    {
        [Header("Harm")]
        [Tooltip("死因：DeathDirector 按它选演出策略（尖刺默认 Spike）")]
        [SerializeField] private DeathCause cause = DeathCause.Spike;
        [Tooltip("只伤谁：CompareTag 传错字符串不会报错、只会永远不匹配，用 Tags 常量")]
        [SerializeField] private string targetTag = Tags.Player;

        private Collider2D hitBox;      // 主节点上的碰撞体：算致死点用

        public void Attach(Interactable root)
        {
            // RequireComponent 保证碰撞体在主节点上；拿不到说明装配错误，吭一声
            hitBox = root.GetComponent<Collider2D>();
            if (hitBox == null)
                Debug.LogWarning($"{root.name} 的 Interactable 上没有 Collider2D，危险物不会生效", root);
        }

        public bool HandleContact(ContactPhase phase, Collider2D other)
        {
            // Enter / Stay 都要接：玩家卡在刺里不动时 Enter 不会重发，复活点万一贴着刺也一样
            if (phase == ContactPhase.Exit) return false;
            if (hitBox == null) return false;
            if (!other.CompareTag(targetTag)) return false;

            // 致死点取碰撞体上离玩家最近的那个点，不能用 transform.position ——
            // 一整排刺画在同一张 Tilemap 上时那是网格原点，碎块会齐刷刷朝几十格外飞
            LifeBus.RaiseDied(new DeathContext(
                other.gameObject,
                hitBox.ClosestPoint(other.bounds.center),
                cause));

            return true;    // 已处理：短路后续 parts（只有玩家会死）
        }
    }
}
