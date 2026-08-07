using UnityEngine;

namespace Inkform.Interactable.Parts
{
    /// <summary>
    /// 可站立表面：普通「可以行走、可以跳跃、可以触碰」的实体物（平台/箱子/食物箱）。
    /// 物理站立由「层 + 非触发器碰撞体 + 刚体」天然完成，本组件只做两件事：
    /// ① 语义标记 —— 让摆关卡的人一眼知道这东西是能站的；
    /// ② 配置校验 —— Attach 时当场检查层和碰撞体类型，装配错了立刻警告。
    ///
    /// 层约束：必须是 Terrain(6) 或 Breakable(11) —— 玩家的四向接触检测
    /// （ContactSensor.terrainMask）和绳索枪地形命中（RopeGun.hitMask）都只认这两层，
    /// 放别的层玩家站不住、钩子直接穿过去。
    ///
    /// 移动平台：配 PatrolMover + Rigidbody2D（Kinematic）后，站上面的玩家会自动
    /// 跟随平台移动（PlayerMotor 按脚下刚体位移做 delta 跟随，本组件无需任何额外逻辑）。
    /// </summary>
    public class SolidSurface : MonoBehaviour, IInteractablePart
    {
        // 与 ContactSensor.terrainMask / RopeGun.hitMask 保持一致，改动必须三处同步
        private const int LayerTerrain = 6;
        private const int LayerBreakable = 11;

        public void Attach(Interactable root)
        {
            int layer = root.gameObject.layer;
            if (layer != LayerTerrain && layer != LayerBreakable)
            {
                Debug.LogWarning($"{root.name} 挂了 SolidSurface 但层是 {layer}（{LayerMask.LayerToName(layer)}）：" + "必须是 Terrain(6) 或 Breakable(11)，否则玩家站不住、绳索枪钩子会穿过去", root);
            }

            Collider2D col = root.GetComponent<Collider2D>();
            if (col == null)
            {
                Debug.LogWarning($"{root.name} 挂了 SolidSurface 但没有 Collider2D", root);
            }
            else if (col.isTrigger)
            {
                Debug.LogWarning($"{root.name} 的碰撞体勾了 IsTrigger：玩家会直接穿过去，站不住。" + "SolidSurface 需要非触发器碰撞体", root);
            }
        }

        // 纯标记：不消费接触
        public bool HandleContact(ContactPhase phase, Collider2D other) => false;
    }
}
