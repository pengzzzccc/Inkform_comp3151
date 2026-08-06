using Inkform.Tool;
using System.Collections.Generic;
using UnityEngine;

namespace Inkform.Interactable.Parts
{
    /// <summary>
    /// 定时显隐：让危险物按「出现 onTime → 消失 offTime」的周期循环。消失期间
    /// 碰撞体和渲染全关、玩家可以穿过去；出现期间一切照旧。定时激光柱 / 定时尖刺
    /// 这类物件直接挂本组件（激光柱只要再配个碰撞体和渲染即可）。
    ///
    /// 实现要点：翻转时只切 Collider2D 和 Renderer 的 enabled，**不用 SetActive** ——
    /// 失活会让本组件自己的 Update 停摆、永远无法在消失周期结束时把自己唤醒。
    /// 这也是全工程的老规矩（BreakableWall.SetBroken / Bomb.SetVisible 都是切 enabled）。
    /// 对 Tilemap 同样生效：TilemapRenderer 也是 Renderer。
    /// </summary>
    public class TimedVisibility : MonoBehaviour, IInteractablePart
    {
        [Header("Timing")]
        [Tooltip("出现（碰撞/渲染开启）时长，秒")]
        [SerializeField] private float onTime = 2f;
        [Tooltip("消失（碰撞/渲染关闭）时长，秒。玩家可在此时段穿过")]
        [SerializeField] private float offTime = 1.5f;
        [Tooltip("开跑先亮还是先灭")]
        [SerializeField] private bool startVisible = true;

        private Collider2D body;
        private readonly List<Renderer> renderers = new List<Renderer>();
        private Timer timer;
        private bool visible;

        public void Attach(Interactable root)
        {
            body = root.GetComponent<Collider2D>();
            if (body == null)
                Debug.LogWarning($"{root.name} 的 Interactable 上没有 Collider2D，定时显隐不会生效", root);

            // 渲染器可能挂在本体或子物体上（Tilemap 的渲染在子物体）；只切 enabled，
            // 关掉的渲染器照常拿回引用，不依赖激活顺序
            renderers.Clear();
            renderers.AddRange(root.GetComponentsInChildren<Renderer>(true));

            visible = startVisible;
            timer.Set(PhaseDuration(visible));
            Apply();
        }

        // 纯驱动器：不消费任何接触，让后面的 part（如 HarmOnTouch）正常收到
        public bool HandleContact(ContactPhase phase, Collider2D other) => false;

        void Update()
        {
            if (timer.IsRunning) return;
            Flip();
        }

        private void Flip()
        {
            visible = !visible;
            timer.Set(PhaseDuration(visible));
            Apply();
        }

        // 防抖：时长 0 会每帧翻转，钳到最小有效时长
        private float PhaseDuration(bool forOn) => Mathf.Max(forOn ? onTime : offTime, 0.05f);

        private void Apply()
        {
            if (body != null) body.enabled = visible;
            foreach (Renderer r in renderers) r.enabled = visible;
        }
    }
}
