using UnityEngine;

namespace Inkform.Life
{
    /// <summary>
    /// 一次死亡的全部已知事实。策略据此决定这次死亡怎么演。
    ///
    /// 用 readonly struct 而不是 class：死亡虽然不频繁，但事件参数走值类型就不会每次都产生垃圾，
    /// 和 Tool/Timer 用 struct 是同一个取向。传参一律加 in，避免大结构体的隐式拷贝。
    /// </summary>
    public readonly struct DeathContext
    {
        /// <summary>死者。订阅方靠它认领自己 —— 发布方（Spike）不需要认识玩家。</summary>
        public readonly GameObject Victim;

        /// <summary>致死点。碎块从这里朝外弹，所以必须是真正的接触点而不是发布方的 transform.position
        /// （一整排刺画在同一张 Tilemap 上时那是网格原点，碎块会齐刷刷朝几十格外飞）。</summary>
        public readonly Vector2 From;

        /// <summary>死因。DeathDirector 靠它选策略。</summary>
        public readonly DeathCause Cause;

        public DeathContext(GameObject victim, Vector2 from, DeathCause cause)
        {
            Victim = victim;
            From = from;
            Cause = cause;
        }
    }
}
