using UnityEngine;
using UnityEngine.InputSystem;
using Inkform.Player;

namespace Inkform.Input
{
    public class InputHandler : MonoBehaviour
    {
        private InputSystem_Actions playerInput;

        // player inputAction init
        private InputAction movment;
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
            movment = playerInput.Player.Move;
            jump = playerInput.Player.Jump;
            attack = playerInput.Player.Attack;
        }

        void Update()
        {
            // call playermove
            Vector2 moveInput = movment.ReadValue<Vector2>();
            player.playerMoving(moveInput);
        }

        private void OnJumpPerformed(InputAction.CallbackContext ctx)
        {
            player.RequestJump();
        }

        private void OnJumpCancel(InputAction.CallbackContext ctx)
        {
            player.playerFalling();
        }

        private void OnAttackPreformed(InputAction.CallbackContext ctx)
        {
            player.playerAttack();
        }

        void OnEnable()
        {
            // enable player input.
            movment.Enable();
            jump.Enable();
            attack.Enable();

            // jump input callback
            jump.performed += OnJumpPerformed;
            jump.canceled += OnJumpCancel;
            attack.performed += OnAttackPreformed;

        }

        void OnDisable()
        {
            // disable player input.
            movment.Disable();
            jump.Disable();
            attack.Disable();

            // jump input callback
            jump.performed -= OnJumpPerformed;
            jump.canceled -= OnJumpCancel;
            attack.performed -= OnAttackPreformed;

        }
    }
}
