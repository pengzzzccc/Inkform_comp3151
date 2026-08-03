using UnityEngine;
using UnityEngine.InputSystem;
using Inkform.Player;

namespace Inkform.Input
{
    /// <summary>
    /// 输入层：把 InputSystem_Actions 资产里的动作转发给 PlayerHandler。
    ///
    /// 键位只有一个来源 —— InputSystem_Actions.inputactions 资产（改键去那儿改，
    /// Unity 会重新生成 InputSystem_Actions.cs）。这里不再运行时补动作、也不改绑定。
    /// </summary>
    public class InputHandler : MonoBehaviour
    {
        private InputSystem_Actions playerInput;

        // Player map 里本作实际用到的六个动作，全部直接取自生成的 wrapper
        private InputAction move;
        private InputAction aim;
        private InputAction jump;
        private InputAction dash;
        private InputAction ropeFire;
        private InputAction spitBomb;

        // Get player
        [SerializeField] private PlayerHandler player;

        // 键盘是离散的 0/±1，这里把它按按压时长合成出模拟摇杆的力度：
        // 按住越久越接近满力（rampUpTime），松开后回中（rampDownTime），
        // 输出前再过一条力度曲线（起步轻柔、末端干脆，像推手柄摇杆）。
        // 手柄等已带模拟量的设备（摇杆）原样透传，不做合成。
        [Header("Keyboard -> stick")]
        [SerializeField] private float rampUpTime = 0.15f;                 // 按多久到满力
        [SerializeField] private float rampDownTime = 0.1f;                // 松开后回中时长
        [SerializeField] private AnimationCurve stickCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        // 虚拟摇杆位置（-1..1）。反向拨动时 MoveTowards 天然经过 0，甩过中位的过渡不用特判
        private Vector2 stick;

        /// <summary>
        /// awake all input system and setting before the game life loop start.
        /// </summary>
        void Awake()
        {
            playerInput = new InputSystem_Actions();

            // 游戏开始隐藏系统光标：瞄准靠绳索枪准星（Aim 动作在锁定状态下照常工作）
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;

            // player input setup
            move = playerInput.Player.Move;
            aim = playerInput.Player.Aim;
            jump = playerInput.Player.Jump;
            dash = playerInput.Player.Dash;
            ropeFire = playerInput.Player.RopeFire;
            spitBomb = playerInput.Player.SpitBomb;

            // 场景实例覆盖里的引用在 Awake 之前就已接线；此时还是 null 就永远不会有了，
            // 与其等 Update 里每帧 NRE，不如现在就吭一声
            if (player == null)
                Debug.LogWarning($"InputHandler 的 player 未接线（GameManager 预制体的场景实例覆盖）", this);
        }

        void OnDestroy()
        {
            // 生成的 wrapper 持有一份 InputActionAsset，实现了 IDisposable —— 不释放会漏
            playerInput?.Dispose();
        }

        void Update()
        {
            if (player == null) return;

            Vector2 raw = move.ReadValue<Vector2>();

            // 非键盘设备（手柄摇杆）输入本身就是模拟量，直接透传，不经过键盘合成
            InputControl control = move.activeControl;
            if (control != null && !(control.device is Keyboard))
            {
                player.Move(raw);
            }
            else
            {
                // 键盘：两轴各自按按压时长爬升 / 松开回中
                stick.x = RampAxis(stick.x, raw.x, Time.deltaTime);
                stick.y = RampAxis(stick.y, raw.y, Time.deltaTime);

                player.Move(new Vector2(
                    Mathf.Sign(stick.x) * stickCurve.Evaluate(Mathf.Abs(stick.x)),
                    Mathf.Sign(stick.y) * stickCurve.Evaluate(Mathf.Abs(stick.y))));
            }

            // 瞄准：Aim 动作（鼠标 delta / 右摇杆）。鼠标 delta 单位是像素，摇杆是模拟量，
            // 用 pixelDelta 标志区分，换算在 RopeGun 里做
            InputControl aimControl = aim.activeControl;
            Vector2 aimValue = aim.ReadValue<Vector2>();
            player.Aim(aimValue, aimControl != null && aimControl.device is Mouse);
        }

        // 单轴摇杆合成：朝目标（0 或 ±1）以对应速率匀速移动。
        // MoveTowards 一步到位得设大速率 —— 这里速率 = 1/时长，满力恰好 rampUpTime 秒到达
        private float RampAxis(float current, float target, float dt)
        {
            float rate = Mathf.Approximately(target, 0f)
                ? (rampDownTime > 0f ? 1f / rampDownTime : 1f)
                : (rampUpTime > 0f ? 1f / rampUpTime : 1f);
            return Mathf.MoveTowards(current, target, rate * dt);
        }

        private void OnJumpPressed(InputAction.CallbackContext ctx)
        {
            player?.JumpPressed();
        }

        private void OnJumpReleased(InputAction.CallbackContext ctx)
        {
            player?.JumpReleased();
        }

        private void OnDash(InputAction.CallbackContext ctx)
        {
            player?.Dash();
        }

        private void OnRopeFire(InputAction.CallbackContext ctx)
        {
            player?.RopeFire();
        }

        private void OnSpitBomb(InputAction.CallbackContext ctx)
        {
            player?.SpitBomb();
        }

        void OnEnable()
        {
            // 逐个启用而不是 playerInput.Player.Enable()：map 里还留着 Interact / Crouch /
            // Previous / Next 四个本作没用上的动作，整 map 启用会把它们一起点亮
            move.Enable();
            aim.Enable();
            jump.Enable();
            dash.Enable();
            ropeFire.Enable();
            spitBomb.Enable();

            jump.performed += OnJumpPressed;
            jump.canceled += OnJumpReleased;
            dash.performed += OnDash;
            ropeFire.performed += OnRopeFire;
            spitBomb.performed += OnSpitBomb;
        }

        void OnDisable()
        {
            move.Disable();
            aim.Disable();
            jump.Disable();
            dash.Disable();
            ropeFire.Disable();
            spitBomb.Disable();

            jump.performed -= OnJumpPressed;
            jump.canceled -= OnJumpReleased;
            dash.performed -= OnDash;
            ropeFire.performed -= OnRopeFire;
            spitBomb.performed -= OnSpitBomb;
        }
    }
}
