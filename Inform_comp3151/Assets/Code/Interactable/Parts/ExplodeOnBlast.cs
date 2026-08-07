using Inkform.Bus;
using Inkform.Tool;
using UnityEngine;

namespace Inkform.Interactable.Parts
{
    /// <summary>
    /// 连锁爆炸触发：被别的爆炸波及后延迟引信再炸。配合 ExplodePart 使用。
    /// 靠 HazardBus.Exploded 的 victim 自认领 —— 发布方不需要认识本物体，
    /// 一排爆炸物因此会依次炸开而不是同一帧全炸光。
    /// </summary>
    public class ExplodeOnBlast : MonoBehaviour, IInteractablePart
    {
        [Header("Chain")]
        [Tooltip("被别的爆炸波及后，隔多久跟着炸（0 = 当帧同步连爆）")]
        [SerializeField] private float chainDelay = 0.1f;

        private Interactable root;
        private ExplodePart explode;
        private Timer timer;
        private bool pending;       // 已被波及、正等引信

        public void Attach(Interactable root) => this.root = root;

        void OnEnable() { HazardBus.Exploded += OnChainExploded; }
        void OnDisable() { HazardBus.Exploded -= OnChainExploded; }

        void Start()
        {
            if (!root.TryGetPart(out explode))
                Debug.LogWarning($"{root.name} 挂了 ExplodeOnBlast 但没挂 ExplodePart，连锁不会生效", root);
        }

        // 纯监听：不消费接触，让 ExplodeOnContact 之类正常收到
        public bool HandleContact(ContactPhase phase, Collider2D other) => false;

        // 由 HazardBus 在别的爆炸波及自己时回调：victim 自认领
        private void OnChainExploded(GameObject victim, Vector2 center, float force)
        {
            if (victim != root.gameObject) return;
            pending = true;
            timer.Set(chainDelay);
        }

        void Update()
        {
            if (!pending || timer.IsRunning) return;
            pending = false;
            explode?.Explode();
        }
    }
}
