using UnityEngine;

namespace Inkform.Interactable
{
    /// <summary>
    /// 可叼物品接口：玩家系统（ItemCarrier / ItemBus / RopeGun）**只经手本接口**，
    /// 不认识任何具体实现 —— 这正是「玩家只需要接口存储，具体逻辑看具体物体」。
    ///
    /// 实现方：
    /// ① 独立类直接实现（Bomb 的方法签名天然匹配，只加声明）；
    /// ② 框架物挂在可接触物上、由 CarriablePart 实现 —— 注意实现必须挂在
    ///    Interactable 的**本物体**上（RopeGun 用 GetComponent 探测，挂子物体找不到）。
    ///
    /// 释放（吐出）不在这里：走 ItemBus.ItemReleased 事件自认领 —— 那条路上还有
    /// 音频听众，事件比方法调用更适合「广播给所有人」。
    /// </summary>
    public interface ICarriable
    {
        /// <summary>拉取目标位置用（MonoBehaviour 自带）。</summary>
        Transform transform { get; }

        /// <summary>绳索枪拉近后吞下。成功返回 true（失败 = 嘴满/不可吃，绳索枪松绳）。</summary>
        bool TrySwallowByRope(Transform player);

        /// <summary>正在被绳索枪拉取（爆炸物用它防止拉取途中接触误爆）。</summary>
        void MarkRopeGrappled();

        /// <summary>拉取结束。</summary>
        void ClearRopeGrappled();

        /// <summary>玩家死亡时放回世界（不点引信、不给初速，原地放）。</summary>
        void DropAt(Vector2 pos);
    }
}
