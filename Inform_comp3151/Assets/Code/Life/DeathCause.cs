namespace Inkform.Life
{
    /// <summary>
    /// 死因。决定这次死亡由哪个 DeathStrategy 来演 —— 于是「怎么死的」和「死了怎么演」彻底分开：
    /// Spike 只需要说自己是刺，不需要知道碎块、抖屏、复活延迟长什么样。
    ///
    /// Blast / Void 目前没有任何发布方，是预留的扩展点：
    /// 补上「炸弹能炸死人」「掉出世界摔死」时，只需给它们各配一份策略资产，本枚举不用改。
    /// </summary>
    public enum DeathCause
    {
        Spike,      // 尖刺：唯一真正会被触发的死因
        Blast,      // 爆炸致死（预留，当前 Bomb 只击退不致死）
        Void,       // 掉出世界 / 深渊（预留，当前没有 DeathZone）
    }
}
