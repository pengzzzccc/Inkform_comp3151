using Inkform.Bus;
using Inkform.Life;
using UnityEngine;

namespace Inkform.Player
{
    /// <summary>
    /// 玩家死亡的表现侧：把「本体」这件事暴露给死亡策略去摆布。
    /// 本类只回答三个问题 —— 本体看起来多大、怎么藏、怎么显；至于藏了之后是碎成一堆块
    /// 还是慢慢淡出，那是 DeathStrategy 的算法，本类一概不知道。
    ///
    /// 和 PlayerHandler 分开是刻意的 —— 那边只管玩法（停物理、锁输入、瞬移），这边只管本体的可见性。
    /// 挂在 Player 上（要拿本物体的 SpriteRenderer）。
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class PlayerDeathFx : MonoBehaviour, IDeathBody
    {
        private SpriteRenderer sprite;

        void Awake()
        {
            sprite = GetComponent<SpriteRenderer>();
        }

        void OnEnable()
        {
            LifeBus.Respawned += OnRespawned;
        }

        void OnDisable()
        {
            LifeBus.Respawned -= OnRespawned;
        }

        // 用 SpriteRenderer.bounds 而不是碰撞体的，两个理由：
        // ① 碎块该照着「看得见的轮廓」切，而玩家碰撞体是 0.5×0.5 的胶囊、比图小一圈；
        // ② 碰撞体的 bounds 会随 PlayerHandler 那边关物理而失效，而两个组件谁先收到
        //    同一个事件是不保证的 —— 用渲染器的包围盒就绕开了这个先后顺序坑
        public Bounds VisualBounds => sprite.bounds;

        // 本体和碎块不能重叠显示，关渲染而不是 SetActive(false)：
        // 后者会触发 OnDisable 退订总线，就再也收不到「复活」了
        public void Hide() => sprite.enabled = false;

        public void Show() => sprite.enabled = true;

        // 死亡由 DeathDirector 驱动（它拿得到策略），复活则不需要策略参与 ——
        // 无论怎么死的，显回本体都是同一件事，所以这一半仍然直接听总线
        private void OnRespawned(GameObject victim, Vector2 pos)
        {
            if (victim != gameObject) return;

            Show();
        }
    }
}
