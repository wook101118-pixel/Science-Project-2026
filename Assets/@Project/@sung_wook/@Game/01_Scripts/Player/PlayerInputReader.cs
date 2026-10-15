using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace sung_wook
{
    public class PlayerInputReader : MonoBehaviour, Controls.IPlayerActions
    {
        private Controls controls;
        public Vector2 MoveVector {get; private set;}

        private void OnEnable()
        {
            controls = new Controls();
            controls.Player.SetCallbacks(this);
            controls.Enable();
        }

        private void OnDisable()
        {
            controls.Disable();
        }

        public void OnMove(InputAction.CallbackContext context)
        {
            MoveVector = context.ReadValue<Vector2>();
        }
        
    }   
}