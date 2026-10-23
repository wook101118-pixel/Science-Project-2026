using UnityEngine;
using UnityEngine.InputSystem;

namespace sung_wook
{
                                                                        
    public class PlayerInputReader : MonoBehaviour, Controls.IPlayerActions
    {
        private Controls controls;
        public Vector2 MoveVector { get; private set; }
                                                        
        public bool StateChangePressed => isActiveAndEnabled && Keyboard.current != null
            && Keyboard.current.spaceKey.wasPressedThisFrame;

        private void OnEnable()
        {
            if (controls == null)
            {
                                                          
                controls = new Controls();
                controls.Player.SetCallbacks(this);
            }
            controls.Enable();
        }

        private void OnDisable()
        {
            controls?.Disable();
                                              
            MoveVector = Vector2.zero;
        }

        private void OnDestroy()
        {
            controls?.Dispose();
        }

        public void OnMove(InputAction.CallbackContext context)
        {
            MoveVector = context.ReadValue<Vector2>();
        }
    }
}
