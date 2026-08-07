using UnityEngine;

namespace Inkform.Interactable.Parts
{
    /// <summary>
    /// 悬挂锚点标记：挂在「挂载了 HangingChain 的物体」的子物体上，标识一个链锚点位置。
    /// 纯标记组件（Bomb.HangingPoint 的框架版）—— 无逻辑，HangingChain 在 Attach 时
    /// GetComponentsInChildren 自动收集，所以锚点**必须是该物体的子节点**。
    /// 无碰撞体、无刚体，只有 Scene 视图里的图标方便定位。
    /// </summary>
    public class HangAnchor : MonoBehaviour
    {
        [Tooltip("Scene 视图里锚点图标的大小，纯编辑辅助")]
        [SerializeField] private float gizmoSize = 0.25f;

        void OnDrawGizmos()
        {
            Vector3 pos = transform.position;
            Gizmos.color = new Color(0.2f, 0.7f, 1f, 0.9f);

            float s = gizmoSize;
            Gizmos.DrawLine(pos + Vector3.left * s, pos + Vector3.right * s);
            Gizmos.DrawLine(pos + Vector3.up * s, pos + Vector3.down * s);
            Gizmos.DrawWireSphere(pos, s * 0.6f);
        }
    }
}
