using Inkform.Bus;
using UnityEngine;

/// <summary>
/// 特效导演：把「游戏里发生了什么」翻译成「屏幕上该有什么反馈」。
/// 订阅 HazardBus.Blast（每次爆炸恰好一次），按爆心到相机的距离衰减后发出整套 FxBus 命令。
/// 「一次爆炸该多晃」的调参全集中在这一个 Inspector 里 ——
/// Bomb 不需要知道特效存在，CamHandler / ScreenFx 也不需要知道爆炸存在。
/// 挂在 Main Camera 上（距离衰减以本物体位置为观察点）。
/// </summary>
public class FxDirector : MonoBehaviour
{
    [Header("Blast falloff")]
    [SerializeField] private float falloffRange = 20f;      // 爆心离得比这还远就完全无感

    [Header("Blast FX")]
    [SerializeField] private float blastTrauma = 0.55f;
    [SerializeField] private float blastHitStop = 0.06f;
    [SerializeField] private float blastZoom = -0.35f;      // 负值 = 推近
    [SerializeField] private float blastZoomTime = 0.25f;
    [SerializeField] private Color blastFlashColor = new Color(1f, 1f, 1f, 0.5f);
    [SerializeField] private float blastFlashTime = 0.12f;
    [SerializeField] private float blastPunch = 0.5f;
    [SerializeField] private float blastPunchTime = 0.3f;

    void OnEnable()
    {
        HazardBus.Blast += OnBlast;
    }

    void OnDisable()
    {
        HazardBus.Blast -= OnBlast;
    }

    private void OnBlast(Vector2 center, float radius, float force)
    {
        float k = falloffRange <= 0f
            ? 1f
            : Mathf.Clamp01(1f - Vector2.Distance(center, transform.position) / falloffRange);
        if (k <= 0f) return;

        // 每项都能在 Inspector 里单独调 0 关掉，互不影响
        if (blastTrauma > 0f) FxBus.RaiseShake(blastTrauma * k);
        if (blastHitStop > 0f) FxBus.RaiseHitStop(blastHitStop * k);
        if (blastZoom != 0f) FxBus.RaiseZoom(blastZoom * k, blastZoomTime);
        if (blastPunch > 0f) FxBus.RaisePunch(blastPunch * k, blastPunchTime);

        if (blastFlashColor.a > 0f)
        {
            Color c = blastFlashColor;
            c.a *= k;
            FxBus.RaiseFlash(c, blastFlashTime);
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(transform.position, falloffRange);
    }
}
