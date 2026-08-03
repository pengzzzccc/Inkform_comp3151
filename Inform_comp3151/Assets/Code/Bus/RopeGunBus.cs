using Inkform.Player;
using System;
using UnityEngine;

namespace Inkform.Bus
{
    /// <summary>
    /// 绳索枪总线：射程覆盖、模式切换这类「别的系统要通知绳索枪」的信号。
    /// 纯信号型不存快照 —— 覆盖/模式是瞬时事件，绳索枪自己记状态。
    /// RopeRangeZone 发 RangeOverride/RangeRestored，AudioDirector 听 ModeChanged 播切换音效。
    /// </summary>
    public static class RopeGunBus
    {
        /// <summary>射程被覆盖（进入特殊区域）：range = 区域指定的最大射程。</summary>
        public static event Action<float> RangeOverride;

        /// <summary>射程覆盖解除（离开区域）：绳索枪恢复自己的默认射程。</summary>
        public static event Action RangeRestored;

        /// <summary>绳索枪模式切换（收缩/悬挂）。音效等表现靠它驱动。</summary>
        public static event Action<GrappleMode> ModeChanged;

        public static void RaiseRangeOverride(float range) => RangeOverride?.Invoke(range);

        public static void RaiseRangeRestored() => RangeRestored?.Invoke();

        public static void RaiseModeChanged(GrappleMode mode) => ModeChanged?.Invoke(mode);

        // 静态字段不随场景重载清空；关闭 Domain Reload 时会残留上一次运行的死订阅者
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            RangeOverride = null;
            RangeRestored = null;
            ModeChanged = null;
        }
    }
}
