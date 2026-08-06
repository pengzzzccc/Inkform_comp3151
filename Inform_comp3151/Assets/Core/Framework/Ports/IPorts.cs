using System.Numerics;

namespace Inkform.Core
{
    /// <summary>
    /// 时间端口：可缩放/非缩放双时钟。hitstop（timeScale=0）期间
    /// Time 冻结而 UnscaledTime 照常推进，两个时钟的语义差异由实现方保证。
    /// </summary>
    public interface ITimePort
    {
        float Time { get; }
        float UnscaledTime { get; }
        float DeltaTime { get; }
    }

    /// <summary>
    /// 随机端口：可注入种子实现，便于测试复现。
    /// </summary>
    public interface IRngPort
    {
        float Next01();
        float Range(float min, float max);
        int Next(int max);
    }

    /// <summary>
    /// 物理端口（最小面）：仅暴露当前（M0）用到的能力。
    /// 速度/接触探测等成员在 M2 引入 PlayerSim 时扩展。
    /// </summary>
    public interface IPhysicsPort
    {
        /// <summary>引擎物理重力（世界向量）。</summary>
        Vector2 Gravity { get; }

        /// <summary>圆形胶囊射线检测。返回是否命中；命中时 hitDistance = 接触点距离。
        /// 语义与 Unity Physics2D.CircleCast 一致（起点埋入碰撞体时 hitDistance≈0）。</summary>
        bool CastCircle(Vector2 from, float radius, Vector2 dir, float dist,
                        LayerKey mask, out float hitDistance);
    }

    /// <summary>层掩码抽象：包装一个引擎层掩码值（int）。Shell 侧由 LayerMask.value 转换。</summary>
    public readonly struct LayerKey
    {
        public readonly int Mask;

        public LayerKey(int mask) => Mask = mask;

        public static implicit operator LayerKey(int mask) => new LayerKey(mask);
    }
}
