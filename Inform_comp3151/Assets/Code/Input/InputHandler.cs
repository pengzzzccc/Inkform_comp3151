using UnityEngine;
using UnityEngine.InputSystem;
using Inkform.Player;

namespace Inkform.Input
{
    public class InputHandler : MonoBehaviour
    {
        private InputSystem_Actions playerInput;

        // player inputAction init
        private InputAction movement;
        private InputAction jump;
        private InputAction attack;

        // Get player
        [SerializeField] private PlayerHandler player;

        /// <summary>
        /// awake all input system and setting before the game life loop start.
        /// </summary>
        void Awake()
        {
            playerInput = new InputSystem_Actions();

            // player input setup
            movement = playerInput.Player.Move;
            jump = playerInput.Player.Jump;
            attack = playerInput.Player.Attack;

            // 场景实例覆盖里的引用在 Awake 之前就已接线；此时还是 null 就永远不会有了，
            // 与其等 Update 里每帧 NRE，不如现在就吭一声
            if (player == null)
                Debug.LogWarning($"InputHandler 的 player 未接线（GameManager 预制体的场景实例覆盖）", this);
        }

        void Update()
        {
            if (player == null) return;

            // call playermove
            Vector2 moveInput = movement.ReadValue<Vector2>();
            player.playerMoving(moveInput);
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

        void OnEnable()
        {
            // enable player input.
            movement.Enable();
            jump.Enable();
            attack.Enable();

            // jump input callback
            jump.performed += OnJumpPerformed;
            jump.canceled += OnJumpCancel;
            attack.performed += OnAttackPerformed;

        }

        void OnDisable()
        {
            // disable player input.
            movement.Disable();
            jump.Disable();
            attack.Disable();

            // jump input callback
            jump.performed -= OnJumpPerformed;
            jump.canceled -= OnJumpCancel;
            attack.performed -= OnAttackPerformed;

        }
    }
}
