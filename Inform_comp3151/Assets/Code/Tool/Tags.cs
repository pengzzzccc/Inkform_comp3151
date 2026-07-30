namespace Inkform.Tool
{
    /// <summary>
    /// 场景里用到的 Tag 字符串。CompareTag 传错字符串不会报错、只会永远不匹配（悄悄失效），
    /// 集中成常量之后拼错就变成编译错误。
    /// 注意：新增常量必须同时在 Project Settings > Tags and Layers 里建好同名 tag，
    /// 否则 CompareTag 会在运行时抛「is not defined」。
    /// </summary>
    public static class Tags
    {
        public const string Player = "Player";
        public const string BreakAble = "BreakAble";
    }
}
