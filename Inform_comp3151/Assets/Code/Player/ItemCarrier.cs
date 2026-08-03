using Inkform.Bus;
using Inkform.Item;
using Inkform.Life;
using UnityEngine;

namespace Inkform.Player
{
    /// <summary>
    /// 叼在嘴里的东西：记住当前持有的物品，攻击时把它吐出去。
    /// 从 PlayerHandler 拆出来的第四层 —— 吞下这一步不在这里，
    /// 那是物品自己判定的（见 Bomb.Swallow），本类只等 ItemBus 通知「你吃到了」。
    /// 玩家死亡时把叼着的东西放回世界，复活后嘴是空的。
    ///
    /// 挂在 Player 上。
    /// </summary>
    public class ItemCarrier : MonoBehaviour
    {
        [Header("Spit")]
        // 8 向吐出（Q / 右扳机）：方向来自移动输入，吐出点与初速都沿该方向。
        // spitOffset 必须大于「玩家碰撞体半宽 + 物品半径」，否则出生就重叠、会被物理弹开
        [SerializeField] private float spitOffset = 0.9f;
        // 必须大于冲刺速度，否则吐出去就被自己追上、免疫期一过原地自爆
        [SerializeField] private float spitSpeed = 24f;

        // 叼在嘴里的物品（null = 没叼东西）。不对外暴露查询接口 ——
        // 「玩家嘴里有没有东西」ItemBus.Held 已经存了一份全局快照，Bomb 用的就是那个
        private ItemSuper heldItem;

        /// <summary>嘴里有没有东西。Q/右扳机吐炸弹时用。</summary>
        public bool IsEmpty => heldItem == null;

        void OnEnable()
        {
            ItemBus.ItemEaten += OnItemEaten;
            LifeBus.Died += OnDied;
        }

        void OnDisable()
        {
            ItemBus.ItemEaten -= OnItemEaten;
            LifeBus.Died -= OnDied;
        }

        /// <summary>
        /// 朝 dir 方向吐出叼着的物品（8 向）。返回是否真的吐了 ——
        /// 调用方据此决定播 Release 还是忽略。
        /// </summary>
        public bool TryRelease(Vector2 dir)
        {
            if (heldItem == null) return false;

            Vector2 mouth = (Vector2)transform.position + dir * spitOffset;
            ItemBus.RaiseItemReleased(heldItem, mouth, dir * spitSpeed);
            heldItem = null;
            return true;
        }

        // 由 ItemBus 在物品被吃下时回调：只有真实吃到才进入叼着物品状态
        private void OnItemEaten(ItemSuper item)
        {
            heldItem = item;
        }

        // 由 LifeBus 在自己死掉时回调：把叼着的东西放回世界。
        // 不清理的话复活后会带着一颗隐形、无物理、永不引信的炸弹卡死在 Held 相
        //（吐出去是走 ItemReleased 的，那条路会点引信，死亡掉落不该点，所以单独处理）
        private void OnDied(DeathContext ctx)
        {
            if (ctx.Victim != gameObject) return;
            if (heldItem == null) return;

            heldItem.DropAt(transform.position);
            heldItem = null;
            ItemBus.ClearHeld();
        }
    }
}
