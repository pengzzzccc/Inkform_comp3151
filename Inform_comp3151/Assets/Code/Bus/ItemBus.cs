using System;
using UnityEngine;

namespace Inkform.Bus
{
    /// <summary>
    /// 物品总线：物品被吃下时由物品自己发布，PlayerHandler 订阅。
    /// 物品预制体因此不再需要序列化任何场景引用，可以在运行时自由 Instantiate。
    /// </summary>
    public static class ItemBus
    {
        /// <summary>玩家吃下某个物品。参数是被吃掉的物品本身，听众可按具体子类型区分种类。</summary>
        public static event Action<ItemSuper> ItemEaten;

        /// <summary>玩家吐出叼着的物品。pos = 出生点（嘴边），velocity = 初速度。</summary>
        public static event Action<ItemSuper, Vector2, Vector2> ItemReleased;

        /// <summary>当前叼在嘴里的物品（null = 没叼东西）。
        /// 快照必须在 Invoke 之前更新：同一物理步里多个物品依次回调，
        /// 后面那个要能立刻看到前面那个已经被吃下。</summary>
        public static ItemSuper Held { get; private set; }

        public static void RaiseItemEaten(ItemSuper item)
        {
            Held = item;
            ItemEaten?.Invoke(item);
        }

        public static void RaiseItemReleased(ItemSuper item, Vector2 pos, Vector2 velocity)
        {
            if (Held == item) Held = null;      // 只有吐的确实是叼着的那个才清快照
            ItemReleased?.Invoke(item, pos, velocity);
        }

        // 静态字段不随场景重载清空；关闭 Domain Reload 时会残留上一次运行的死订阅者
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            ItemEaten = null;
            ItemReleased = null;
            Held = null;
        }
    }
}
