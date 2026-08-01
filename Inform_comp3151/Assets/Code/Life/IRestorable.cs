namespace Inkform.Life
{
    /// <summary>
    /// 快照（备忘录）：一个关卡物件在某一刻的状态。内容对持有者不透明 ——
    /// LevelMemento 只知道「有这么一份快照，可以让它还原」，不知道里面存的是墙碎没碎还是别的什么。
    /// 于是加一种可还原物件不需要改 LevelMemento 一个字。
    /// </summary>
    public interface IMemento
    {
        /// <summary>把originator 恢复到本快照拍下时的状态。</summary>
        void Restore();
    }

    /// <summary>
    /// 可还原物件（原发者）：能给自己拍一张快照。
    /// 由 LevelMemento 在踩到检查点时统一拍照，死亡复活时统一还原 ——
    /// 否则炸碎的墙不会回来，反复重试同一段关卡会越来越空。
    ///
    /// 实现方注意：还原的前提是物件一直活着。**不要在被破坏时 Destroy 自己**，
    /// 改成关碰撞体 + 关渲染（BreakableWall 就是这么做的）。
    /// </summary>
    public interface IRestorable
    {
        IMemento Capture();
    }
}
