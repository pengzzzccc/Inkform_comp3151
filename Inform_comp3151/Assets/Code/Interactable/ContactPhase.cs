namespace Inkform.Interactable
{
    /// <summary>
    /// 接触阶段：Enter = 刚进入 / Stay = 停留中 / Exit = 刚离开。
    /// 主节点把 OnTrigger* 回调按此分发 —— parts 只对关心的阶段做处理
    /// （如 Checkpoint 只看 Enter，危险物的致死逻辑 Enter/Stay 都要接，
    /// 玩家卡在触发区里不动时 Enter 不会重发）。
    /// </summary>
    public enum ContactPhase
    {
        Enter,
        Stay,
        Exit,
    }
}
