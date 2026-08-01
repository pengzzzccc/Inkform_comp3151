using Inkform.Life;
using System;
using UnityEngine;

namespace Inkform.Bus
{
    /// <summary>
    /// 生命总线：死亡 / 复活 / 检查点。死亡是瞬时信号，死亡次数和「现在是不是死着」是持续状态，
    /// 所以既发事件也存快照 —— 和 PlayerBus 一样由总线自己负责去重。
    /// 死者靠 ctx.Victim 认领自己，发布方（Spike）不需要认识玩家，玩家也不需要认识 Spike。
    /// </summary>
    public static class LifeBus
    {
        /// <summary>死亡：ctx 里带着死者、致死点和死因。
        /// 只有玩家会死，一次死亡恰好发一次，所以震屏/音效这类整体反馈直接听它就行 ——
        /// 不需要像 HazardBus 那样再分 Exploded（逐受害者）和 Blast（整体）两层。
        /// 「这次死亡该怎么演」由 DeathDirector 按 ctx.Cause 选策略决定，本总线不关心。</summary>
        public static event Action<DeathContext> Died;

        /// <summary>复活：victim = 复活者，pos = 复活坐标。</summary>
        public static event Action<GameObject, Vector2> Respawned;

        /// <summary>检查点被激活：pos = 从此以后的复活坐标。</summary>
        public static event Action<Vector2> CheckpointSet;

        // 当前快照：订阅者可随时读取，不必自己跟着记一份
        public static int DeathCount { get; private set; }
        public static bool IsDead { get; private set; }

        /// <summary>最近一次死亡的上下文。RespawnDirector 靠它反查该等多久 ——
        /// 「多久复活」写在死法上，而事件参数在死亡那一刻之后就取不到了，所以要存一份快照。
        /// 快照必须在 Invoke 之前写好：订阅者回调里读到的得是本次死亡而不是上一次。</summary>
        public static DeathContext CurrentDeath { get; private set; }

        public static void RaiseDied(in DeathContext ctx)
        {
            // 去重：同一帧可能有好几块刺同时判定到玩家，不挡的话计数翻倍、碎块也是双份
            //（和 Bomb.exploded 一个理由，只是那个防的是自己被调两次，这个防的是多个来源）
            if (IsDead) return;
            IsDead = true;
            DeathCount++;
            CurrentDeath = ctx;

            Died?.Invoke(ctx);
        }

        public static void RaiseRespawned(GameObject victim, Vector2 pos)
        {
            IsDead = false;
            Respawned?.Invoke(victim, pos);
        }

        public static void RaiseCheckpointSet(Vector2 pos)
        {
            CheckpointSet?.Invoke(pos);
        }

        // 静态字段不随场景重载清空；关闭 Domain Reload 时会残留上一次运行的死订阅者
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Died = null;
            Respawned = null;
            CheckpointSet = null;
            // 计数也要清：ScriptableObject 那套「上次运行残留」的坑在静态字段上同样存在
            DeathCount = 0;
            IsDead = false;
            CurrentDeath = default;
        }
    }
}
