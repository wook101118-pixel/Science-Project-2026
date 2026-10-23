using UnityEngine;
using UnityEngine.InputSystem;

namespace sung_wook
{
                 
                                         
                                                       
                  
    [DisallowMultipleComponent]
    public sealed class ElectricLearningInput2D : MonoBehaviour
    {
        [SerializeField] private ElectricLearningController2D controller;

                                                                       
        public void Configure(ElectricLearningController2D owner)
        {
            controller = owner;
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (controller == null || !controller.isActiveAndEnabled || keyboard == null)
                return;

            switch (controller.SelectedStage)
            {
                case 3:
                    ReadComparisonCommands(keyboard);
                    break;
                case 4:
                    ReadPolarityCommands(keyboard);
                    break;
            }

                                                      
            if (keyboard.lKey.wasPressedThisFrame)
                LogObservation();
        }

                     
                                                         
                                                   
                      
        private void ReadComparisonCommands(Keyboard keyboard)
        {
            if (keyboard.tKey.wasPressedThisFrame)
                controller.PrepareTrial();
            if (keyboard.bKey.wasPressedThisFrame)
                controller.RecordBaseline();

            if (!controller.IsPreparingTrial)
            {
                if (keyboard.zKey.wasPressedThisFrame || keyboard.xKey.wasPressedThisFrame || keyboard.cKey.wasPressedThisFrame
                    || keyboard.vKey.wasPressedThisFrame || keyboard.nKey.wasPressedThisFrame)
                    controller.ReportComparisonPreparationRequired();
                return;
            }

                                                
            if (keyboard.zKey.wasPressedThisFrame)
                controller.SetBallCharge(0.5f);
            else if (keyboard.xKey.wasPressedThisFrame)
                controller.SetBallCharge(1f);
            else if (keyboard.cKey.wasPressedThisFrame)
                controller.SetBallCharge(2f);

                                                               
            if (keyboard.vKey.wasPressedThisFrame)
                controller.SetSourceChargeMagnitude(0.5f);
            else if (keyboard.nKey.wasPressedThisFrame)
                controller.SetSourceChargeMagnitude(1f);
        }

                                                                             
        private void ReadPolarityCommands(Keyboard keyboard)
        {
            if (keyboard.zKey.wasPressedThisFrame)
                controller.SetBallCharge(1f);
            else if (keyboard.xKey.wasPressedThisFrame)
                controller.SetBallCharge(-1f);
            else if (keyboard.cKey.wasPressedThisFrame)
                controller.SetBallCharge(0f);
        }

                                                                                 
        private void LogObservation()
        {
            string observation = $"[전기 학습 · {controller.SelectedStage}번] {controller.LearningGoalProgress}";
            if (controller.SelectedStage == 5)
                observation += $"\n현재 전압: {controller.CurrentVoltage:0.0} V / 마지막 발사 전압: {controller.LastLaunchVoltage:0.0} V";
            if (controller.TryGetCurrentSample(out ElectricForceSample sample))
                observation += "\n현재: " + DescribeSample(sample);
            if (controller.HasBaseline)
                observation += "\n저장한 기준: " + DescribeSample(controller.BaselineSample);
            if (controller.HasResult)
                observation += "\n발사 결과: " + DescribeSample(controller.ResultSample);
            observation += "\n설명: " + controller.LastExplanation;

            Debug.Log(observation, controller);
        }

                                                                       
        private static string DescribeSample(ElectricForceSample sample)
        {
            if (sample.UsesSIUnits)
                return $"r={sample.DistanceMeters:0.###} m, q={sample.SignedChargeCoulombs * 1e6f:0.###} μC, "
                    + $"Q={sample.SourceChargeCoulombs * 1e6f:0.###} μC, E={sample.FieldNewtonsPerCoulomb} N/C, "
                    + $"F={sample.ForceNewtons} N, |F|={sample.ForceNewtons.magnitude:0.###} N "
                    + $"(근접 안전 제한: {sample.IsLimited})";
            return $"r={sample.Distance:0.###}, q={sample.SignedCharge:0.###}, Q={sample.SourceCharge:0.###}, "
                + $"E=({sample.Field.x:0.###}, {sample.Field.y:0.###}), |E|={sample.FieldMagnitude:0.###}, "
                + $"F=({sample.Force.x:0.###}, {sample.Force.y:0.###}), |F|={sample.ForceMagnitude:0.###} "
                + $"(게임 단위, 적용 범위: {sample.IsInRange}, 안전 제한: {sample.IsLimited})";
        }
    }
}
