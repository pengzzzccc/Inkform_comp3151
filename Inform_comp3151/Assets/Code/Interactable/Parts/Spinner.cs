using UnityEngine;

namespace Inkform.Interactable.Parts
{
    /// <summary>
    /// 旋转：绕自身 Z 轴匀速自转（度/秒）。碰撞体随 transform 一起转，
    /// 触发检测跟着走 —— 旋转齿轮这类危险物的基本运动。负值反转方向。
    /// 纯驱动器：不消费接触。
    /// </summary>
    public class Spinner : MonoBehaviour, IInteractablePart
    {
        [Header("Spin")]
        [Tooltip("角速度，度/秒。负值 = 反转")]
        [SerializeField] private float angularSpeed = 120f;

        private Interactable root;

        public void Attach(Interactable root) => this.root = root;

        public bool HandleContact(ContactPhase phase, Collider2D other) => false;

        void Update()
        {
            root.transform.Rotate(0f, 0f, angularSpeed * Time.deltaTime);
        }
    }
}
