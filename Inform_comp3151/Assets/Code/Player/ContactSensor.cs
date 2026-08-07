using Inkform.Tool;
using UnityEngine;

namespace Inkform.Player
{
    /// <summary>
    /// 四向接触检测：玩家现在贴着地面 / 左墙 / 右墙 / 天花板没有。
    /// 从 PlayerHandler 拆出来的第一层 —— 运动和动画都要读这几个标志，但谁都不该重复探一遍。
    ///
    /// 不自带 Update：探测必须严格排在运动和动画之前，而同一物体上各组件的 Update 顺序
    /// Unity 不保证。统一由 PlayerHandler 按「感知 → 运动 → 动画」的顺序调 Tick()。
    /// 挂在 Player 上。
    /// </summary>
    public class ContactSensor : MonoBehaviour
    {
        [Header("Terrain Check")]
        [SerializeField] private Transform groundCheck;
        [SerializeField] private Transform leftWallCheck;
        [SerializeField] private Transform rightWallCheck;
        [SerializeField] private Transform ceilingCheck;

        // 四向检测统一用这一层：Terrain(6) | Breakable(11)。
        // 默认值写死成预制体上的原配置，免得新加组件时忘了勾、玩家直接掉出世界
        [SerializeField] private LayerMask terrainMask = (1 << 6) | (1 << 11);
        [SerializeField] private float checkRadius = 0.1f;

        [Header("Ceiling")]
        [SerializeField] private float ceilingStickTime = 0.5f;

        // 贴顶允许时长。注意它的驱动方式是反的：**没贴顶时每帧刷新**，
        // 于是「IsRunning」在离开天花板期间恒为真，贴上之后才开始真正倒计时。
        // 重力那边正是靠这个反相语义区分「还能吸住」和「该掉下来了」
        private Timer ceilingStickTimer;

        public bool OnGround { get; private set; }
        public bool OnLeftWall { get; private set; }
        public bool OnRightWall { get; private set; }
        public bool OnCeiling { get; private set; }

        public bool OnWall => OnLeftWall || OnRightWall;

        /// <summary>脚下命中的碰撞体（层 6/11）。平台跟随靠它读脚下物体的位移。</summary>
        public Collider2D Ground { get; private set; }

        /// <summary>贴顶时间还没用完 —— 见 ceilingStickTimer 上那段反相语义的说明。</summary>
        public bool CeilingStickActive => ceilingStickTimer.IsRunning;

        /// <summary>探一次四向接触。由 PlayerHandler 在每帧最前面调。</summary>
        public void Tick()
        {
            Ground = Physics2D.OverlapCircle(groundCheck.position, checkRadius, terrainMask);
            OnGround = Ground != null;
            OnLeftWall = Physics2D.OverlapCircle(leftWallCheck.position, checkRadius, terrainMask);
            OnRightWall = Physics2D.OverlapCircle(rightWallCheck.position, checkRadius, terrainMask);
            OnCeiling = Physics2D.OverlapCircle(ceilingCheck.position, checkRadius, terrainMask);

            // 贴着墙时不算贴顶：墙角处两边会同时判定到，不排掉的话会在贴墙下滑和天花板吸附之间抖
            if (OnCeiling && OnLeftWall) OnCeiling = false;
            if (OnCeiling && OnRightWall) OnCeiling = false;

            if (!OnCeiling) ceilingStickTimer.Set(ceilingStickTime);
        }

        void OnDrawGizmosSelected()
        {
            if (groundCheck == null) return;
            Gizmos.color = OnGround ? Color.green : Color.red;
            Gizmos.DrawWireSphere(groundCheck.position, checkRadius);

            if (leftWallCheck == null) return;
            Gizmos.color = OnLeftWall ? Color.green : Color.red;
            Gizmos.DrawWireSphere(leftWallCheck.position, checkRadius);

            if (rightWallCheck == null) return;
            Gizmos.color = OnRightWall ? Color.green : Color.red;
            Gizmos.DrawWireSphere(rightWallCheck.position, checkRadius);

            if (ceilingCheck == null) return;
            Gizmos.color = OnCeiling ? Color.green : Color.red;
            Gizmos.DrawWireSphere(ceilingCheck.position, checkRadius);
        }
    }
}
