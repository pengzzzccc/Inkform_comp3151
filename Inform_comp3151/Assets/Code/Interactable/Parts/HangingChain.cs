using Inkform.Item;
using System.Collections.Generic;
using UnityEngine;

namespace Inkform.Interactable.Parts
{
    /// <summary>
    /// 绳索悬挂：从悬挂锚点（HangAnchor 子物体）垂到本物体挂点，给每个锚点生成一条
    /// 物理链（Chain，Verlet 绳索）。炸弹悬挂模式（Bomb.BuildHangingMode）的框架版写法 ——
    /// 锚点靠 HangAnchor 标记组件自动收集，不需要手动拖引用。
    ///
    /// 锚点约定：锚点是挂载了 HangingChain 的物体的**子节点**，由标记组件识别、
    /// GetComponentsInChildren 自动收集（子节点约束因此是结构保证，不用运行时校验）。
    /// 运行时锚点取 Attach 时的世界坐标（钉死在世界，不随本物体移动）。
    /// 链可被爆炸波及（Chain 已订阅 HazardBus.Blast）和绳索枪飞行切断
    /// （Chain.Active 静态注册表），全断后物体只剩自身物理。断链不可恢复 —— 与 Bomb 悬挂模式一致。
    /// </summary>
    public class HangingChain : MonoBehaviour, IInteractablePart
    {
        [Header("Hanging")]
        [Tooltip("挂点相对刚体位置的局部偏移（如墙右缘 = 半宽,0），默认零 = 挂质心")]
        [SerializeField] private Vector2 attachPoint;
        [Tooltip("链条调参包：链段长/段数/迭代/重力/阻尼/段碰撞/线宽/排序")]
        [SerializeField] private Chain.Settings chainSettings = new Chain.Settings();
        [Tooltip("链子材质包：多条链按下标循环取，空槽 = 默认；null = 全部默认")]
        [SerializeField] private ChainMaterialCue materialCue;

        [Header("Editor")]
        [Tooltip("仅编辑器用：Generate Hang Anchors 菜单生成的锚点数量")]
        [SerializeField] private int anchorCount = 1;
        [Tooltip("仅编辑器用：锚点横向等距排布的间距")]
        [SerializeField] private Vector2 anchorSpacing = new Vector2(0.6f, 0f);
        [Tooltip("仅编辑器用：锚点整体抬高到挂点上方多高")]
        [SerializeField] private float anchorHeight = 3f;

        private Interactable root;
        private readonly List<Chain> chains = new List<Chain>();    // 本物生成的链，CutAllChains 用

        public void Attach(Interactable root)
        {
            this.root = root;

            Rigidbody2D body = root.GetComponent<Rigidbody2D>();
            if (body == null)
            {
                Debug.LogWarning($"{root.name} 挂了 HangingChain 但没挂 Rigidbody2D，链条不会生成", root);
                return;
            }

            // 锚点 = HangAnchor 标记子物体，自动收集：子节点约束由结构保证
            HangAnchor[] pts = GetComponentsInChildren<HangAnchor>(true);
            if (pts.Length == 0)
            {
                Debug.LogWarning($"{root.name} 没有悬挂锚点，请在 Inspector 右键执行 Generate Hang Anchors", root);
                return;
            }

            chains.Clear();
            // 每个锚点生成一条链：锚点钉死在世界（取初始世界坐标），之后物体怎么动链都跟着。
            // 链挂在锚点下（Bomb 同款）：锚点销毁/移动时链跟着，层级也干净
            int index = 0;
            foreach (HangAnchor pt in pts)
            {
                GameObject chainGo = new GameObject($"Chain_{pt.name}");
                chainGo.transform.SetParent(pt.transform, false);

                Chain chain = chainGo.AddComponent<Chain>();
                chain.Configure(chainSettings);
                chain.SetMaterial(materialCue != null ? materialCue.PickMaterial(index) : null);   // 多条链循环取材质
                chain.Init(body, pt.transform.position, attachPoint);
                chains.Add(chain);
                index++;
            }
        }

        /// <summary>全部切断：被吞/需释放时调用，断链不可恢复（与 Bomb 悬挂语义一致）。</summary>
        public void CutAllChains()
        {
            foreach (Chain c in chains) c.CutAll();
        }

        // 纯构建：不消费接触
        public bool HandleContact(ContactPhase phase, Collider2D other) => false;

        // ---- 编辑器辅助 ----

        // 等距排开生成 anchorCount 个锚点子物体并自动接入（先清旧的）。
        // 注意：编辑器菜单只允许用组件自身的 transform —— Attach 缓存的 root 只在运行期有效
        [ContextMenu("Generate Hang Anchors")]
        private void GenerateHangAnchors()
        {
            ClearHangAnchors();

            int n = Mathf.Max(1, anchorCount);
            for (int i = 0; i < n; i++)
            {
                GameObject go = new GameObject($"HangAnchor_{i + 1}");
                go.transform.SetParent(transform, false);
                go.AddComponent<HangAnchor>();

                // 以挂点为中心横向等距排开、整体抬到锚点高度，生成后自己在 Scene 里拖
                float x = (i - (n - 1) * 0.5f) * anchorSpacing.x;
                go.transform.localPosition = (Vector3)attachPoint + new Vector3(x, anchorHeight, 0f);
            }
        }

        [ContextMenu("Clear Hang Anchors")]
        private void ClearHangAnchors()
        {
            HangAnchor[] old = GetComponentsInChildren<HangAnchor>(true);
            foreach (HangAnchor o in old)
            {
                if (Application.isPlaying) Destroy(o.gameObject);
                else DestroyImmediate(o.gameObject);
            }
        }
    }
}
