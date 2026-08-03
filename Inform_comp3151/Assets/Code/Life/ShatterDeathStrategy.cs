using Inkform.Audio;
using Inkform.Bus;
using Inkform.Fx;
using UnityEngine;

namespace Inkform.Life
{
    /// <summary>
    /// 碎裂死法：本体瞬间消失、原地炸成一堆碎块，配一整套屏幕反馈和一条死亡音。
    /// 刺死用的就是这一份；换个 FragmentCue 和更猛的数值，同一个类也能演「被炸死」。
    ///
    /// 这里同时负责碎块、屏幕反馈和音效，是刻意的：一种死法的全部配置集中在一个资产里，
    /// 调「刺死该多痛」不用在 FxDirector、AudioDirector、PlayerDeathFx 三个 Inspector 之间来回跳。
    /// 代价是表现逻辑从「按关注点切分」改成了「按死因切分」—— FxDirector / AudioDirector
    /// 仍然负责爆炸、碎墙、吃吐、跳跃落地等其余全部事件，只是不再管死亡。
    /// </summary>
    [CreateAssetMenu(menuName = "Life/Shatter Death Strategy")]
    public class ShatterDeathStrategy : DeathStrategy
    {
        [Header("Death burst")]
        [SerializeField] private FragmentCue pieces;        // 碎成什么样全写在这份资产里
        [SerializeField] private float burstForce = 14f;    // 碎块初速度，再乘 Cue 里的 forceMultiplier

        // 死亡是全场最强的一次反馈，每项都该比爆炸更重。
        // 这里不做距离衰减：死亡恒发生在玩家身上 ≈ 镜头中心，算出来的系数必然是 1
        [Header("Screen FX")]
        [SerializeField] private float trauma = 0.7f;
        [SerializeField] private float hitStop = 0.12f;     // ScreenFx.maxHitStop 是 0.25，别超
        [SerializeField] private float zoom = -0.5f;        // 负值 = 推近
        [SerializeField] private float zoomTime = 0.4f;
        [SerializeField] private float punch = 0.8f;        // 暗角强度增量，全场只有死亡用得到后处理
        [SerializeField] private float punchTime = 0.5f;

        [Header("Audio")]
        [SerializeField] private SoundCue deathCue;

        public override void Execute(in DeathContext ctx, IDeathBody body)
        {
            // 顺序有讲究：包围盒必须赶在 Hide() 之前取，
            // 关掉渲染之后 bounds 会退化成原点上的零尺寸，碎块会全挤在世界原点
            Bounds bounds = body.VisualBounds;
            body.Hide();

            // Cue 没配时 Shatter 自己会静默跳过，这里不用再判一次
            Shatter.Burst(pieces, bounds, ctx.From, burstForce);

            // 每项都能在 Inspector 里单独调 0 关掉，互不影响（与 FxDirector 的写法一致）
            if (trauma > 0f) FxBus.RaiseShake(trauma);
            if (hitStop > 0f) FxBus.RaiseHitStop(hitStop);
            if (zoom != 0f) FxBus.RaiseZoom(zoom, zoomTime);
            if (punch > 0f) FxBus.RaisePunch(punch, punchTime);

            // 不传位置：死亡恒发生在玩家身上 ≈ 镜头中心，衰减系数必然接近 1。
            // 槽位留空或场景里还没有 AudioManager 都静默跳过
            if (deathCue != null && AudioManager.Instance != null)
                AudioManager.Instance.Play(deathCue);
        }
    }
}
