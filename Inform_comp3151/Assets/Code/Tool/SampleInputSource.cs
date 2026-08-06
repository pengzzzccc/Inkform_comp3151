using UnityEngine;

namespace Inkform.Tool
{
    /// <summary>
    /// 每物理帧的输入快照：设备钩子（DeviceInputHook）或脚本驱动（GoldenSessionRunner）
    /// 写入，SessionRecorder 在 FixedUpdate 读取。
    /// 双写双读，录制内容不依赖具体输入设备。
    /// </summary>
    public static class SampleInputSource
    {
        public static Vector2 Move;
        public static bool JumpPressed;
        public static bool JumpHeld;
        public static bool DashPressed;
        public static Vector2 Aim;

        public static void ResetAll()
        {
            Move = Vector2.zero;
            JumpPressed = false;
            JumpHeld = false;
            DashPressed = false;
            Aim = Vector2.zero;
        }
    }
}
