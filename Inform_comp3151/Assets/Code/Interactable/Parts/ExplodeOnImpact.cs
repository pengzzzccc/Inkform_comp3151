using UnityEngine;

namespace Inkform.Interactable.Parts
{
    /// <summary>
    /// 撞击引爆触发：速度达到阈值后与任何物体碰撞即炸。配合 ExplodePart 使用 ——
    /// 速度爆炸（Bomb.CheckSpeedExplode）的框架版：不限制目标标签，撞墙撞地撞玩家都算，
    /// 也不吃任何免疫期。
    /// 被吞时刚体模拟已关、碰撞体已关 → 无接触回调，天然不会触发，无需防御检查。
    /// </summary>
    public class ExplodeOnImpact : MonoBehaviour, IInteractablePart
    {
        [Header("Impact")]
        [Tooltip("速度达到该阈值后，与任何物体碰撞都会爆炸；<= 0 关闭")]
        [SerializeField] private float threshold = 0f;

        private Interactable root;
        private ExplodePart explode;
        private Rigidbody2D body;

        public void Attach(Interactable root)
        {
            this.root = root;
            body = root.GetComponent<Rigidbody2D>();
            if (body == null)
                Debug.LogWarning($"{root.name} 挂了 ExplodeOnImpact 但没挂 Rigidbody2D，速度判定不会生效", root);
        }

        // 依赖解析放 Start：Interactable.Awake 边收集边调 Attach，此刻 TryGetPart 可能
        // 还没轮到核心；Start 在所有 Awake 之后，保证爆炸核心已入列表
        void Start()
        {
            if (!root.TryGetPart(out explode))
                Debug.LogWarning($"{root.name} 挂了 ExplodeOnImpact 但没挂 ExplodePart，撞击不会爆炸", root);
        }

        public bool HandleContact(ContactPhase phase, Collider2D other)
        {
            if (phase == ContactPhase.Exit) return false;
            if (threshold <= 0f || explode == null) return false;
            if (body.linearVelocity.magnitude < threshold) return false;

            explode.Explode();
            return true;    // 已处理：短路后续 parts
        }
    }
}
