using Inkform.Bus;
using Inkform.Life;
using Inkform.Tool;
using UnityEngine;

namespace Inkform.Level
{
    /// <summary>
    /// 重生导演：记住当前检查点，死亡后停顿一小会儿再把死者放回去。
    /// 只用瞬移不重载场景 —— 于是不依赖 Build Settings，AudioManager 那类跨场景单例也不用重建。
    /// 挂在 GameManager 上（那里已经是 InputHandler / AudioDirector / AudioManager 的宿主）。
    /// </summary>
    public class RespawnDirector : MonoBehaviour
    {
        [Header("Respawn")]
        // 兜底值：死因没配策略时用它。正常情况下停顿时长由 DeathStrategy.RespawnDelay 决定 ——
        // 「多久回来」是死法的属性（摔死该比刺死回得快），不是复活系统的属性
        [SerializeField] private float fallbackDelay = 0.9f;     // 死亡到复活的停顿，蔚蓝大概 1 秒

        private Vector2 checkpoint;
        private GameObject pending;         // 正在等复活的死者，null = 现在没人在等

        // 必须用非缩放计时：死亡当帧死亡策略就会请求 hitstop 把 timeScale 压到 0，
        // 普通 Timer 的 Time.time 那时是冻住的，等它到期等于永远等不到
        private UnscaledTimer respawnTimer;

        // 和 Bomb.Player / AudioManager.Listener 同一套懒缓存：换场景后旧引用会变成
        // Unity 的 fake-null，下次取用时自动重找，所以不需要写 ResetStatics
        private DeathDirector deathCache;

        private DeathDirector Deaths
        {
            get
            {
                // 用 FindAnyObjectByType 而不是 FindFirstObjectByType：后者依赖 instance ID
                // 顺序、已废弃（顺序本来就不保证稳定，这里也不关心哪个先找到）
                if (deathCache == null) deathCache = FindAnyObjectByType<DeathDirector>();
                return deathCache;
            }
        }

        void OnEnable()
        {
            LifeBus.Died += OnDied;
            LifeBus.CheckpointSet += OnCheckpointSet;
        }

        void OnDisable()
        {
            LifeBus.Died -= OnDied;
            LifeBus.CheckpointSet -= OnCheckpointSet;
        }

        void Start()
        {
            GameObject player = GameObject.FindGameObjectWithTag(Tags.Player);
            if (player == null) return;

            // 开局的复活点：优先用勾了 isStartPoint 的检查点并把玩家放过去；
            // 一个都没勾也能正常玩 —— 退回「玩家在场景里被摆在哪」
            Checkpoint start = FindStartPoint();
            if (start == null)
            {
                checkpoint = player.transform.position;
                return;
            }

            checkpoint = start.SpawnPos;
            LifeBus.RaiseRespawned(player, checkpoint);   // 复用同一条复活路径，省得瞬移逻辑写两份
            FxBus.RaiseSnap();
        }

        void Update()
        {
            if (pending == null) return;
            if (respawnTimer.IsRunning) return;

            LifeBus.RaiseRespawned(pending, checkpoint);
            pending = null;

            // 玩家刚被瞬移走，不切一下的话相机会拖着惯性从死亡点一路滑过来
            FxBus.RaiseSnap();
        }

        private void OnCheckpointSet(Vector2 pos)
        {
            checkpoint = pos;
        }

        private void OnDied(DeathContext ctx)
        {
            pending = ctx.Victim;

            // 场景里没有 DeathDirector、或这个死因没配策略时退回兜底值：
            // 缺了策略只该丢掉演出，不该把人永远留在死亡状态里
            DeathDirector deaths = Deaths;
            DeathStrategy strategy = deaths != null ? deaths.Resolve(ctx.Cause) : null;

            respawnTimer.Set(strategy != null ? strategy.RespawnDelay : fallbackDelay);
        }

        // 不带排序参数的重载即当前推荐 API：带 FindObjectsSortMode 的版本在 Unity 6000.4 已废弃，
        // 理由和 AudioManager 那边一样 —— instance ID 的顺序本来就不保证稳定，这里也不关心顺序
        private Checkpoint FindStartPoint()
        {
            Checkpoint[] all = Object.FindObjectsByType<Checkpoint>();
            foreach (Checkpoint c in all)
            {
                if (c.IsStartPoint) return c;
            }
            return null;
        }
    }
}
