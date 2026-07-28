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

        /// <summary>爆炸发生（整体，每次爆炸恰好发一次）：center = 爆心，radius = 波及半径，force = 推进力度。
        /// 区别于 Exploded —— 那个是逐受害者发的，炸到 N 个发 N 次、一个没炸到发 0 次，
        /// 所以「一次爆炸抖一次屏」这类整体反馈只能听 Blast。</summary>
        public static event Action<Vector2, float, float> Blast;

        /// <summary>可破坏物被炸碎（每碎一个发一次）：center = 被炸碎物体的中心。
        /// 与 Blast 区分 —— Blast 是「发生了一次爆炸」，这个是「有东西被炸碎了」。</summary>
        public static event Action<Vector2> Broken;

        /// <summary>危险物的警戒帧推进了一格：pos = 位置，step = 新帧序号，total = 总帧数。
        /// 逼近预警和引信倒计时共用这一个信号 —— 两者都是「离炸还有多近」的离散推进。</summary>
        public static event Action<Vector2, int, int> Ticked;

        public static void RaiseExploded(GameObject victim, Vector2 center, float force)
        {
            Exploded?.Invoke(victim, center, force);
        }

        public static void RaiseBlast(Vector2 center, float radius, float force)
        {
            Blast?.Invoke(center, radius, force);
        }

        public static void RaiseBroken(Vector2 center)
        {
            Broken?.Invoke(center);
        }

        public static void RaiseTicked(Vector2 pos, int step, int total)
        {
            Ticked?.Invoke(pos, step, total);
        }

        // 静态字段不随场景重载清空；关闭 Domain Reload 时会残留上一次运行的死订阅者
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Exploded = null;
            Blast = null;
            Broken = null;
            Ticked = null;
        }
    }
}
