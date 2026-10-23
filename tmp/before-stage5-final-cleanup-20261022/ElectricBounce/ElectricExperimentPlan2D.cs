using UnityEngine;

namespace sung_wook
{
    /// <summary>기존 장치의 두 풀이를 선택하고 실제 행동으로 달성했는지 관찰한다. 물리와 출구 조건은 바꾸지 않는다.</summary>
    [DisallowMultipleComponent]
    public sealed class ElectricExperimentPlan2D : MonoBehaviour
    {
        private ElectricStageSelector selector;
        private ElectricLearningController2D lesson;
        private ElectricStageSession2D session;
        private ElectricGoal2D goal;
        private int selectedPlan, previousRetries;
        private bool recorded, hasPreviousPosition;
        private Vector2 previousPosition;
        private float neutralTravel, chargedTravel;

        public int SelectedPlan => selectedPlan;
        public bool ShowPlans => selector != null && (selector.SelectedStage == 2
            || selector.SelectedStage == 3 || selector.SelectedStage == 5);
        public bool CanSelect => ShowPlans && !recorded && goal != null && !goal.IsCleared
            && !SceneTransitionController.IsBusy && (selector.SelectedStage != 3 || !lesson.HasResult)
            && (selector.SelectedStage != 5 || !lesson.VoltageChallengeSatisfied);
        public float RecommendedVoltage => selectedPlan == 0 ? 50f : 55f;
        public int CompletedPlans => ShowPlans ? PlayerPrefs.GetInt(Key, 0) & 3 : 0;
        public string ResultSummary { get; private set; } = "";
        private string Key => "ElectricBounce.Plan.Stage" + selector.SelectedStage
            + (selector.SelectedStage == 3 ? ".FixedCharges.Flags" : ".Flags");

        public string PlanName(int plan)
        {
            if (selector == null) return "";
            switch (selector.SelectedStage)
            {
                case 2: return plan == 0 ? "중성으로 안전 이동" : "전하 유지 이동";
                case 3: return plan == 0 ? "전극 Q만 바꿔 비교" : "공 q만 바꿔 비교";
                case 5: return plan == 0 ? "50 V 효율 발사" : "55 V 여유 발사";
                default: return "";
            }
        }

        private string RecordName(int plan) => selector.SelectedStage == 2 ? (plan == 0 ? "중성" : "전하 유지")
            : selector.SelectedStage == 3 ? (plan == 0 ? "전극 Q" : "공 q") : plan == 0 ? "50 V" : "55 V";

        public string DetailText
        {
            get
            {
                if (!ShowPlans) return "";
                int flags = CompletedPlans;
                string progress = PlanSatisfied ? "달성 · 출구에서 기록"
                    : selector.SelectedStage == 2
                        ? $"이동 {Mathf.Min(2f, selectedPlan == 0 ? neutralTravel : chargedTravel):0.0}/2"
                        : selector.SelectedStage == 5 ? $"발사 {lesson.LastLaunchVoltage:0.0}/{RecommendedVoltage:0} V" : "비교 준비";
                return "선택 풀이: " + PlanName(selectedPlan) + " · " + progress
                    + "\n완주: " + RecordName(0) + ((flags & 1) != 0 ? " 완료" : " 미완")
                    + " / " + RecordName(1) + ((flags & 2) != 0 ? " 완료" : " 미완");
            }
        }

        public string CurrentTaskOverride
        {
            get
            {
                if (!ShowPlans || lesson == null || selector.Ball == null || recorded
                    || goal == null || goal.IsCleared || selector.Ball.Stars > 0) return "";
                switch (selector.SelectedStage)
                {
                    case 2:
                        if (!lesson.ContactChallengeSatisfied) return "";
                        if (selectedPlan == 0)
                            return selector.Ball.CurrentPolarity == Polarity.Neutral
                                ? "중성 안전 이동 · 계단을 따라 별로 가세요."
                                : "안전 풀이 · 전극에서 떨어진 뒤 Space로 중성 → 계단";
                        return selector.Ball.CurrentPolarity == Polarity.Neutral
                            ? "다른 풀이 · Space로 전하를 유지하며 별로 이동하세요."
                            : "전하 유지 풀이 · A/D로 힘과 관성을 조절하며 별로 이동하세요.";
                    case 3:
                        if (!lesson.IsPreparingTrial || lesson.HasResult) return "";
                        if (!lesson.HasBaseline) return "";
                        if (lesson.TryGetCurrentSample(out ElectricForceSample sample)
                            && ElectricComparisonRecord.HaveConditionsChanged(lesson.BaselineSample, sample)) return "";
                        return selectedPlan == 0 ? "기준 자동 저장 · 레버로 Q를 높인 뒤 E로 발사"
                            : "공 q 기준 자동 저장 · 전극 Q 유지 → X로 q=1 → E";
                    case 5:
                        if (lesson.VoltageChallengeSatisfied || selector.Ball.SignedCharge <= 0f) return "";
                        return lesson.CurrentVoltage < RecommendedVoltage
                            ? $"선택 {RecommendedVoltage:0} V 충전 · 현재 {lesson.CurrentVoltage:0.0} V (필수 50 V)"
                            : $"선택 {RecommendedVoltage:0} V 준비 · 전극 사이에서 E, 발사 뒤 A/D 착지";
                    default: return "";
                }
            }
        }

