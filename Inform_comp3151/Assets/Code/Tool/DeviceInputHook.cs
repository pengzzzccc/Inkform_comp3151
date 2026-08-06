using UnityEngine;
using UnityEngine.InputSystem;

namespace Inkform.Tool
{
    /// <summary>
    /// 设备输入钩子：把真实输入设备（键盘/手柄）的当前输入写入 SampleInputSource，
    /// 供 SessionRecorder 录制。与 InputHandler 读同一个 InputSystem_Actions 资产，
    /// 保证录制内容与游戏实际消费的输入一致。
    /// 挂在玩家物体或任意物体上；自动查找玩家。
    /// </summary>
    [DefaultExecutionOrder(-2000)]   // 先于 SessionRecorder 的 FixedUpdate 采样
    public class DeviceInputHook : MonoBehaviour
    {
        private InputSystem_Actions playerInput;
        private InputAction move;
        private InputAction aim;
        private InputAction jump;
        private InputAction dash;

        void Awake()
        {
            playerInput = new InputSystem_Actions();
            move = playerInput.Player.Move;
            aim = playerInput.Player.Aim;
            jump = playerInput.Player.Jump;
            dash = playerInput.Player.Dash;
        }

        void OnEnable()
        {
            move.Enable();
            aim.Enable();
            jump.Enable();
            dash.Enable();
        }

        void OnDisable()
        {
            move.Disable();
            aim.Disable();
            jump.Disable();
            dash.Disable();
        }

        void OnDestroy()
        {
            playerInput?.Dispose();
        }

        void FixedUpdate()
        {
            SampleInputSource.Move = move.ReadValue<Vector2>();
            SampleInputSource.Aim = aim.ReadValue<Vector2>();
            SampleInputSource.JumpPressed = jump.WasPressedThisFrame();
            SampleInputSource.JumpHeld = jump.IsPressed();
            SampleInputSource.DashPressed = dash.WasPressedThisFrame();
        }
    }
}
