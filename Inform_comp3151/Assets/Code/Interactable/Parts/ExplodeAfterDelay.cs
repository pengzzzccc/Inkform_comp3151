using Inkform.Tool;
using UnityEngine;

namespace Inkform.Interactable.Parts
{
    /// <summary>
    /// 定时爆炸触发：出生后 delay 秒自动爆炸。配合 ExplodePart 使用。
    /// 走缩放计时（Time.time）：hitstop 时游戏时间冻结、炸弹跟着冻结 ——
    /// 与全工程「玩法计时用 Timer」的约定一致。
    /// </summary>
    public class ExplodeAfterDelay : MonoBehaviour, IInteractablePart
    {
        [Header("Fuse")]
        [Tooltip("出生后多久爆炸，秒")]
        [SerializeField] private float delay = 1.5f;

        private Interactable root;
        private ExplodePart explode;
        private Timer timer;
        private bool started;

        public void Attach(Interactable root) => this.root = root;

        void Start()
        {
            if (!root.TryGetPart(out explode))
                Debug.LogWarning($"{root.name} 挂了 ExplodeAfterDelay 但没挂 ExplodePart，不会爆炸", root);
            timer.Set(delay);
            started = true;
        }

        // 纯计时：不消费接触
        public bool HandleContact(ContactPhase phase, Collider2D other) => false;

        void Update()
        {
            if (!started || timer.IsRunning) return;
            started = false;
            explode?.Explode();
        }
    }
}
