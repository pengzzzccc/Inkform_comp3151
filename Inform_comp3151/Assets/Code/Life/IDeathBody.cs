using UnityEngine;

namespace Inkform.Life
{
    /// <summary>
    /// 尸体的操作面：策略通过它摆布死者的本体，而不直接认识 PlayerDeathFx / SpriteRenderer。
    /// 于是「谁是尸体」和「尸体怎么处置」解耦 —— 以后想让敌人也能死，让它实现本接口即可。
    ///
    /// 只放当前用得到的成员。加 FadeDeathStrategy（淡出而不碎裂）时在这里补一个 FadeOut(float)，
    /// 那才是接口该长大的时机 —— 现在就摆一个没人调的方法只会变成死代码。
    /// </summary>
    public interface IDeathBody
    {
        /// <summary>本体的可见包围盒，碎块照着它切。
        /// 必须在 Hide() 之前取 —— 关掉渲染/碰撞之后包围盒会退化成原点上的零尺寸。</summary>
        Bounds VisualBounds { get; }

        /// <summary>藏起本体。实现方须避开 SetActive(false)：那会触发 OnDisable 退订总线，
        /// 就再也收不到「复活」了。关渲染即可。</summary>
        void Hide();

        /// <summary>复活时显回本体。</summary>
        void Show();
    }
}
