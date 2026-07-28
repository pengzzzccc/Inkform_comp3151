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

        public static void RaiseItemEaten(ItemSuper item)
        {
            ItemEaten?.Invoke(item);
        }

        public static void RaiseItemReleased(ItemSuper item, Vector2 pos, Vector2 velocity)
        {
            ItemReleased?.Invoke(item, pos, velocity);
        }

        // 静态字段不随场景重载清空；关闭 Domain Reload 时会残留上一次运行的死订阅者
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            ItemEaten = null;
            ItemReleased = null;
        }
    }
}
