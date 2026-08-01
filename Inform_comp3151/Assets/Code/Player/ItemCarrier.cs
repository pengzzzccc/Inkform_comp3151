using Inkform.Bus;
using Inkform.Item;
using UnityEngine;

namespace Inkform.Player
{
    /// <summary>
    /// 叼在嘴里的东西：记住当前持有的物品，攻击时把它吐出去。
    /// 从 PlayerHandler 拆出来的第四层 —— 吞下这一步不在这里，
    /// 那是物品自己判定的（见 Bomb.Swallow），本类只等 ItemBus 通知「你吃到了」。
    ///
    /// 挂在 Player 上。
    /// </summary>
    public class ItemCarrier : MonoBehaviour
    {
        [Header("Spit")]
        // x 都会按朝向取反；spitOffset.x 必须大于「玩家碰撞体半宽 + 物品半径」，否则出生就重叠、会被物理弹开
        [SerializeField] private Vector2 spitOffset = new Vector2(0.9f, 0.15f);
        // spitSpeed.x 必须大于冲刺速度（movingSpeed * attackMultiper），否则吐出去就被自己追上、免疫期一过原地自爆
        [SerializeField] private Vector2 spitSpeed = new Vector2(24f, 6f);

        // 叼在嘴里的物品（null = 没叼东西）。不对外暴露查询接口 ——
        // 「玩家嘴里有没有东西」ItemBus.Held 已经存了一份全局快照，Bomb 用的就是那个
        private ItemSuper heldItem;

        void OnEnable()
        {
            ItemBus.ItemEaten += OnItemEaten;
        }

        void OnDisable()
        {
            ItemBus.ItemEaten -= OnItemEaten;
        }

        /// <summary>
        /// 朝 dir 方向吐出叼着的物品。返回是否真的吐了 ——
        /// 调用方据此决定播 Release 还是 Eat 动画。
        /// </summary>
        public bool TryRelease(float dir)
        {
            if (heldItem == null) return false;

            Vector2 mouth = (Vector2)transform.position + new Vector2(dir * spitOffset.x, spitOffset.y);
            ItemBus.RaiseItemReleased(heldItem, mouth, new Vector2(dir * spitSpeed.x, spitSpeed.y));
            heldItem = null;
            return true;
        }

        // 由 ItemBus 在物品被吃下时回调：只有真实吃到才进入叼着物品状态
        private void OnItemEaten(ItemSuper item)
        {
            heldItem = item;
        }
    }
}
