using Inkform.Bus;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// 屏幕级特效：卡帧 hitstop、全屏闪色、后处理 punch（暗角/色差）。
/// 三者都只听 FxBus 的命令，不关心是什么游戏事件触发的。
/// 计时一律用 UnscaledTimer —— 卡帧期间 Time.time 是冻住的，用普通 Timer 会永远不到期。
/// </summary>
public class ScreenFx : MonoBehaviour
{
    [Header("Hit stop")]
    [SerializeField] private float maxHitStop = 0.25f;      // 安全上限，防止填个大数字把游戏冻太久

    [Header("Flash")]
    [SerializeField] private Image flashImage;              // 全屏 UI Image，记得关掉它的 Raycast Target

    [Header("Post punch")]
    [SerializeField] private Volume volume;
    [SerializeField] private float vignettePunch = 0.35f;   // amount = 1 时的暗角增量
    [SerializeField] private float chromaPunch = 0.8f;      // amount = 1 时的色差增量

    private UnscaledTimer stopTimer;

    private Color flashColor;
    private float flashDuration;
    private UnscaledTimer flashTimer;

    private Vignette vignette;
    private ChromaticAberration chroma;
    private float baseVignette, baseChroma;
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
            if (volume.profile.TryGet(out chroma)) baseChroma = chroma.intensity.value;
        }

        SetFlashAlpha(0f);
    }

    void OnEnable()
    {
        FxBus.HitStopRequested += OnHitStop;
        FxBus.FlashRequested += OnFlash;
        FxBus.PunchRequested += OnPunch;
    }

    void OnDisable()
    {
        FxBus.HitStopRequested -= OnHitStop;
        FxBus.FlashRequested -= OnFlash;
        FxBus.PunchRequested -= OnPunch;

        // 保险：本组件被禁用/销毁时若仍处于卡帧，必须把时间放回去，否则整个游戏永久冻结
        if (Time.timeScale == 0f) Time.timeScale = 1f;

        // 后处理值还原，免得停在 punch 峰值上
        if (vignette != null) vignette.intensity.value = baseVignette;
        if (chroma != null) chroma.intensity.value = baseChroma;
    }

    void Update()
    {
        // 卡帧到期恢复
        if (Time.timeScale == 0f && !stopTimer.IsRunning) Time.timeScale = 1f;

        FlashStep();
        PunchStep();
    }

    private void FlashStep()
    {
        if (flashImage == null || flashDuration <= 0f) return;

        float k = flashTimer.IsRunning ? flashTimer.Remaining / flashDuration : 0f;
        SetFlashAlpha(flashColor.a * k);
    }

    private void PunchStep()
    {
        float k = (punchDuration > 0f && punchTimer.IsRunning) ? punchTimer.Remaining / punchDuration : 0f;
        float a = punchAmount * k;

        if (vignette != null) vignette.intensity.value = Mathf.Clamp01(baseVignette + vignettePunch * a);
        if (chroma != null) chroma.intensity.value = Mathf.Clamp01(baseChroma + chromaPunch * a);
    }

    private void SetFlashAlpha(float alpha)
    {
        if (flashImage == null) return;

        Color c = flashColor;
        c.a = alpha;
        flashImage.color = c;
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

    private void OnFlash(Color color, float duration)
    {
        if (duration <= 0f) return;

        flashColor = color;
        flashDuration = duration;
        flashTimer.Set(duration);
    }

    private void OnPunch(float amount, float duration)
    {
        if (duration <= 0f) return;

        punchAmount = Mathf.Clamp01(amount);
        punchDuration = duration;
        punchTimer.Set(duration);
    }
}
