using UnityEngine;

namespace Inkform.Life
{
    /// <summary>
    /// 死亡策略（抽象）：一种死法的完整演出算法 —— 尸体怎么处置、屏幕怎么反馈、放哪条音、多久复活。
    ///
    /// 做成 ScriptableObject 而不是纯 C# 类，是沿用 SoundCue / FragmentCue 那套：
    /// 参数在 Inspector 里配、同一份资产可被多处引用、加一种死法只是新建一个资产而不用改代码引用。
    ///
    /// 为什么是策略而不是「参数表」：不同死法的差异不止于数字。碎裂要按网格切块、
    /// 淡出要逐帧改 alpha、下沉要动位置 —— 这是三条不同的代码路径，只能靠多态选，选不出来就得写 switch。
    /// </summary>
    public abstract class DeathStrategy : ScriptableObject
    {
        [Header("Dispatch")]
        [SerializeField] private DeathCause cause = DeathCause.Spike;

        [Header("Respawn")]
        // 死亡到复活的停顿。摆在策略上而不是 RespawnDirector 上：摔死该比刺死回得快，
        // 这是「死法」的属性而不是「复活系统」的属性
        [SerializeField] private float respawnDelay = 0.9f;

        public DeathCause Cause => cause;
        public float RespawnDelay => respawnDelay;

        /// <summary>
        /// 演这一次死亡。由 DeathDirector 在收到 LifeBus.Died 时调用。
        ///
        /// 实现方注意：body.VisualBounds 必须在 body.Hide() 之前取，
        /// 关掉渲染之后包围盒会退化成原点上的零尺寸。
        /// </summary>
        public abstract void Execute(in DeathContext ctx, IDeathBody body);
    }
}
