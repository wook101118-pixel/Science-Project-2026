using UnityEngine;
using UnityEngine.InputSystem;

namespace sung_wook
{
    /// <summary>스테이지 단위 키보드 명령만 전달한다. 이동·Space·레버·실험 조작은 각 입력 담당자가 유지한다.</summary>
    [DisallowMultipleComponent]
    public sealed class ElectricStageInput2D : MonoBehaviour
    {
        [SerializeField]
        private ElectricStageSelector selector;

        public void Configure(ElectricStageSelector owner)
        {
            selector = owner;
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || selector == null || !selector.isActiveAndEnabled)
                return;

            // 추후 한 씬 안의 스테이지 선택을 다시 사용할 수 있도록 기존 입력을 보관한다.
            // 현재 Stage_02~Stage_05는 각각의 씬으로 실행하므로 숫자키 선택은 사용하지 않는다.
            // if (keyboard.digit2Key.wasPressedThisFrame || keyboard.numpad2Key.wasPressedThisFrame) selector.SelectStage(2);
            // else if (keyboard.digit3Key.wasPressedThisFrame || keyboard.numpad3Key.wasPressedThisFrame) selector.SelectStage(3);
            // else if (keyboard.digit4Key.wasPressedThisFrame || keyboard.numpad4Key.wasPressedThisFrame) selector.SelectStage(4);
            // else if (keyboard.digit5Key.wasPressedThisFrame || keyboard.numpad5Key.wasPressedThisFrame) selector.SelectStage(5);

            if (keyboard.rKey.wasPressedThisFrame)
                selector.RestartStage();
            else if (keyboard.backspaceKey.wasPressedThisFrame)
                selector.RetryCheckpoint();
        }
    }
}
