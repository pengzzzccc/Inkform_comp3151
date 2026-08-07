using System.Collections.Generic;
using UnityEngine;

namespace Inkform.Interactable
{
    /// <summary>
    /// 可接触物主节点（组件模式交互框架的核心）：场景里所有与玩家交互的物件
    /// （危险物 / 普通表面 / 炸弹…）都挂一个本组件，再按需组合行为组件（parts）。
    ///
    /// 分工：
    /// ① 主节点只做两件事 —— 统一收物理回调（OnTrigger*/OnCollision*）并按声明顺序分发给 parts
    ///    （遇「已处理」即短路）；对外暴露 parts 查询（TryGetPart），玩家和系统
    ///    只经手本节点，不认识任何具体行为；
    /// ② parts 挂在本物体或子物体上（GetComponentsInChildren 自动收集，挂上即生效，
    ///    不用 Inspector 手动拖），具体行为（接触致死 / 爆炸 / 可叼 / 可抓…）由各 part
    ///    自己实现 —— 「玩家只需要接口存储，具体逻辑看具体物体」。
    ///
    /// 触发器与物理碰撞统一分发：静态物（尖刺/激光，触发器碰撞体、无刚体）走 OnTrigger*，
    /// 实体物（炸弹/地雷，非触发器 + Rigidbody2D）走 OnCollision* —— parts 不必区分来源，
    /// 两者最终都汇进 HandleContact(phase, other)。碰撞回调要求本物体上有 Rigidbody2D。
    ///
    /// 层建议：危险物类放在 Hazard(13) 层 —— 工程里 Physics2D.QueriesHitTriggers = 1，
    /// 放 Terrain/Breakable 的话玩家的四向 OverlapCircle 会把刺当成能站的地面。
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class Interactable : MonoBehaviour
    {
        private readonly List<IInteractablePart> parts = new List<IInteractablePart>();

        /// <summary>查询行为组件：玩家/系统侧的统一入口（如 ItemCarrier 查可叼 part、
        /// RopeGun 查可抓 part）。查不到返回 false，调用方按「没有这个行为」降级。</summary>
        public bool TryGetPart<T>(out T part) where T : class, IInteractablePart
        {
            foreach (IInteractablePart p in parts)
            {
                if (p is T t)
                {
                    part = t;
                    return true;
                }
            }
            part = null;
            return false;
        }

        void Awake()
        {
            // 收集 parts 必须在任何物理回调之前：物理回调在 Awake 之后才可能到达
            parts.Clear();
            foreach (IInteractablePart p in GetComponentsInChildren<IInteractablePart>(true))
            {
                parts.Add(p);
                p.Attach(this);     // 挂载回调：此刻本节点 Awake 已跑完，可直接缓存引用
            }
        }

        // ---- 物理回调统一入口：分发给所有 parts，遇「已处理」即短路 ----
        // 触发器与碰撞共用同一套分发：静态物（无刚体）只收到 OnTrigger*，
        // 实体物（有 Rigidbody2D）收到 OnCollision*，parts 无需区分来源

        void OnTriggerEnter2D(Collider2D other) => Dispatch(ContactPhase.Enter, other);
        void OnTriggerStay2D(Collider2D other) => Dispatch(ContactPhase.Stay, other);
        void OnTriggerExit2D(Collider2D other) => Dispatch(ContactPhase.Exit, other);

        void OnCollisionEnter2D(Collision2D collision) => Dispatch(ContactPhase.Enter, collision.collider);
        void OnCollisionStay2D(Collision2D collision) => Dispatch(ContactPhase.Stay, collision.collider);
        void OnCollisionExit2D(Collision2D collision) => Dispatch(ContactPhase.Exit, collision.collider);

        private void Dispatch(ContactPhase phase, Collider2D other)
        {
            foreach (IInteractablePart p in parts)
            {
                if (p.HandleContact(phase, other)) return;
            }
        }

        // 在 Scene 视图画出主节点范围 + 已挂 parts 一览，摆关卡时一眼看出组合
        void OnDrawGizmosSelected()
        {
            Collider2D c = GetComponent<Collider2D>();
            if (c == null) return;

            Gizmos.color = new Color(0.9f, 0.3f, 0.9f, 0.5f);
            Gizmos.DrawWireCube(c.bounds.center, c.bounds.size);

            // 每个行为组件画个黄点：这个可接触物挂了哪几层行为，一眼可见
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.9f);
            foreach (IInteractablePart p in GetComponentsInChildren<IInteractablePart>(true))
            {
                Transform t = (p as MonoBehaviour)?.transform;
                if (t == null) continue;
                Gizmos.DrawWireSphere(t.position, 0.12f);
            }
        }
    }
}
