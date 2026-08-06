using Inkform.Core;

namespace Inkform.Tool
{
    /// <summary>
    /// Core 端口占位注册表（M0~M2 过渡期用）：Shell 侧默认实现。
    /// M3 由 GameRoot/ServiceRegistry 统一装配替换。
    /// </summary>
    public static class CoreBridge
    {
        public static IPhysicsPort Physics { get; set; } = new UnityPhysicsPort();
        public static ITimePort Time { get; set; } = new UnityTimePort();
        public static IRngPort Rng { get; set; } = new UnityRngPort();
    }
}
