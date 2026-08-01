using Inkform.Bus;
using UnityEngine;

namespace Inkform.Fx
{
    /// <summary>
    /// 特效导演：把「游戏里发生了什么」翻译成「屏幕上该有什么反馈」。
    /// 订阅 HazardBus.Blast（每次爆炸恰好一次），按爆心到相机的距离衰减后发出对应的 FxBus 命令。
    /// 「一次爆炸该多晃」的调参全集中在这一个 Inspector 里 ——
    /// Bomb 不需要知道特效存在，CamHandler / ScreenFx 也不需要知道爆炸存在。
    /// 后处理暗角是死亡专属的，所以这里没有 RaisePunch —— 爆炸只有抖屏 / 卡帧 / 推近。
    ///
    /// 死亡的那一份反馈不在这里：它按死因而不是按关注点分派，整套演出归 DeathStrategy 资产管
    /// （见 Life/ShatterDeathStrategy）。本类只剩爆炸这一支。
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
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(transform.position, falloffRange);
        }
    }
}
