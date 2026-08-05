using Inkform.Tool;
using UnityEngine;

namespace Inkform.Item
{
    /// <summary>
    /// 合页墙：一端绕世界固定的合页轴旋转，另一端被一条可配置的锁链挂住。
    ///
    /// 物理结构全部在 Awake 运行时组装，场景里只需摆好本体和锚点：
    /// ① HingeJoint2D —— 合页轴 = 墙上的 hingePivot 局部位置，connectedBody = null
    ///    （世界固定轴，锚点 = 墙摆好时轴端的初始世界坐标），墙受重力绕轴摆动、可被踩/推；
    /// ② Chain —— 从场景摆的 chainAnchor 垂到墙的 attachPoint（Verlet 附刚体模式），
    ///    绷紧后吊住墙的自由端。
    ///
    /// 锁链可配置：Chain.Settings 全参数（链段长/段数/迭代/重力/阻尼/段碰撞/线宽/排序）。
    /// 锁链可切断：爆炸波及（Chain 已订阅 HazardBus.Blast）和绳索枪飞行
    /// （Chain.Active 静态注册表已被 RopeGun.CutChainsNearHook 遍历）都能切断，
    /// 全断后墙只剩合页约束、绕轴自由摆动。断链不可恢复 —— 与 Bomb 悬挂模式一致。
    ///
    /// 层建议：放在 Terrain(6) / Breakable(11) 层，玩家四向接触检测和绳索枪
    /// 地形命中都只认这两层（ContactSensor.terrainMask / RopeGun.hitMask）。
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(Collider2D))]
    public class HangWall : MonoBehaviour
    {
        [Header("Hinge")]
        [Tooltip("合页轴在墙上的位置（局部坐标，相对质心 = 默认的碰撞体中心）。如左端 = (-半宽, 0)")]
        [SerializeField] private Vector2 hingePivot = new Vector2(-1f, 0f);
        [Tooltip("绕合页轴的角度限制（度，相对初始角度）。0 = 不限 —— 正常情况下摆动范围由锁链余长和碰撞决定")]
        [SerializeField] private float angleLimit = 0f;

        [Header("Chain")]
        [Tooltip("锁链挂点位置（局部坐标，相对质心）。如右端 = (半宽, 0)")]
        [SerializeField] private Vector2 attachPoint = new Vector2(1f, 0f);
        [Tooltip("锁链固定锚点（场景中摆的空物体，Inspector 拖入）。右键本组件 → Create Chain Anchor 可自动生成")]
        [SerializeField] private Transform chainAnchor;
        [Tooltip("锁链配置：链段长/段数/迭代/重力/阻尼/段碰撞/线宽/排序")]
        [SerializeField] private Chain.Settings chainSettings = new Chain.Settings();

        [Header("Editor")]
        [Tooltip("仅编辑器用：Create Chain Anchor 菜单按这个高度在挂点正上方生成锚点")]
        [SerializeField] private float anchorHeight = 3f;

        private Rigidbody2D body;
        private HingeJoint2D hinge;
        private Chain chain;

        /// <summary>运行时可读：锁链是否完整（未被切断）。</summary>
        public bool ChainIntact => chain != null && chain.IsIntact;

        void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            BuildHinge();
            BuildChain();
        }

        // 合页：世界固定轴。connectedAnchor = 墙摆好时的轴端世界坐标，之后墙绕它转
        private void BuildHinge()
        {
            hinge = gameObject.AddComponent<HingeJoint2D>();
            hinge.anchor = hingePivot;
            hinge.connectedBody = null;         // 锚点钉死在世界，不随任何物体移动
            hinge.connectedAnchor = (Vector2)transform.TransformPoint(hingePivot);

            if (angleLimit > 0f)
            {
                hinge.useLimits = true;
                hinge.limits = new JointAngleLimits2D { min = -angleLimit, max = angleLimit };
            }
        }

        // 锁链：从固定锚点垂到墙的自由端挂点，Verlet 附刚体模式（偏移挂点，不挂质心）
        private void BuildChain()
        {
            if (chainAnchor == null)
            {
                Debug.LogWarning($"{name} 没有配置 chainAnchor：墙将只受合页约束。请拖入锚点或右键执行 Create Chain Anchor", this);
                return;
            }

            GameObject chainGo = new GameObject("HangChain");
            chainGo.transform.SetParent(transform, false);

            chain = chainGo.AddComponent<Chain>();
            chain.Configure(chainSettings);
            chain.Init(body, chainAnchor.position, attachPoint);
        }

        // ---- 编辑器辅助 ----

        // 在 Scene 视图画出轴/挂点/锁链示意，方便摆关卡时一眼看出墙会怎么动
        void OnDrawGizmosSelected()
        {
            Vector3 pivot = transform.TransformPoint(hingePivot);
            Vector3 attach = transform.TransformPoint(attachPoint);

            // 合页轴：橙色十字 —— 墙绕这个点转
            Gizmos.color = new Color(1f, 0.55f, 0.15f, 0.9f);
            float s = 0.4f;
            Gizmos.DrawLine(pivot + Vector3.left * s, pivot + Vector3.right * s);
            Gizmos.DrawLine(pivot + Vector3.down * s, pivot + Vector3.up * s);
            Gizmos.DrawWireSphere(pivot, s * 0.6f);

            // 挂点：青色圆
            Gizmos.color = new Color(0.2f, 0.9f, 0.9f, 0.9f);
            Gizmos.DrawWireSphere(attach, 0.18f);

            // 锁链：锚点到挂点的虚线示意
            if (chainAnchor != null)
            {
                Gizmos.color = new Color(0.75f, 0.75f, 0.75f, 0.8f);
                Gizmos.DrawLine(chainAnchor.position, attach);
            }
        }

        // 在挂点正上方生成一个固定锚点空物体并自动接好引用，省得手动摆
        [ContextMenu("Create Chain Anchor")]
        private void CreateChainAnchor()
        {
            if (chainAnchor != null)
            {
                Debug.LogWarning($"{name} 已经有 chainAnchor 了，请先清掉引用再生成", this);
                return;
            }

            GameObject go = new GameObject($"{name}_ChainAnchor");
            go.transform.position = transform.TransformPoint(attachPoint) + Vector3.up * anchorHeight;
            chainAnchor = go.transform;

            Debug.Log($"已生成锚点 {go.name} 并接入 chainAnchor", go);
        }
    }
}
