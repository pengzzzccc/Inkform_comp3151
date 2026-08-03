using Inkform.Bus;
using Inkform.Tool;
using UnityEngine;

namespace Inkform.Level
{
    /// <summary>
    /// 绳索枪射程覆盖区域：玩家进入时通过 RopeGunBus 把绳索枪的射程改成区域指定值，
    /// 离开时恢复默认。需要在本物体上放一个勾了 Is Trigger 的碰撞体（建议盖住整个特殊区域）。
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class RopeRangeZone : MonoBehaviour
    {
        [SerializeField] private float maxRange = 4.2f;    // 区域内绳索枪的最大射程

        void OnTriggerEnter2D(Collider2D other)
        {
            if (!other.CompareTag(Tags.Player)) return;
            RopeGunBus.RaiseRangeOverride(maxRange);
        }

        void OnTriggerExit2D(Collider2D other)
        {
            if (!other.CompareTag(Tags.Player)) return;
            RopeGunBus.RaiseRangeRestored();
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.9f, 0.2f, 0.4f);
            Gizmos.DrawWireCube(transform.position, transform.localScale);
        }
    }
}
