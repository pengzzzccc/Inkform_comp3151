using Inkform.Tool;
using UnityEngine;

namespace Inkform.Level
{
    /// <summary>
    /// 定点刷新物：在自己的位置上生成一个实例，实例被销毁后冷却若干秒再补一个。
    /// 归属靠持有实例引用判断 —— HazardBus.Exploded 是逐受害者发的、不带炸弹自身身份，
    /// 用它判断「我的那颗炸了没」必然误判（别人炸也会触发，一个没炸到则永远不触发）。
    /// </summary>
    public class Spawner : MonoBehaviour
    {
        [SerializeField] private float spawneraTimer = 5f;
        [SerializeField] private GameObject spawneraObject;

        private Timer timer;
        private GameObject current;     // 本 spawner 当前持有的实例
        private bool cooling;           // 已经发现实例没了、正在等冷却

        void Start()
        {
            Spawn();
        }

        void Update()
        {
            if (current != null) return;        // Unity 重载了 ==，实例被 Destroy 后这里为 null

            if (!cooling)                       // 刚发现实例没了，从这一刻才开始计冷却
            {
                cooling = true;
                timer.Set(spawneraTimer);
                return;
            }

            if (timer.IsRunning) return;
            Spawn();
        }

        private void Spawn()
        {
            // 必须用带 position 的重载：Instantiate(prefab, transform) 会沿用预制体存档里的
            // localPosition（Bomb.prefab 存的是 -4.07, 0.45），而不是挪到本节点位置上
            current = Instantiate(spawneraObject, transform.position, Quaternion.identity);
            cooling = false;
        }
    }
}