        public bool PlanSatisfied
        {
            get
            {
                if (!ShowPlans || lesson == null) return false;
                switch (selector.SelectedStage)
                {
                    case 2: return lesson.ContactChallengeSatisfied && (selectedPlan == 0 ? neutralTravel : chargedTravel) >= 2f;
                    case 3:
                        if (!lesson.ComparisonChallengeSatisfied || !lesson.HasResult || !lesson.HasBaseline) return false;
                        ElectricForceSample a = lesson.QualifiedBaselineSample, b = lesson.QualifiedResultSample;
                        bool distance = Mathf.Abs(a.Distance - b.Distance) > 0.02f;
                        bool charge = Mathf.Abs(a.SignedCharge - b.SignedCharge) > 0.02f;
                        bool source = Mathf.Abs(a.SourceCharge - b.SourceCharge) > 0.02f;
                        return selectedPlan == 0 ? !distance && !charge && source : !distance && charge && !source;
                    case 5: return lesson.VoltageChallengeSatisfied && lesson.LastLaunchVoltage >= 50f
                        && Mathf.Abs(lesson.LastLaunchVoltage - RecommendedVoltage) <= 0.5f;
                    default: return false;
                }
            }
        }

        public void Configure(ElectricStageSelector owner)
        {
            if (goal != null) goal.OnCleared.RemoveListener(RecordClear);
            selector = owner;
            lesson = owner.GetComponent<ElectricLearningController2D>();
            session = owner.GetComponent<ElectricStageSession2D>();
            goal = owner.GetStageComponent<ElectricGoal2D>(owner.SelectedStage);
            if (goal != null) goal.OnCleared.AddListener(RecordClear);
        }

        public bool SelectPlan(int plan)
        {
            if (!CanSelect || plan < 0 || plan > 1) return false;
            if (selectedPlan == plan) return true;
            selectedPlan = plan;
            neutralTravel = chargedTravel = 0f;
            hasPreviousPosition = false;
            if (selector.SelectedStage == 3 && lesson.IsPreparingTrial)
            {
                if (plan == 0) lesson.ResetComparisonLeverToRest();
                lesson.SetBallCharge(plan == 0 ? 1f : 0.5f);
                lesson.SetSourceChargeMagnitude(plan == 0 ? 0.5f : 1f);
                lesson.RecordBaseline();
            }
            return true;
        }

        /// <summary>명시적인 새 비교 실험은 레버 하나를 사용하는 기본 풀이로 시작한다.</summary>
        internal void ResetComparisonPlan()
        {
            if (selector != null && selector.SelectedStage == 3) selectedPlan = 0;
        }

        public void ResetAttempt()
        {
            recorded = hasPreviousPosition = false;
            neutralTravel = chargedTravel = 0f;
            previousRetries = 0;
            ResultSummary = "";
        }

        private void FixedUpdate()
        {
            if (!ShowPlans || selector.SelectedStage != 2 || selector.Ball == null || recorded
                || !selector.Ball.CanReceiveForce || lesson == null || !lesson.ContactChallengeSatisfied)
            {
                hasPreviousPosition = false;
                return;
            }
            Vector2 position = selector.Ball.Body.position;
            int retries = session != null ? session.RetryCount : 0;
            if (hasPreviousPosition && retries == previousRetries)
            {
                float distance = Mathf.Abs(position.x - previousPosition.x);
                // 복귀나 외부 순간 이동은 실제 이동으로 세지 않는다.
                if (distance <= 20f * Time.fixedDeltaTime + 0.05f)
                {
                    if (selector.Ball.CurrentPolarity == Polarity.Neutral) neutralTravel += distance;
                    else chargedTravel += distance;
                }
            }
            previousPosition = position;
            previousRetries = retries;
            hasPreviousPosition = true;
        }

        public void RecordClear()
        {
            if (recorded || !ShowPlans || goal == null || !goal.IsCleared || lesson == null
                || selector.Ball == null || !lesson.CanClearGoal(selector.Ball)) return;
            recorded = true;
            if (!PlanSatisfied)
            {
                ResultSummary = "기본 완주 · 선택 풀이는 다시 연습할 수 있어요.";
                return;
            }
            int before = CompletedPlans, flag = 1 << selectedPlan;
            PlayerPrefs.SetInt(Key, before | flag);
            PlayerPrefs.Save();
            ResultSummary = "선택 풀이 달성 · " + PlanName(selectedPlan)
                + ((before & flag) == 0 ? " · 새 기록" : " · 재달성");
        }

        private void OnDestroy()
        {
            if (goal != null) goal.OnCleared.RemoveListener(RecordClear);
        }
    }
}
