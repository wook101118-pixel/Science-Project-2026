using UnityEngine;
using UnityEngine.InputSystem;

namespace sung_wook
{
    /// <summary>
    /// 화면을 생성하지 않고 학습 실험의 키보드 명령만 전달합니다.
    /// 이동, 스테이지 선택, 재시작, 발사, 레버 입력은 기존 담당 컴포넌트가 처리합니다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ElectricLearningInput2D : MonoBehaviour
    {
        [SerializeField] private ElectricLearningController2D controller;

        /// <summary>선택기가 연결한 학습 컨트롤러를 사용합니다. 이벤트를 추가하지 않습니다.</summary>
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

            // 화면을 그리지 않고 요청할 때만 관찰 기록을 Console에 남깁니다.
            if (keyboard.lKey.wasPressedThisFrame)
                LogObservation();
        }

        /// <summary>
        /// T로 같은 출발점에 복귀하고 B로 기준을 저장합니다.
        /// 전하량은 공이 고정된 준비 상태에서만 변경하여 비교 조건을 유지합니다.
        /// </summary>
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

            // Z/X/C: 공의 전하량 q = +0.5 / +1 / +2.
            if (keyboard.zKey.wasPressedThisFrame)
                controller.SetBallCharge(0.5f);
            else if (keyboard.xKey.wasPressedThisFrame)
                controller.SetBallCharge(1f);
            else if (keyboard.cKey.wasPressedThisFrame)
                controller.SetBallCharge(2f);

            // V/N: 고정 발사 전극의 전하량 크기 Q = 0.5 / 1 (레버의 양 끝과 같음).
            if (keyboard.vKey.wasPressedThisFrame)
                controller.SetSourceChargeMagnitude(0.5f);
            else if (keyboard.nKey.wasPressedThisFrame)
                controller.SetSourceChargeMagnitude(1f);
        }

        /// <summary>Z/X/C로 제자리에서 양전하, 음전하, 중성을 선택한다. 재시작은 R이 담당한다.</summary>
        private void ReadPolarityCommands(Keyboard keyboard)
        {
            if (keyboard.zKey.wasPressedThisFrame)
                controller.SetBallCharge(1f);
            else if (keyboard.xKey.wasPressedThisFrame)
                controller.SetBallCharge(-1f);
            else if (keyboard.cKey.wasPressedThisFrame)
                controller.SetBallCharge(0f);
        }

        /// <summary>현재 물리 표본과 저장된 학습 결과를 읽기만 합니다. 힘이나 입력 상태를 바꾸지 않습니다.</summary>
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

        /// <summary>전기장과 힘의 크기·방향을 표시하며 SI 전극은 실제 단위를 사용합니다.</summary>
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
