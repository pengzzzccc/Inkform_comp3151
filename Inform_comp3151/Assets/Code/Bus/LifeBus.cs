using System;
using UnityEngine;

namespace Inkform.Bus
{
    /// <summary>
    /// 生命总线：死亡 / 复活 / 检查点。死亡是瞬时信号，死亡次数和「现在是不是死着」是持续状态，
    /// 所以既发事件也存快照 —— 和 PlayerBus 一样由总线自己负责去重。
    /// 死者靠 victim 认领自己，发布方（Spike）不需要认识玩家，玩家也不需要认识 Spike。
    /// </summary>
    public static class LifeBus
    {
        /// <summary>死亡：victim = 死者，from = 致死点（碎块从这里朝外弹开）。
        /// 只有玩家会死，一次死亡恰好发一次，所以震屏/音效这类整体反馈直接听它就行 ——
        /// 不需要像 HazardBus 那样再分 Exploded（逐受害者）和 Blast（整体）两层。</summary>
        public static event Action<GameObject, Vector2> Died;

        /// <summary>复活：victim = 复活者，pos = 复活坐标。</summary>
        public static event Action<GameObject, Vector2> Respawned;

        /// <summary>检查点被激活：pos = 从此以后的复活坐标。</summary>
        public static event Action<Vector2> CheckpointSet;

        // 当前快照：订阅者可随时读取，不必自己跟着记一份
        public static int DeathCount { get; private set; }
        public static bool IsDead { get; private set; }

        public static void RaiseDied(GameObject victim, Vector2 from)
        {
            // 去重：同一帧可能有好几块刺同时判定到玩家，不挡的话计数翻倍、碎块也是双份
            //（和 Bomb.exploded 一个理由，只是那个防的是自己被调两次，这个防的是多个来源）
            if (IsDead) return;
            IsDead = true;
            DeathCount++;

            Died?.Invoke(victim, from);
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
        }
    }
}
