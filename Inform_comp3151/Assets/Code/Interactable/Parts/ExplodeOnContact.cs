using Inkform.Tool;
using UnityEngine;

namespace Inkform.Interactable.Parts
{
    /// <summary>
    /// 触碰爆炸触发：碰到目标（默认玩家）就炸。配合 ExplodePart 使用 ——
    /// 触发条件与爆炸本身分离，想换触发方式（定时 / 连锁）只换 part 不碰核心。
    /// 触发器碰撞体和物理碰撞体都能触发：主节点把两者统一分发进 HandleContact。
    /// </summary>
    public class ExplodeOnContact : MonoBehaviour, IInteractablePart
    {
        [Header("Trigger")]
        [Tooltip("碰谁炸：CompareTag 传错字符串不会报错、只会永远不匹配，用 Tags 常量")]
        [SerializeField] private string targetTag = Tags.Player;

        private Interactable root;
        private ExplodePart explode;

        public void Attach(Interactable root) => this.root = root;

        // 依赖解析放 Start：Interactable.Awake 边收集边调 Attach，此刻 TryGetPart 可能
        // 还没轮到核心；Start 在所有 Awake 之后，保证爆炸核心已入列表
        void Start()
        {
            if (!root.TryGetPart(out explode))
                Debug.LogWarning($"{root.name} 挂了 ExplodeOnContact 但没挂 ExplodePart，触碰不会爆炸", root);
        }

        public bool HandleContact(ContactPhase phase, Collider2D other)
        {
            if (phase == ContactPhase.Exit) return false;
            if (explode == null) return false;
            if (!other.CompareTag(targetTag)) return false;

            explode.Explode();
            return true;    // 已处理：短路后续 parts
        }
    }
}
