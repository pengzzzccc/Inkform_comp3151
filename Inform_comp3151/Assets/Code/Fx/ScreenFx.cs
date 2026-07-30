using Inkform.Bus;
using Inkform.Tool;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Inkform.Fx
{
    /// <summary>
    /// 屏幕级特效：卡帧 hitstop 和后处理暗角 punch。
    /// 两者都只听 FxBus 的命令，不关心是什么游戏事件触发的（实际只有死亡会请求暗角，但本类不需要知道）。
    /// 计时一律用 UnscaledTimer —— 卡帧期间 Time.time 是冻住的，用普通 Timer 会永远不到期。
    /// </summary>
    public class ScreenFx : MonoBehaviour
    {
        [Header("Hit stop")]
        [SerializeField] private float maxHitStop = 0.25f;      // 安全上限，防止填个大数字把游戏冻太久

        [Header("Post punch")]
        [SerializeField] private Volume volume;
        [SerializeField] private float vignettePunch = 0.35f;   // amount = 1 时的暗角增量

        private UnscaledTimer stopTimer;

        private Vignette vignette;
        private float baseVignette;
        private float punchAmount, punchDuration;
        private UnscaledTimer punchTimer;

        void Awake()
        {
            if (volume != null)
            {
                // 用 profile 而不是 sharedProfile：profile 的 getter 会自动复制一份专属副本，
                // 运行时改它不会污染工程里的 Volume Profile 资产；
                // sharedProfile 才是共享资产本体，改了会写进 .asset 且影响所有用它的 Volume
                if (volume.profile.TryGet(out vignette)) baseVignette = vignette.intensity.value;
            }
        }

        void OnEnable()
        {
            FxBus.HitStopRequested += OnHitStop;
            FxBus.PunchRequested += OnPunch;
        }

        void OnDisable()
        {
            FxBus.HitStopRequested -= OnHitStop;
            FxBus.PunchRequested -= OnPunch;

            // 保险：本组件被禁用/销毁时若仍处于卡帧，必须把时间放回去，否则整个游戏永久冻结
            if (Time.timeScale == 0f) Time.timeScale = 1f;

            // 后处理值还原，免得停在 punch 峰值上
            if (vignette != null) vignette.intensity.value = baseVignette;
        }

        void Update()
        {
            // 卡帧到期恢复
            if (Time.timeScale == 0f && !stopTimer.IsRunning) Time.timeScale = 1f;

            PunchStep();
        }

        private void PunchStep()
        {
            float k = (punchDuration > 0f && punchTimer.IsRunning) ? punchTimer.Remaining / punchDuration : 0f;
            float a = punchAmount * k;

            if (vignette != null) vignette.intensity.value = Mathf.Clamp01(baseVignette + vignettePunch * a);
        }

        private void OnHitStop(float duration)
        {
            if (duration <= 0f) return;
            duration = Mathf.Min(duration, maxHitStop);

            // 重叠请求取更长的那个，别互相打断
            if (duration <= stopTimer.Remaining) return;

            stopTimer.Set(duration);
            Time.timeScale = 0f;
        }

        private void OnPunch(float amount, float duration)
        {
            if (duration <= 0f) return;

            punchAmount = Mathf.Clamp01(amount);
            punchDuration = duration;
            punchTimer.Set(duration);
        }
    }
}
