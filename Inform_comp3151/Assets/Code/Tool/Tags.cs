namespace Inkform.Tool
{
    /// <summary>
    /// Tag strings used across the scene. CompareTag does not error on a wrong string — it just
    /// never matches (silent failure). Centralizing them as constants turns typos into compile errors.
    /// Note: every new constant must also have a matching tag created in Project Settings >
    /// Tags and Layers, otherwise CompareTag throws "is not defined" at runtime.
    /// </summary>
    public static class Tags
    {
        public const string Player = "Player";
        public const string BreakAble = "BreakAble";
    }
}
