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
        /// <summary>玩家吃下某个物品。参数是被吃掉的物品本身，听众可按 ItemID 区分种类。</summary>
        public static event Action<ItemSuper> ItemEaten;

        public static void RaiseItemEaten(ItemSuper item)
        {
            ItemEaten?.Invoke(item);
        }

        // 静态字段不随场景重载清空；关闭 Domain Reload 时会残留上一次运行的死订阅者
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            ItemEaten = null;
        }
    }
}
