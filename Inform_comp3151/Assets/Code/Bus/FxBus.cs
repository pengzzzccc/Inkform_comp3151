using System;
using UnityEngine;

namespace Inkform.Bus
{
    /// <summary>
    /// 特效总线：命令型（不是事实型）—— 发布方说「给我抖一下」，不关心谁来抖、怎么抖。
    /// 于是 Bomb 不需要持有相机引用，相机也不需要知道爆炸的存在。
    /// 具体某个游戏事件该配多强的特效，由 FxDirector 统一翻译。
    /// </summary>
    public static class FxBus
    {
        /// <summary>屏幕抖动：amount 是 trauma 增量（0~1），多次请求累加而非重置，衰减速度由相机设置决定。</summary>
        public static event Action<float> ShakeRequested;

        /// <summary>卡帧：timeScale 归零的时长，单位是非缩放秒。</summary>
        public static event Action<float> HitStopRequested;

        /// <summary>镜头缩放 punch：amount = orthographicSize 的瞬时增量（负值为推近），duration = 回弹时长。</summary>
        public static event Action<float, float> ZoomRequested;

        /// <summary>全屏闪色：color 含 alpha 作为峰值不透明度，duration = 淡出时长。</summary>
        public static event Action<Color, float> FlashRequested;

        /// <summary>后处理 punch：amount = 暗角/色差的强度增量（0~1），duration = 回落时长。</summary>
        public static event Action<float, float> PunchRequested;

        public static void RaiseShake(float amount)
        {
            ShakeRequested?.Invoke(amount);
        }

        public static void RaiseHitStop(float duration)
        {
            HitStopRequested?.Invoke(duration);
        }

        public static void RaiseZoom(float amount, float duration)
        {
            ZoomRequested?.Invoke(amount, duration);
        }

        public static void RaiseFlash(Color color, float duration)
        {
            FlashRequested?.Invoke(color, duration);
        }

        public static void RaisePunch(float amount, float duration)
        {
            PunchRequested?.Invoke(amount, duration);
        }

        // 静态字段不随场景重载清空；关闭 Domain Reload 时会残留上一次运行的死订阅者
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            ShakeRequested = null;
            HitStopRequested = null;
            ZoomRequested = null;
            FlashRequested = null;
            PunchRequested = null;
        }
    }
}
