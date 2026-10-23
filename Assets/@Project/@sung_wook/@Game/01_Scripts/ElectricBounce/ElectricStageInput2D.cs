using UnityEngine;
using UnityEngine.InputSystem;

namespace sung_wook
{
                                                                                   
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

                                                           
                                                                    
                                                                                                                              
                                                                                                                                   
                                                                                                                                   
                                                                                                                                   

            if (keyboard.rKey.wasPressedThisFrame)
                selector.RestartStage();
            else if (keyboard.backspaceKey.wasPressedThisFrame)
                selector.RetryCheckpoint();
        }
    }
}
