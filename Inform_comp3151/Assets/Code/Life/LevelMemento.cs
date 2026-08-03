using Inkform.Bus;
using System.Collections.Generic;
using UnityEngine;

namespace Inkform.Life
{
    /// <summary>
    /// 关卡快照管理者（备忘录模式里的 Caretaker）：踩到检查点时给全场可还原物件拍一张照，
    /// 死亡复活时统一还原。
    ///
    /// 解决的是一个真实的玩法问题：复活原本只把玩家瞬移回检查点，被炸碎的墙不会回来，
    /// 于是同一段关卡反复重试会越来越空、后面的谜题直接失效。
    ///
    /// 本类不认识任何具体物件类型 —— 只经手 IMemento，内容对它不透明。
    /// 挂在 GameManager 上（那里已经是 RespawnDirector / DeathDirector 的宿主）。
    /// </summary>
    public class LevelMemento : MonoBehaviour
    {
        // 原发者只在开局扫一次：关卡物件是场景里摆好的，运行时不会新增。
        // 这也天然把 Spawner 运行时生成的实例排除在外 —— 那些归 Spawner 自己补货，
        // 纳进来反而会在复活时凭空多出一批炸弹
        private readonly List<IRestorable> originators = new List<IRestorable>();
        private readonly List<IMemento> snapshot = new List<IMemento>();

        void OnEnable()
        {
            LifeBus.CheckpointSet += OnCheckpointSet;
            LifeBus.Respawned += OnRespawned;
        }

        void OnDisable()
        {
            LifeBus.CheckpointSet -= OnCheckpointSet;
            LifeBus.Respawned -= OnRespawned;
        }

        void Start()
        {
            // 用带 includeInactive 的重载：Unity 6000.4 已废弃带 FindObjectsSortMode 的版本
            // （instance ID 排序将来会被 EntityId 取代），不带排序参数的重载即当前推荐 API。
            // 不关心顺序（还原之间互不影响），也不需要排序参数
            MonoBehaviour[] all = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include);

            foreach (MonoBehaviour mb in all)
            {
                if (mb is IRestorable r) originators.Add(r);
            }

            // 开局先拍一张：还没踩到任何检查点就死掉时，也得有一份可还原的初始状态
            Capture();
        }

        private void OnCheckpointSet(Vector2 pos) => Capture();

        private void OnRespawned(GameObject victim, Vector2 pos) => Restore();

        private void Capture()
        {
            snapshot.Clear();
            foreach (IRestorable r in originators)
            {
                // 原发者可能已被别处销毁。必须转回 MonoBehaviour 再判 —— Unity 重载的 ==
                // 挂在 UnityEngine.Object 上，拿接口引用直接判 null 认不出已销毁的对象
                if (r as MonoBehaviour == null) continue;
                snapshot.Add(r.Capture());
            }
        }

        private void Restore()
        {
            foreach (IMemento m in snapshot) m.Restore();
        }
    }
}
