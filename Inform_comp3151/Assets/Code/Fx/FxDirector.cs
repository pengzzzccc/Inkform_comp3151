using Inkform.Bus;
using UnityEngine;

namespace Inkform.Fx
{
    /// <summary>
    /// 特效导演：把「游戏里发生了什么」翻译成「屏幕上该有什么反馈」。
    /// 订阅 HazardBus.Blast（每次爆炸恰好一次），按爆心到相机的距离衰减后发出对应的 FxBus 命令。
    /// 「一次爆炸该多晃」的调参全集中在这一个 Inspector 里 ——
    /// Bomb 不需要知道特效存在，CamHandler / ScreenFx 也不需要知道爆炸存在。
    /// 后处理暗角是死亡专属的，所以 OnBlast 里没有 RaisePunch —— 爆炸只有抖屏 / 卡帧 / 推近。
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

        // 死亡是全场最强的一次反馈，所以每项都比爆炸更重。
        // 这里不做距离衰减：死亡恒发生在玩家身上 ≈ 镜头中心，算出来的系数必然是 1
        [Header("Death FX")]
        [SerializeField] private float deathTrauma = 0.7f;
        [SerializeField] private float deathHitStop = 0.12f;     // ScreenFx.maxHitStop 是 0.25，别超
        [SerializeField] private float deathZoom = -0.5f;        // 负值 = 推近
        [SerializeField] private float deathZoomTime = 0.4f;
        [SerializeField] private float deathPunch = 0.8f;        // 暗角强度增量，全场只有死亡用得到后处理
        [SerializeField] private float deathPunchTime = 0.5f;    // 暗角回落时长

        void OnEnable()
        {
            HazardBus.Blast += OnBlast;
            LifeBus.Died += OnDied;
        }

        void OnDisable()
        {
            HazardBus.Blast -= OnBlast;
            LifeBus.Died -= OnDied;
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
        }

        // 每项都能在 Inspector 里单独调 0 关掉，和 OnBlast 一个写法
        private void OnDied(GameObject victim, Vector2 from)
        {
            if (deathTrauma > 0f) FxBus.RaiseShake(deathTrauma);
            if (deathHitStop > 0f) FxBus.RaiseHitStop(deathHitStop);
            if (deathZoom != 0f) FxBus.RaiseZoom(deathZoom, deathZoomTime);
            if (deathPunch > 0f) FxBus.RaisePunch(deathPunch, deathPunchTime);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(transform.position, falloffRange);
        }
    }
}
