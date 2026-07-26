using System;
using UnityEngine;

namespace Inkform.Bus
{
    /// <summary>
    /// 危险物总线：爆炸这类「范围波及」事件由危险物自己发布，被波及者自行认领。
    /// 纯信号型（不保存快照）—— 爆炸是瞬时事件，没有「当前状态」可言。
    /// </summary>
    public static class HazardBus
    {
        /// <summary>爆炸事件：victim = 被波及对象，center = 爆心，force = 推进力度。</summary>
        public static event Action<GameObject, Vector2, float> Exploded;

        public static void RaiseExploded(GameObject victim, Vector2 center, float force)
        {
            Exploded?.Invoke(victim, center, force);
        }

        // 静态字段不随场景重载清空；关闭 Domain Reload 时会残留上一次运行的死订阅者
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Exploded = null;
        }
    }
}
