using Inkform.Bus;
using UnityEngine;

namespace Inkform.Life
{
    /// <summary>
    /// 死亡导演：收到「有人死了」，按死因选出对应的 DeathStrategy 并让它演。
    /// 策略模式里的上下文（Context）—— 本类只负责选，怎么演一概不知道。
    ///
    /// 和 FxDirector / AudioDirector 是同一个套路（订阅总线、把配置集中在一个 Inspector 里），
    /// 区别是那两个按「关注点」分派（屏幕的归屏幕、音频的归音频），本类按「死因」分派。
    /// 挂在 GameManager 上。
    /// </summary>
    public class DeathDirector : MonoBehaviour
    {
        [Header("Strategies")]
        [Tooltip("按死因查表。同一死因配多份时取第一份；漏配的死因会落到 fallback")]
        [SerializeField] private DeathStrategy[] strategies;

        [Tooltip("查不到对应死因时的兜底。留空 = 该死因静默不演出（复活照常）")]
        [SerializeField] private DeathStrategy fallback;

        void OnEnable()
        {
            LifeBus.Died += OnDied;
        }

        void OnDisable()
        {
            LifeBus.Died -= OnDied;
        }

        /// <summary>
        /// 按死因取策略。RespawnDirector 也要用它拿 RespawnDelay ——
        /// 「多久复活」是死法的属性，所以查表逻辑只该有这一份。
        /// </summary>
        public DeathStrategy Resolve(DeathCause cause)
        {
            if (strategies != null)
            {
                // 线性扫：死因就三个，建字典的开销和心智负担都不划算
                foreach (DeathStrategy s in strategies)
                {
                    if (s != null && s.Cause == cause) return s;
                }
            }
            return fallback;
        }

        private void OnDied(DeathContext ctx)
        {
            DeathStrategy strategy = Resolve(ctx.Cause);
            if (strategy == null) return;       // 槽位没配，静默跳过（复活不受影响）

            if (ctx.Victim == null) return;

            // 用 GetComponent 而不是 TryGetComponent：后者对接口类型的支持不可靠。
            // 接口不是 UnityEngine.Object，取不到时是真 null（没有 fake-null 那套），可以直接判
            IDeathBody body = ctx.Victim.GetComponent<IDeathBody>();

            // 死者身上没挂 IDeathBody 是装配错误而不是合法降级 —— 静默跳过的话
            // 表现全无、却又照常复活，排查起来毫无线索，所以要吭一声
            if (body == null)
            {
                Debug.LogWarning($"{ctx.Victim.name} 身上没有 IDeathBody，本次死亡不演出", ctx.Victim);
                return;
            }

            strategy.Execute(ctx, body);
        }
    }
}
