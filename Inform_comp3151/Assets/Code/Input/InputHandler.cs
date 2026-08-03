using UnityEngine;
using UnityEngine.InputSystem;
using Inkform.Player;

namespace Inkform.Input
{
    public class InputHandler : MonoBehaviour
    {
        private InputSystem_Actions playerInput;
        private InputActionMap playerMap;   // 运行时补动作 / 改绑定的入口（Player map 的原生对象）

        // player inputAction init
        private InputAction movement;
        private InputAction look;
        private InputAction jump;
        private InputAction attack;
        private InputAction ropeFire;
        private InputAction ropeToggle;
        private InputAction spit;

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
            playerMap = playerInput.asset.FindActionMap("Player", false);

            // player input setup
            movement = playerInput.Player.Move;
            look = playerInput.Player.Look;
            jump = playerInput.Player.Jump;
            attack = playerInput.Player.Attack;
            ropeFire = FindOrCreate("RopeFire",
                "<Mouse>/leftButton", "<Gamepad>/rightShoulder");
            ropeToggle = FindOrCreate("RopeToggle",
                "<Mouse>/rightButton", "<Gamepad>/leftShoulder");
            spit = FindOrCreate("Spit",
                "<Keyboard>/q", "<Gamepad>/rightTrigger");

            // 新资产生效时（wrapper 已按 .inputactions 重新生成）Attack 已经绑 leftShift；
            // 还没生效时旧绑定还在（LMB/Enter），运行时修成一致 —— 保证两种状态下键位相同
            RebindDash(attack);

            // 场景实例覆盖里的引用在 Awake 之前就已接线；此时还是 null 就永远不会有了，
            // 与其等 Update 里每帧 NRE，不如现在就吭一声
            if (player == null)
                Debug.LogWarning($"InputHandler 的 player 未接线（GameManager 预制体的场景实例覆盖）", this);
        }

        // 从 Player map 里按名字取动作；资产里还没有（wrapper 未重新生成 / 编辑器未保存）时，
        // 运行时补进 map —— 键位与 InputSystem_Actions.inputactions 资产保持一致
        private InputAction FindOrCreate(string name, params string[] bindingPaths)
        {
            InputAction action = playerMap != null ? playerMap.FindAction(name, false) : null;
            if (action != null) return action;

            action = playerMap.AddAction(name, InputActionType.Button);
            foreach (string p in bindingPaths) action.AddBinding(p);
            return action;
        }

        // dash 键位：Attack 应绑 leftShift 而不是 LMB/Enter。新资产生效时什么都不做；
        // 旧资产（wrapper 未重新生成）时把 LMB/Enter 用空路径覆盖掉（InputSystem 没有
        // 运行时删绑定的 API，空路径 = 匹配不到任何控制 = 禁用），再补上 leftShift。
        private void RebindDash(InputAction action)
        {
            for (int i = 0; i < action.bindings.Count; i++)
            {
                if (action.bindings[i].path == "<Keyboard>/leftShift") return;   // 新绑定已在
            }

            for (int i = 0; i < action.bindings.Count; i++)
            {
                string p = action.bindings[i].path;
                if (p == "<Mouse>/leftButton" || p == "<Keyboard>/enter")
                    action.ApplyBindingOverride(i, "");
            }
            action.AddBinding("<Keyboard>/leftShift");
        }

        void Update()
        {
            if (player == null) return;

            Vector2 raw = movement.ReadValue<Vector2>();

            // 非键盘设备（手柄摇杆）输入本身就是模拟量，直接透传，不经过键盘合成
            InputControl control = movement.activeControl;
            if (control != null && !(control.device is Keyboard))
            {
                player.playerMoving(raw);
            }
            else
            {
                // 键盘：两轴各自按按压时长爬升 / 松开回中
                stick.x = RampAxis(stick.x, raw.x, Time.deltaTime);
                stick.y = RampAxis(stick.y, raw.y, Time.deltaTime);

                player.playerMoving(new Vector2(
                    Mathf.Sign(stick.x) * stickCurve.Evaluate(Mathf.Abs(stick.x)),
                    Mathf.Sign(stick.y) * stickCurve.Evaluate(Mathf.Abs(stick.y))));
            }

            // 瞄准：Look 动作（鼠标 delta / 右摇杆）。鼠标 delta 单位是像素，摇杆是模拟量，
            // 用 pixelDelta 标志区分，换算在 RopeGun 里做
            InputControl aimControl = look.activeControl;
            Vector2 lookValue = look.ReadValue<Vector2>();
            player.AimLook(lookValue, aimControl != null && aimControl.device is Mouse);
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

        private void OnJumpPerformed(InputAction.CallbackContext ctx)
        {
            player?.RequestJump();
        }

        private void OnJumpCancel(InputAction.CallbackContext ctx)
        {
            player?.playerFalling();
        }

        private void OnAttackPerformed(InputAction.CallbackContext ctx)
        {
            player?.playerAttack();
        }

        private void OnRopeFire(InputAction.CallbackContext ctx)
        {
            player?.RopeFire();
        }

        private void OnRopeToggle(InputAction.CallbackContext ctx)
        {
            player?.RopeToggle();
        }

        private void OnSpit(InputAction.CallbackContext ctx)
        {
            player?.SpitBomb();
        }

        void OnEnable()
        {
            // enable player input.
            movement.Enable();
            look.Enable();
            jump.Enable();
            attack.Enable();
            ropeFire.Enable();
            ropeToggle.Enable();
            spit.Enable();

            // jump input callback
            jump.performed += OnJumpPerformed;
            jump.canceled += OnJumpCancel;
            attack.performed += OnAttackPerformed;
            ropeFire.performed += OnRopeFire;
            ropeToggle.performed += OnRopeToggle;
            spit.performed += OnSpit;

        }

        void OnDisable()
        {
            // disable player input.
            movement.Disable();
            look.Disable();
            jump.Disable();
            attack.Disable();
            ropeFire.Disable();
            ropeToggle.Disable();
            spit.Disable();

            // jump input callback
            jump.performed -= OnJumpPerformed;
            jump.canceled -= OnJumpCancel;
            attack.performed -= OnAttackPerformed;
            ropeFire.performed -= OnRopeFire;
            ropeToggle.performed -= OnRopeToggle;
            spit.performed -= OnSpit;

        }
    }
}
