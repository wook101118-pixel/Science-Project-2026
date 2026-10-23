using UnityEngine;
using UnityEngine.InputSystem;

namespace sung_wook
{
    /// <summary>이동과 상태 변경 입력을 읽으며 직접 이동하거나 플레이어 상태를 변경하지 않는다.</summary>
    public class PlayerInputReader : MonoBehaviour, Controls.IPlayerActions
    {
        private Controls controls;
        public Vector2 MoveVector { get; private set; }
        // 누르는 순간만 전달하므로 Space를 계속 눌러도 상태가 반복해서 바뀌지 않는다.
        public bool StateChangePressed => isActiveAndEnabled && Keyboard.current != null
            && Keyboard.current.spaceKey.wasPressedThisFrame;

        private void OnEnable()
        {
            if (controls == null)
            {
                // 재활성화 때 콜백을 다시 구독하지 않도록 입력 객체를 한 번만 만든다.
                controls = new Controls();
                controls.Player.SetCallbacks(this);
            }
            controls.Enable();
        }

        private void OnDisable()
        {
            controls?.Disable();
            // 비활성화 직전의 입력이 다음 활성화까지 남지 않게 한다.
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
