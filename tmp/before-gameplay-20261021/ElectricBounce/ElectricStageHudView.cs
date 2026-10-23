using TMPro;
using UnityEngine;

namespace sung_wook
{
    /// <summary>공통 화면 UI의 저장된 배치를 사용하고 현재 씬의 학습 상태만 표시한다.</summary>
    [DisallowMultipleComponent]
    public sealed class ElectricStageHudView : MonoBehaviour
    {
        [SerializeField] private TMP_Text stageNumberText;
        [SerializeField] private TMP_Text stageNameText;
        [SerializeField] private TMP_Text questText;
        [SerializeField] private TMP_Text controlsText;
        [SerializeField] private TMP_Text objectiveBodyText;

        private ElectricStageSelector selector;

        public static string StageName(int stage)
        {
            switch (stage)
            {
                case 3: return "전기력 비교 · 움직이는 발판";
                case 4: return "평행판 전기장";
                case 5: return "반데그라프 · 전압 발사";
                default: return "접촉 대전";
            }
        }

        public static string Instructions(int stage)
        {
            switch (stage)
            {
                case 3: return "B 기준 저장 / 레버로 거리 조절 / E 비교 발사 / Z·X·C 공 전하량 / V·N 전극 전하량 / T 재준비";
                case 4: return "Space로 음전하 선택 / 레버로 전기장 방향 조절 / F = qE";
                case 5: return "레버를 올려 50 V 이상 충전 / 발사 영역에서 E / qV = 0.5mv^2";
                default: return "레버 중앙은 중성, 양쪽은 +/− 전하입니다. 당긴 만큼 전극이 이동·충전되며 접촉한 공에 대전됩니다.";
            }
        }

        public void PreviewStage(int stage)
        {
            if (stageNumberText != null) stageNumberText.text = stage.ToString();
            if (stageNameText != null) stageNameText.text = StageName(stage);
            if (controlsText != null)
                controlsText.text = "A/D 이동   Space 전하 전환   마우스 레버   R 재시작   "
                    + (stage == 3 ? "B 기준 / E 발사 / Z·X·C q / V·N Q / T 준비"
                        : stage == 5 ? "E 발사" : stage == 4 ? "Z 양전하 / X 음전하 / C 중성" : "");
            if (objectiveBodyText != null) objectiveBodyText.text = Instructions(stage);
            if (questText != null) questText.text = "학습 진행   별 0/1";
        }

        private void Start()
        {
            foreach (GameObject root in gameObject.scene.GetRootGameObjects())
            {
                ElectricStageSelector candidate = root.GetComponentInChildren<ElectricStageSelector>();
                if (candidate == null) continue;
                selector = candidate;
                break;
            }
            if (selector != null) PreviewStage(selector.SelectedStage);
        }

        private void LateUpdate()
        {
            if (selector == null || selector.Ball == null) return;
            ElectricLearningController2D lesson = selector.GetComponent<ElectricLearningController2D>();
            if (lesson == null) return;
            if (questText != null)
            {
                string charge = selector.Ball.CurrentPolarity == Polarity.Positive ? "+"
                    : selector.Ball.CurrentPolarity == Polarity.Negative ? "-" : "0";
                string progress = lesson.IsStageCleared ? "클리어"
                    : lesson.CurrentLearningGoalSatisfied && selector.Ball.Stars > 0 ? "출구 준비"
                    : "학습 " + (lesson.CurrentLearningGoalSatisfied ? "완료" : "진행");
                questText.text = progress + "   별 " + selector.Ball.Stars + "/1   전하 " + charge
                    + "\n" + Measurement(lesson);
            }
            if (objectiveBodyText != null)
                objectiveBodyText.text = lesson.LearningGoalSummary + "\n\n"
                    + Instructions(selector.SelectedStage) + "\n\n" + lesson.LearningGoalProgress;
        }

        private string Measurement(ElectricLearningController2D lesson)
        {
            if (selector.SelectedStage == 5)
                return $"현재 {lesson.CurrentVoltage:0.0} V / 목표 50 V   마지막 발사 {lesson.LastLaunchVoltage:0.0} V";
            if (!selector.TryGetCurrentForceSample(out ElectricForceSample sample)) return "실험 준비";
            if (sample.UsesSIUnits)
                return $"r={sample.DistanceMeters:0.000} m   F={sample.ForceNewtons.magnitude:0.000} N"
                    + $"   q={sample.SignedChargeCoulombs * 1e6f:+0.0;−0.0;0} μC"
                    + $"   Q={sample.SourceChargeCoulombs * 1e6f:+0.0;−0.0;0} μC"
                    + (sample.IsLimited ? "   근접 안전 제한" : "");
            if (selector.SelectedStage == 4)
                return $"E={sample.Field.y:0.0} ({Direction(sample.Field.y)})   F={sample.Force.y:0.0} ({Direction(sample.Force.y)}) · 게임 단위";
            if (selector.SelectedStage == 3)
            {
                string charges = $"q={selector.Ball.SignedCharge:0.0}  Q={sample.SourceCharge:0.0}";
                if (lesson.HasResult)
                    return $"r {lesson.BaselineSample.Distance:0.00} → {lesson.ResultSample.Distance:0.00}   F {lesson.BaselineSample.ForceMagnitude:0.0} → {lesson.ResultSample.ForceMagnitude:0.0}   {charges}";
                if (lesson.HasBaseline)
                    return $"기준 F={lesson.BaselineSample.ForceMagnitude:0.0}   현재 r={sample.Distance:0.00} F={sample.ForceMagnitude:0.0}   {charges}";
                return $"r={sample.Distance:0.00}   F={sample.ForceMagnitude:0.0}   {charges} · 게임 단위";
            }
            return $"r={sample.Distance:0.00}   F={sample.ForceMagnitude:0.0} · 게임 단위";
        }

        private static string Direction(float value) => value > 0.001f ? "위" : value < -0.001f ? "아래" : "0";
    }
}
