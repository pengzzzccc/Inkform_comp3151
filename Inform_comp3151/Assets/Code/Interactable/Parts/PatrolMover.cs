using UnityEngine;

namespace Inkform.Interactable.Parts
{
    /// <summary>
    /// 往返平移：在「初始位置 + pointA」与「初始位置 + pointB」之间来回走。
    /// 两个端点是本地偏移、以 Attach 时的世界位置为基准 —— 物体摆在哪都能原地起算，
    /// 整组挪动不用重配端点。配合 Spinner 就是「左右横移的旋转齿轮」。
    /// 纯驱动器：不消费接触。
    /// </summary>
    public class PatrolMover : MonoBehaviour, IInteractablePart
    {
        [Header("Patrol")]
        [Tooltip("左端点（相对初始位置的本地偏移）")]
        [SerializeField] private Vector2 pointA = new Vector2(-2.5f, 0f);
        [Tooltip("右端点（相对初始位置的本地偏移）")]
        [SerializeField] private Vector2 pointB = new Vector2(2.5f, 0f);
        [Tooltip("移动速度，单位/秒")]
        [SerializeField] private float speed = 2.2f;

        private Interactable root;
        private Vector2 startPos;   // Attach 时的世界位置，巡逻基准
        private Vector2 target;     // 当前目标端点
        private bool goingToB = true;

        public void Attach(Interactable root)
        {
            this.root = root;
            startPos = root.transform.position;
            target = startPos + pointB;
        }

        public bool HandleContact(ContactPhase phase, Collider2D other) => false;

        void Update()
        {
            Vector2 from = root.transform.position;
            Vector2 to = target - from;
            float dist = to.magnitude;
            float step = speed * Time.deltaTime;
            if (step <= 0f) return;

            if (dist <= step)
            {
                // 到达端点：位置精确落在端点上（避免逐帧逼近的累计误差），换另一头
                root.transform.position = target;
                goingToB = !goingToB;
                target = startPos + (goingToB ? pointB : pointA);
            }
            else
            {
                root.transform.position = from + to / dist * step;
            }
        }

        // 编辑器下 Attach 没跑过，用当前 transform.position 当基准画示意
        void OnDrawGizmosSelected()
        {
            Vector3 basePos = transform.position;
            Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.9f);
            Gizmos.DrawLine(basePos + (Vector3)pointA, basePos + (Vector3)pointB);
            Gizmos.DrawWireSphere(basePos + (Vector3)pointA, 0.15f);
            Gizmos.DrawWireSphere(basePos + (Vector3)pointB, 0.15f);
        }
    }
}
