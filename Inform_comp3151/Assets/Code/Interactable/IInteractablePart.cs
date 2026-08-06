using UnityEngine;

namespace Inkform.Interactable
{
    /// <summary>
    /// 可接触物行为组件（part）：挂到 Interactable 主节点同一物体或子物体上，
    /// 由主节点 Awake 时 GetComponentsInChildren 自动收集并逐个 Attach。
    /// 每个 part 负责一类行为（接触致死 / 可叼 / 可抓…），主节点收到物理回调时
    /// 按声明顺序分发 —— 返回 true 表示「已处理」，后续 parts 不再收到这次接触。
    ///
    /// 只放当前用得到的成员（见 IDeathBody 的先例）：碰撞回调分发、每帧驱动等
    /// 扩展点等真有用到时再加 —— 现在就摆空方法只会变成死代码。
    /// </summary>
    public interface IInteractablePart
    {
        /// <summary>挂载回调：被主节点收集时调用一次，替代 GetComponentInParent。
        /// 此时主节点的 Awake 已跑完，root 上的组件引用可直接缓存。</summary>
        void Attach(Interactable root);

        /// <summary>一次玩家/物体接触。true = 已处理，短路后续 parts。</summary>
        bool HandleContact(ContactPhase phase, Collider2D other);
    }
}
