using UnityEngine;

namespace Inkform.Item
{
    /// <summary>
    /// 悬挂点：炸弹悬挂模式下链条的世界固定锚点。
    /// 由 Bomb 的 [ContextMenu] 在编辑模式生成、作为炸弹子物体摆在场景里任意拖动；
    /// 运行时 Bomb 在 Awake 取它的世界坐标作为链条锚点（固定，不随炸弹移动）。
    /// 纯标记组件：无碰撞体、无刚体，只有 Scene 视图里的图标方便定位。
    /// </summary>
    public class HangingPoint : MonoBehaviour
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
