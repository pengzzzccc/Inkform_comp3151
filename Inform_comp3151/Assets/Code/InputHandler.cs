using System;
using UnityEngine;
using UnityEngine.InputSystem;
using Inkform.player;

namespace Inkform.Input
{
    public class InputHandler : MonoBehaviour
    {
        private InputSystem_Actions playerInput;

        // player inputAction init
        private InputAction movment;
        private InputAction jump;
        private InputAction attack;
        private InputAction interact;

        // UI inputAction init
        private InputAction submit;
        private InputAction cancel;
        private InputAction navigate;
        private InputAction click;

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
            interact = playerInput.Player.Interact;

            // UI input setup
            submit = playerInput.UI.Submit;
            cancel = playerInput.UI.Cancel;
            navigate = playerInput.UI.Navigate;
            click = playerInput.UI.Click;

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

        void OnEnable()
        {
            // enable player input.
            movment.Enable();
            jump.Enable();
            attack.Enable();
            interact.Enable();

            // enable UI input.
            submit.Enable();
            cancel.Enable();
            navigate.Enable();
            click.Enable();

            // jump input callback
            jump.performed += OnJumpPerformed;
            jump.canceled += OnJumpCancel;

        }

        void OnDisable()
        {
            // disable player input.
            movment.Disable();
            jump.Disable();
            attack.Disable();
            interact.Disable();

            // enable UI input.
            submit.Disable();
            cancel.Disable();
            navigate.Disable();
            click.Disable();

            // jump input callback
            jump.performed -= OnJumpPerformed;
            jump.canceled -= OnJumpCancel;

        }
    }
}
