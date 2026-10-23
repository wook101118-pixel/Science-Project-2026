using UnityEngine;

namespace sung_wook
{
                 
                                                     
                                                                           
                                                      
                  
    [DisallowMultipleComponent]
    public sealed class ElectricLearningController2D : MonoBehaviour, IElectricStageClearRule
    {
        [SerializeField] private ElectricStageSelector selector;

        private readonly ElectricComparisonRecord comparison = new ElectricComparisonRecord();
        private ContactCharger2D contactCharger;
        private DistanceLauncher2D distanceLauncher;
        private UniformField2D field;
        private VoltageLauncher2D voltageLauncher;
        private Rigidbody2D heldBody;
        private RigidbodyConstraints2D savedConstraints;
        private bool launchingPreparedTrial;
        private bool hasVoltageLaunchResult;
        private const float FeedbackSeconds = 3f;
        private string recentFeedback = "";
        private float recentFeedbackTime = float.NegativeInfinity;
        private Vector2 previousFieldPosition;
        private bool hasPreviousFieldPosition;
        private bool ascendingSegment;
        private bool neutralDescendingSegment;
        private float ascentDistance;
        private float neutralDescentDistance;
        private float negativeReturnSeconds;
        private ElectricForceSample lastDisplaySample;
        private bool hasDisplaySample;

        public bool IsPreparingTrial => heldBody != null;
        public bool CanLaunchComparison => SelectedStage == 3 && distanceLauncher != null && distanceLauncher.CanLaunch;
        public string ComparisonLaunchBlockReason => SelectedStage != 3 ? ""
            : distanceLauncher != null ? distanceLauncher.LaunchBlockReason : "발사 장치를 연결하고 있습니다.";
        public bool HasBaseline => comparison.HasBaseline;
        public bool HasResult => comparison.HasResult;
        public ElectricForceSample BaselineSample => comparison.BaselineSample;
        public ElectricForceSample ResultSample => comparison.ResultSample;
        public ElectricForceSample QualifiedBaselineSample => comparison.QualifiedBaselineSample;
        public ElectricForceSample QualifiedResultSample => comparison.QualifiedResultSample;
        public string LastExplanation { get; private set; } = "현재 씬의 실험 장치를 조작하세요.";
        public bool ContactChallengeSatisfied { get; private set; }
        public bool ComparisonChallengeSatisfied => comparison.HasQualifiedComparison;
        public bool SingleVariableComparisonSatisfied => comparison.HasSingleVariableComparison;
        public bool NegativeChallengeSatisfied { get; private set; }
        public bool VoltageChallengeSatisfied { get; private set; }
        public bool HasFieldAscent { get; private set; }
        public bool HasNeutralFieldDescent { get; private set; }
        public bool HasRestoredNegativeField { get; private set; }
        public float FieldAscentProgress => HasFieldAscent ? 1f : Mathf.Clamp01(ascentDistance);
        public float NeutralFieldDescentProgress => HasNeutralFieldDescent ? 1f : Mathf.Clamp01(neutralDescentDistance);
        public float FieldBrakeProgress => HasRestoredNegativeField ? 0.1f : Mathf.Clamp(negativeReturnSeconds, 0f, 0.1f);
        public bool IsInsideField => SelectedStage == 4 && TryGetCurrentSample(out ElectricForceSample sample) && sample.IsInRange;
        public bool IsFieldDirectedDownward => SelectedStage == 4 && field != null && field.FieldDirection.y < -0.5f;
        public float FieldVerticalVelocity => SelectedStage == 4 && selector != null && selector.Ball != null
            && selector.Ball.Body != null ? selector.Ball.Body.linearVelocity.y : 0f;
        public bool CanStartFieldBrake => HasNeutralFieldDescent && !HasRestoredNegativeField
            && IsFieldDirectedDownward && IsInsideField && FieldVerticalVelocity < -0.25f;
        public bool IsFieldBrakeActive => CanStartFieldBrake
            && TryGetCurrentSample(out ElectricForceSample sample) && sample.SignedCharge < 0f && sample.Force.y > 0.01f;
                                                               
        private bool IsNearFieldReturnExit => SelectedStage == 4 && selector != null && selector.Ball != null
            && selector.Ball.Body.position.x <= 2.2f && selector.Ball.Body.position.y <= -1.5f;
        public bool IsFieldBrakeRecommended => CanStartFieldBrake && IsNearFieldReturnExit;

                                                                                     
        public string FieldInstructionText
        {
            get
            {
                if (!CanDisplay || SelectedStage != 4) return "전기장 실험을 준비하고 있습니다.";
                if (IsStageCleared) return "상승·하강·제동을 관찰했습니다.";
                if (!IsFieldDirectedDownward) return "전기장이 위를 향해요 · 레버를 중앙이나 오른쪽으로 되돌리세요.";
                Polarity polarity = selector.Ball.CurrentPolarity;
                if (HasRestoredNegativeField)
                    return polarity == Polarity.Negative ? "제동 완료 · 음전하를 유지하고 출구로 이동하세요."
                        : "제동 기록은 유지됩니다 · X 또는 −로 음전하를 선택하고 출구로 이동하세요.";
                if (!HasFieldAscent)
                {
                    if (!IsInsideField) return "A/D로 전기장 안으로 돌아오세요 · 복구는 Backspace";
                    if (polarity == Polarity.Positive) return "양전하의 전기력은 아래 · X 또는 −로 음전하를 선택하세요.";
                    if (polarity == Polarity.Neutral) return "중성의 전기력은 0 · X 또는 −로 음전하를 선택하세요.";
                    return FieldVerticalVelocity > 0.25f ? "음전하를 유지해 전기장 안에서 연속 1m 상승하세요."
                        : "전기력은 위쪽 · 음전하를 유지하고 상승을 기다리세요.";
                }
                if (CollectedStars == 0)
                    return polarity == Polarity.Negative ? "상승 관찰 완료 · A/D로 위쪽 발판의 별을 모으세요."
                        : "X 또는 −로 음전하를 선택하고 위쪽 별로 이동하세요.";
                if (!HasNeutralFieldDescent)
                {
                    if (polarity != Polarity.Neutral) return "별을 모았어요 · C 또는 0으로 중성이 되어 내려오세요.";
                    if (!IsInsideField) return "중성을 유지하고 오른쪽 발판을 따라 내려와 전기장 안으로 돌아오세요.";
                    return FieldVerticalVelocity < -0.25f ? "중성 유지 · 전기장 안에서 연속 1m 하강을 관찰하세요."
                        : "중성의 전기력은 0 · 위로 가던 속도가 줄고 하강할 때까지 기다리세요.";
                }
                if (!IsInsideField) return "전기장 안에서 하강해야 제동할 수 있어요 · 판 사이로 돌아오세요.";
                if (IsFieldBrakeActive) return "하강하는 공에 위쪽 전기력 · 음전하를 유지해 제동하세요.";
                if (!IsNearFieldReturnExit) return polarity == Polarity.Neutral
                    ? "중성 하강 관찰 완료 · 중성을 유지하고 A로 왼쪽 아래 출구 쪽으로 이동하세요."
                    : "C 또는 0으로 중성 유지 · A로 왼쪽 아래 출구 쪽으로 이동하세요.";
                if (IsFieldBrakeRecommended) return "지금 X 또는 −로 음전하 전환 · 하강을 제동하세요!";
                return "아직 하강 중이 아니에요 · C 또는 0으로 중성 하강한 뒤 X로 제동하세요.";
            }
        }

                                                                     
        public string FieldStatusText
        {
            get
            {
                if (SelectedStage != 4 || selector == null || selector.Ball == null) return "";
                string area = IsInsideField ? "장 안" : "장 밖";
                if (!IsFieldDirectedDownward) return area + " · 전기장 방향 복구 필요";
                if (!HasFieldAscent) return $"{area} · 연속 상승 {FieldAscentProgress:0.00}/1m";
                if (CollectedStars == 0) return area + " · 상승 완료 · 상부 별 필요";
                if (!HasNeutralFieldDescent)
                {
                    if (!IsInsideField) return "장 밖 · 중성 하강 관찰은 전기장 안에서";
                    if (selector.Ball.CurrentPolarity == Polarity.Neutral && FieldVerticalVelocity > 0.25f)
                        return "장 안 · 중성의 전기력 0 · 아직 관성으로 상승 중";
                    return $"{area} · 중성 하강 {NeutralFieldDescentProgress:0.00}/1m";
                }
                if (HasRestoredNegativeField) return area + " · 상승·하강·제동 완료";
                if (!IsInsideField) return "장 밖 · 장 안에서 하강 중 X 제동";
                if (selector.Ball.Body.linearVelocity.y >= -0.25f) return "장 안 · 하강 중 X 제동 필요";
                if (selector.Ball.CurrentPolarity != Polarity.Negative) return "장 안 · 지금 X로 음전하 제동";
                return $"장 안 · 하강 제동 {FieldBrakeProgress:0.00}/0.10초";
            }
        }
        public float RequiredLaunchVoltage => 50f;
        public float LastLaunchVoltage { get; private set; }
        public bool CurrentLearningGoalSatisfied => selector != null && CanClearGoal(selector.Ball);

                                                                      
        public string CurrentTaskText
        {
            get
            {
                if (!CanDisplay) return "실험을 준비하고 있습니다.";
                if (IsStageCleared) return "클리어! 다음 단계로 이동합니다.";
                if (SelectedStage == 3 && IsPreparingTrial) return ComparisonPhaseText;
                if (CurrentLearningGoalSatisfied)
                    return SelectedStage == 3 && !IsPreparingTrial
                        ? (CollectedStars > 0 ? "A/D로 착지를 조절해 출구로 이동" : "A/D로 착지를 조절해 별을 모으세요.")
                        : CollectedStars > 0 ? "별·학습 완료 · 출구로 이동하세요." : "학습 완료 · 별을 모으세요.";
                switch (SelectedStage)
                {
                    case 2:
                        if (selector.Ball.CurrentPolarity != Polarity.Neutral)
                            return "Space로 중성 선택 → 전극 접촉";
                        return contactCharger != null && Mathf.Abs(contactCharger.SignedCharge) <= 0f
                            ? "레버로 고정 전극을 충전하고 접촉하세요."
                            : "중성 공을 충전된 전극에 접촉하세요.";
                    case 3: return ComparisonPhaseText;
                    case 4: return FieldInstructionText;
                    case 5:
                        if (selector.Ball.SignedCharge <= 0f)
                            return "Space로 양전하 선택 후 충전하세요.";
                        return CurrentVoltage < RequiredLaunchVoltage
                            ? $"레버로 충전하세요 · {CurrentVoltage:0.0}/{RequiredLaunchVoltage:0} V"
                            : "전극 사이에서 E로 발사하세요.";
                    default: return "학습 목표를 확인하세요.";
                }
            }
        }

                                                                          
        public string ComparisonPhaseText
        {
            get
            {
                if (SelectedStage != 3) return "";
                if (!IsPreparingTrial) return ComparisonChallengeSatisfied
                    ? "A/D로 착지 → 별·출구로 이동"
                    : "Backspace로 발사 위치 복귀";
                if (ComparisonChallengeSatisfied) return "비교 완료 · E로 다시 발사해 별·출구로 이동";
                if (!TryGetDisplaySample(out ElectricForceSample sample) || !sample.IsInRange || sample.ForceMagnitude <= 0f)
                    return ComparisonLaunchBlockReason;
                if (!HasBaseline) return "기준을 준비하고 있습니다.";
                return ElectricComparisonRecord.HaveConditionsChanged(BaselineSample, sample)
                    ? "힘이 달라졌어요 · E로 발사한 뒤 A/D로 착지"
                    : "기준 자동 저장 완료 · 레버로 전극 Q를 높이세요";
            }
        }

                                                                                 
        public string RecentFeedback
        {
            get
            {
                string message = recentFeedback;
                float at = recentFeedbackTime;
                if (SelectedStage == 3 && distanceLauncher != null && distanceLauncher.LastCommandFeedbackTime > at)
                {
                    message = distanceLauncher.LastCommandFeedback;
                    at = distanceLauncher.LastCommandFeedbackTime;
                }
                if (SelectedStage == 5 && voltageLauncher != null && voltageLauncher.LastCommandFeedbackTime > at)
                {
                    message = voltageLauncher.LastCommandFeedback;
                    at = voltageLauncher.LastCommandFeedbackTime;
                }
                return Time.realtimeSinceStartup - at < FeedbackSeconds ? message : "";
            }
        }

        private void ReportCommand(string message, string explanation = null)
        {
            recentFeedback = message;
            recentFeedbackTime = Time.realtimeSinceStartup;
            LastExplanation = explanation ?? message;
        }

                                                                     
        public string LearningGoalSummary
        {
            get
            {
                switch (SelectedStage)
                {
                    case 2: return "중성 공을 전극에 접촉시켜 대전하기";
                    case 3: return "기준 자동 저장 → 레버로 전극 Q 변경 → 발사해 힘 비교";
                    case 4: return "음전하 상승 → 별 → 중성 하강 → 음전하 제동";
                    case 5: return $"{RequiredLaunchVoltage:0} V 이상 충전한 전압으로 발사하기";
                    default: return "스테이지를 선택하세요.";
                }
            }
        }

        public string LearningGoalProgress
        {
            get
            {
                if (CurrentLearningGoalSatisfied)
                    return "학습 목표 달성! 별을 모으고 출구로 이동하세요.";
                switch (SelectedStage)
                {
                    case 2: return "미완료 · 중성 공과 전극의 실제 접촉이 필요합니다.";
                    case 3: return HasBaseline
                        ? "기준 저장 완료 · 레버로 전극 Q를 바꾸고 발사하세요."
                        : "고정된 출발점에서 기준을 준비하고 있습니다.";
                    case 4: return CurrentTaskText + " · 전기장은 아래로 유지하세요.";
                    case 5: return hasVoltageLaunchResult
                        ? $"마지막 발사 {LastLaunchVoltage:0.0} V · {RequiredLaunchVoltage:0} V 이상으로 다시 발사하세요."
                        : $"미완료 · {RequiredLaunchVoltage:0} V 이상 충전 후 발사 영역에서 발사하세요.";
                    default: return "학습 목표를 달성해야 출구가 열립니다.";
                }
            }
        }

                                                   
        public bool CanDisplay => isActiveAndEnabled && selector != null && selector.Ball != null;
        public int SelectedStage => selector != null ? selector.SelectedStage : 2;
        public int CollectedStars => CanDisplay ? selector.Ball.Stars : 0;
        public bool IsStageCleared => selector != null
            && selector.GetStageComponent<ElectricGoal2D>(SelectedStage) is ElectricGoal2D goal
            && goal.IsCleared;
        public bool HasVoltageLauncher => voltageLauncher != null;
        public float CurrentVoltage => voltageLauncher != null ? voltageLauncher.Voltage : 0f;

                                                                             
        public void Configure(ElectricStageSelector owner)
        {
            if (selector != owner)
            {
                ReleaseTrial();
                ResetLearningRecords();
                LastExplanation = "현재 씬의 실험 장치를 조작하세요.";
            }
            Unbind();
            selector = owner;
            contactCharger = owner != null ? owner.GetStageComponent<ContactCharger2D>(owner.SelectedStage == 1 ? 1 : 2) : null;
            distanceLauncher = owner != null ? owner.GetStageComponent<DistanceLauncher2D>(3) : null;
            field = owner != null ? owner.GetStageComponent<UniformField2D>(4) : null;
            voltageLauncher = owner != null ? owner.GetStageComponent<VoltageLauncher2D>(5) : null;
            if (isActiveAndEnabled)
                Bind();
            ConfigureGoals();
        }

        private void Awake()
        {
            if (selector != null)
                Configure(selector);
        }

        private void OnEnable()
        {
            Bind();
        }

        private void OnDisable()
        {
            ReleaseTrial();
            Unbind();
        }

        private void ConfigureGoals()
        {
            if (selector == null || selector.StageRoots == null)
                return;
            foreach (GameObject root in selector.StageRoots)
            {
                if (root == null)
                    continue;
                foreach (ElectricGoal2D goal in root.GetComponentsInChildren<ElectricGoal2D>(true))
                    goal.ConfigureLearningRequirement(this);
            }
        }

        private void Bind()
        {
            if (contactCharger != null)
            {
                contactCharger.ChargedFromNeutral -= OnContactCharged;
                contactCharger.ChargedFromNeutral += OnContactCharged;
            }
            if (distanceLauncher != null)
            {
                distanceLauncher.SetComparisonLaunchRequirement(GetComparisonLearningBlockReason);
                distanceLauncher.Launching -= OnDistanceLaunching;
                distanceLauncher.Launched -= OnLaunched;
                distanceLauncher.Launching += OnDistanceLaunching;
                distanceLauncher.Launched += OnLaunched;
            }
            if (voltageLauncher != null)
            {
                voltageLauncher.LaunchedWithVoltage -= OnVoltageLaunched;
                voltageLauncher.LaunchedWithVoltage += OnVoltageLaunched;
            }
        }

        private void Unbind()
        {
            if (contactCharger != null)
                contactCharger.ChargedFromNeutral -= OnContactCharged;
            if (distanceLauncher != null)
            {
                distanceLauncher.SetComparisonLaunchRequirement(null);
                distanceLauncher.Launching -= OnDistanceLaunching;
                distanceLauncher.Launched -= OnLaunched;
            }
            if (voltageLauncher != null)
                voltageLauncher.LaunchedWithVoltage -= OnVoltageLaunched;
            launchingPreparedTrial = false;
        }

                                                                  
        public void ResetLesson()
        {
            ReleaseTrial();
            ResetLearningRecords();
            if (selector == null || selector.Ball == null)
                return;
            selector.Ball.SetChargeMagnitude(1f);
            distanceLauncher?.SetSourceChargeMagnitude(0.5f);
            if ((SelectedStage == 4 || SelectedStage == 5) && selector.Lever != null)
            {
                foreach (global::LeverMover mover in selector.Lever.GetComponentsInChildren<global::LeverMover>(true))
                    mover.ResetToRest();
                selector.Lever.UseLeverInput();
            }
            LastExplanation = SelectedStage == 4
                ? "아래쪽 전기장에서 음전하로 1m 이상 상승하고 상부 별을 모으세요. 중성으로 장 안에서 1m 이상 하강한 뒤 음전하로 복귀해 제동하고 출구로 이동하세요."
                : SelectedStage == 2
                    ? "중성 공으로 고정 전극에 닿아 전하가 바뀌는 모습을 관찰하세요."
                    : SelectedStage == 5
                        ? $"레버로 {RequiredLaunchVoltage:0} V 이상 충전한 뒤 전극 사이에서 발사하세요. 충전만으로는 목표가 완료되지 않습니다."
                        : "레버로 조건을 조절하고 결과를 확인하세요.";
            if (SelectedStage == 3)
                PrepareTrial();
        }

                                                                                   
        private void ResetLearningRecords()
        {
            comparison.Reset();
            ContactChallengeSatisfied = false;
            NegativeChallengeSatisfied = false;
            VoltageChallengeSatisfied = false;
            launchingPreparedTrial = false;
            hasVoltageLaunchResult = false;
            LastLaunchVoltage = 0f;
            recentFeedback = "";
            recentFeedbackTime = float.NegativeInfinity;
            HasFieldAscent = false;
            HasNeutralFieldDescent = false;
            HasRestoredNegativeField = false;
            ResetTransientObservations();
        }

                                                                      
        public void ResetTransientObservations()
        {
            hasPreviousFieldPosition = false;
            ascendingSegment = false;
            neutralDescendingSegment = false;
            ascentDistance = 0f;
            neutralDescentDistance = 0f;
            negativeReturnSeconds = 0f;
            hasDisplaySample = false;
        }

                     
                                                 
                                               
                      
        public void PrepareTrial()
        {
            if (!Application.isPlaying || selector == null || SelectedStage != 3
                || selector.Ball == null || !selector.TryGetSpawnPosition(out Vector2 position))
                return;
            ReleaseTrial();
            launchingPreparedTrial = false;
            comparison.Reset();
            GetComponent<ElectricExperimentPlan2D>()?.ResetComparisonPlan();
            distanceLauncher?.ResetLaunch();
            ElectricBall2D ball = selector.Ball;
            ResetDestination();
            ball.ResetAt(position, Polarity.Positive);
            ball.SetChargeMagnitude(1f);
            ResetComparisonLeverToRest();
            distanceLauncher?.SetSourceChargeMagnitude(0.5f);
            HoldTrial(ball);
            ResetTransientObservations();
            GetComponent<ElectricStageSession2D>()?.ResetComparisonCheckpoint(position);
            if (TryGetCurrentSample(out ElectricForceSample sample) && sample.IsInRange && sample.ForceMagnitude > 0f)
                comparison.RecordBaseline(sample);
            ReportCommand(HasBaseline ? "기준 자동 저장 · 레버로 Q를 높여 보세요." : "발사 위치와 장치를 확인하세요.",
                "같은 거리에서 공 q=+1, 전극 Q=0.5의 힘을 기준으로 저장했습니다. 레버로 Q를 바꾸고 E로 발사하세요. Backspace는 기록을 유지하고, T는 새 실험을 시작합니다.");
        }

                                                                            
        public bool RetryComparisonPosition()
        {
            if (!Application.isPlaying || selector == null || SelectedStage != 3
                || selector.Ball == null || !selector.TryGetSpawnPosition(out Vector2 position)) return false;
            ReleaseTrial();
            launchingPreparedTrial = false;
            distanceLauncher?.ResetLaunch();
            selector.Ball.ReturnToCheckpoint(position);
            HoldTrial(selector.Ball);
            ResetTransientObservations();
            ReportCommand("발사 위치로 복귀 · 기준·학습·별 유지", ComparisonChallengeSatisfied
                ? "비교에 성공한 기록을 유지했습니다. E로 다시 발사하고 A/D로 착지해 별과 출구로 이동하세요."
                : "기준과 현재 전하량을 유지했습니다. 레버로 전극 Q를 바꾸고 E로 발사하세요.");
            return true;
        }

        private void HoldTrial(ElectricBall2D ball)
        {
            heldBody = ball.Body;
            if (heldBody == null) return;
            savedConstraints = heldBody.constraints;
            heldBody.constraints = RigidbodyConstraints2D.FreezeAll;
        }

        internal void ResetComparisonLeverToRest()
        {
            if (SelectedStage != 3 || selector.Lever == null) return;
            foreach (global::LeverMover mover in selector.Lever.GetComponentsInChildren<global::LeverMover>(true))
                mover.ResetToRest();
            selector.Lever.UseLeverInput();
        }

        private string GetComparisonLearningBlockReason()
        {
            if (SelectedStage != 3) return "";
            if (!IsPreparingTrial) return "비행 중 · A/D로 착지하세요. 복귀는 Backspace";
            if (ComparisonChallengeSatisfied) return "";
            if (!HasBaseline) return "기준이 없습니다 · T로 새 실험을 시작하세요.";
            if (!TryGetCurrentSample(out ElectricForceSample sample)) return "측정값을 준비하고 있습니다.";
            return ElectricComparisonRecord.HaveConditionsChanged(BaselineSample, sample)
                ? "" : GetComponent<ElectricExperimentPlan2D>()?.SelectedPlan == 1
                    ? "기준과 같은 조건입니다 · X로 공 전하량을 높이세요."
                    : "기준과 같은 조건입니다 · 레버로 전극 Q를 높이세요.";
        }

        private void ReleaseTrial()
        {
            if (heldBody == null)
                return;
            heldBody.constraints = savedConstraints;
            heldBody = null;
        }

                                                                              
        public void SetBallCharge(float signedCharge)
        {
            if (selector == null || selector.Ball == null || float.IsNaN(signedCharge)
                || float.IsInfinity(signedCharge))
                return;
            ElectricBall2D ball = selector.Ball;
            Polarity polarity = signedCharge > 0f ? Polarity.Positive
                : signedCharge < 0f ? Polarity.Negative : Polarity.Neutral;
            ball.SetPolarity(polarity);
            ball.SetChargeMagnitude(Mathf.Abs(signedCharge));
                                                 
            string explanation = polarity == Polarity.Negative
                ? "음전하는 전기장과 반대 방향으로 전기력을 받습니다. 장과 힘의 방향을 비교하세요."
                : polarity == Polarity.Positive
                    ? "양전하는 전기장과 같은 방향으로 전기력을 받습니다."
                    : "이 점전하 모형에서 중성 공의 전기력은 0입니다. 전기장은 그대로 존재합니다. 실제 금속의 유도 현상은 이 실험에서 다루지 않습니다.";
            if (SelectedStage == 4)
            {
                Vector2 forceDirection = field != null ? field.FieldDirection * (int)polarity : Vector2.zero;
                string direction = forceDirection.y > 0.01f ? "위" : forceDirection.y < -0.01f ? "아래" : "0";
                string observation = polarity == Polarity.Neutral ? "중성 · 전기력 0, 중력은 작용"
                    : $"{(polarity == Polarity.Negative ? "음" : "양")}전하 · {(IsInsideField ? "전기력 " : "장에 들어가면 전기력 ")}{direction}";
                ReportCommand(observation, explanation + " 이동 방향은 남아 있는 속도와 힘에 따라 바뀌므로 전하를 바꾼 즉시 뒤집히지는 않습니다.");
                return;
            }
            ReportCommand(polarity == Polarity.Neutral ? "중성 · 전기력 0, 현 위치 유지"
                : $"{(polarity == Polarity.Negative ? "음" : "양")}전하 {Mathf.Abs(signedCharge):0.##} · 현 위치 유지", explanation);
        }

                                                                      
        public bool TryGetCurrentSample(out ElectricForceSample sample)
        {
            sample = default;
            return selector != null && selector.TryGetCurrentForceSample(out sample);
        }

                                                                         
        public bool TryGetDisplaySample(out ElectricForceSample sample)
        {
            if (!TryGetCurrentSample(out sample)) return false;
            if (Time.timeScale <= 0f && hasDisplaySample
                && sample.Position == lastDisplaySample.Position
                && Mathf.Abs(sample.Distance - lastDisplaySample.Distance) < 0.0001f
                && sample.SignedCharge == lastDisplaySample.SignedCharge
                && sample.SourceCharge == lastDisplaySample.SourceCharge)
                sample = lastDisplaySample;
            return true;
        }

                                                                    
        public void RecordBaseline()
        {
            if (SelectedStage != 3 || !IsPreparingTrial
                || !TryGetCurrentSample(out ElectricForceSample sample) || !sample.IsInRange
                || sample.ForceMagnitude <= 0f)
            {
                ReportCommand("기준 저장 불가 · 준비·양전하 확인");
                return;
            }
            comparison.RecordBaseline(sample);
            ReportCommand("새 기준 저장 완료 · 공 q 또는 전극 Q를 바꾸세요.");
        }

                                                                                   
        private void OnDistanceLaunching()
        {
            launchingPreparedTrial = SelectedStage == 3 && IsPreparingTrial;
            ReleaseTrial();
        }

        private void OnLaunched(ElectricForceSample sample)
        {
            if (SelectedStage != 3)
                return;
            bool wasPrepared = launchingPreparedTrial;
            launchingPreparedTrial = false;
            comparison.RecordResult(sample, wasPrepared);
            LastExplanation = comparison.DescribeResult();
            if (ComparisonChallengeSatisfied)
                LastExplanation += " 비교 발사 목표를 달성했습니다. 별을 모으고 출구로 이동하세요.";
            else if (HasBaseline)
                LastExplanation += wasPrepared
                    ? " 기준과 같은 조건입니다. 실험 위치로 복귀해 공 q 또는 전극 Q를 바꾼 뒤 다시 발사하세요."
                    : " 공중에서 달라진 거리는 비교 조건으로 인정하지 않습니다. 실험 위치로 복귀해 조건을 바꾸고 발사하세요.";
            ReportCommand(ComparisonChallengeSatisfied ? "비교 성공 · 별과 출구로 이동하세요."
                : !HasBaseline ? "기준 없음 · T로 새 실험"
                : wasPrepared ? "조건이 같습니다 · T로 재준비"
                : "출발점이 다릅니다 · T로 재준비", LastExplanation);
        }

                                                                          
        private void OnVoltageLaunched(float launchVoltage)
        {
            if (SelectedStage != 5)
                return;
            LastLaunchVoltage = launchVoltage;
            hasVoltageLaunchResult = true;
            if (launchVoltage >= RequiredLaunchVoltage)
                VoltageChallengeSatisfied = true;
            LastExplanation = launchVoltage >= RequiredLaunchVoltage
                ? $"{launchVoltage:0.0} V를 사용해 발사했습니다. 전기적 에너지가 운동 에너지로 바뀌었습니다. 충전 발사 목표를 달성했으니 별을 모으고 출구로 이동하세요."
                : VoltageChallengeSatisfied
                    ? $"이번 발사에는 {launchVoltage:0.0} V를 사용했습니다. 앞서 {RequiredLaunchVoltage:0} V 이상으로 발사한 학습 기록은 유지됩니다. 별을 모으고 출구로 이동하세요."
                    : $"{launchVoltage:0.0} V로 발사했지만 목표 전압에 미달했습니다. {RequiredLaunchVoltage:0} V 이상 충전한 뒤 다시 발사해야 출구를 통과합니다.";
            ReportCommand(VoltageChallengeSatisfied ? $"{launchVoltage:0.0} V 발사 · 학습 기록 유지"
                : $"{launchVoltage:0.0} V 발사 · {RequiredLaunchVoltage:0} V 미달", LastExplanation);
        }

                                                               
        public void SelectStage(int stageNumber) => selector?.SelectStage(stageNumber);
        public void RestartStage() => selector?.RestartStage();

        public void SetSourceChargeMagnitude(float magnitude)
        {
            if (SelectedStage == 3 && IsPreparingTrial)
            {
                distanceLauncher?.SetSourceChargeMagnitude(magnitude);
                ReportCommand($"전극 Q {distanceLauncher?.SourceChargeMagnitude:0.##} 선택 · 기준과 비교");
            }
            else if (SelectedStage == 3)
                ReportCommand("비행 중 선택 불가 · T로 재준비");
        }

        public void ReportComparisonPreparationRequired() =>
            ReportCommand("비행 중 선택 불가 · T로 재준비");

        public void RequestLaunch()
        {
            if (SelectedStage == 3)
                distanceLauncher?.RequestLaunch();
            else if (SelectedStage == 5)
                voltageLauncher?.RequestLaunch();
        }

        private void FixedUpdate()
        {
            if (TryGetCurrentSample(out ElectricForceSample display))
            {
                lastDisplaySample = display;
                hasDisplaySample = true;
            }
            if (selector == null || selector.Ball == null)
                return;
            ObserveNegativeChallenge();
            ObserveFieldTraversal();
        }

                                                                          
        private void OnContactCharged(ElectricBall2D chargedBall)
        {
            if (selector == null || SelectedStage != 2 || chargedBall != selector.Ball)
                return;
            ContactChallengeSatisfied = true;
            string polarityName = chargedBall.CurrentPolarity == Polarity.Positive ? "양전하" : "음전하";
            LastExplanation = "접촉으로 공이 " + polarityName
                + "를 띠었습니다. 같은 부호의 전극과 공 사이에 척력이 작용합니다.";
        }

                                                                  
        private void ObserveNegativeChallenge()
        {
            if (SelectedStage != 4 || !TryGetCurrentSample(out ElectricForceSample sample)
                || !sample.IsInRange || sample.SignedCharge >= 0f)
                return;
            if (sample.Force.y > 0.01f)
            {
                if (!NegativeChallengeSatisfied)
                    LastExplanation = "전기장은 아래, 음전하가 받는 힘은 위입니다. 장 안에서 1m 상승하고 별을 모은 뒤 중성 하강과 음전하 제동을 이어가세요.";
                NegativeChallengeSatisfied = true;
            }
        }

                                                                      
        private void ObserveFieldTraversal()
        {
            if (SelectedStage != 4 || selector.Ball.Body == null) return;
            ElectricBall2D ball = selector.Ball;
            Vector2 position = ball.Body.position;
            float deltaY = hasPreviousFieldPosition ? position.y - previousFieldPosition.y : 0f;
            bool inDownwardField = TryGetCurrentSample(out ElectricForceSample sample)
                && sample.IsInRange && field != null && field.FieldDirection.y < -0.5f;
            bool rising = inDownwardField && sample.SignedCharge < 0f && sample.Force.y > 0.01f
                && ball.Body.linearVelocity.y > 0.25f;
            if (!HasFieldAscent)
            {
                ascentDistance = rising && ascendingSegment ? ascentDistance + Mathf.Max(0f, deltaY) : 0f;
                if (ascentDistance >= 1f) HasFieldAscent = true;
            }
            ascendingSegment = rising;

            bool neutralFalling = HasFieldAscent && ball.Stars > 0 && inDownwardField
                && ball.CurrentPolarity == Polarity.Neutral && Mathf.Abs(sample.Force.y) <= 0.01f
                && ball.Body.linearVelocity.y < -0.25f;
            if (!HasNeutralFieldDescent)
            {
                neutralDescentDistance = neutralFalling && neutralDescendingSegment
                    ? neutralDescentDistance + Mathf.Max(0f, -deltaY) : 0f;
                if (neutralDescentDistance >= 1f) HasNeutralFieldDescent = true;
            }
            neutralDescendingSegment = neutralFalling;

            if (!HasRestoredNegativeField)
            {
                bool braking = HasNeutralFieldDescent && inDownwardField
                    && sample.SignedCharge < 0f && sample.Force.y > 0.01f
                    && ball.Body.linearVelocity.y < -0.25f;
                negativeReturnSeconds = braking ? negativeReturnSeconds + Time.fixedDeltaTime : 0f;
                if (negativeReturnSeconds >= 0.1f) HasRestoredNegativeField = true;
            }
            previousFieldPosition = position;
            hasPreviousFieldPosition = true;
        }

                                                                      
        public bool CanClearGoal(ElectricBall2D candidate)
        {
            if (selector == null || candidate == null || candidate != selector.Ball)
                return false;
            switch (SelectedStage)
            {
                case 1:
                    TutorialController2D tutorial = selector.GetComponent<TutorialController2D>();
                    return tutorial != null && tutorial.CanClearGoal(candidate);
                case 2: return ContactChallengeSatisfied;
                case 3: return ComparisonChallengeSatisfied;
                case 4: return candidate.CurrentPolarity == Polarity.Negative
                    && NegativeChallengeSatisfied && HasFieldAscent && HasNeutralFieldDescent
                    && HasRestoredNegativeField && field != null && field.FieldDirection.y < -0.5f;
                case 5: return VoltageChallengeSatisfied;
                default: return false;
            }
        }

        private void ResetDestination()
        {
            GameObject root = selector != null ? selector.CurrentStageRoot : null;
            if (root == null)
                return;
            foreach (ElectricStar2D star in root.GetComponentsInChildren<ElectricStar2D>(true))
                star.ResetProgress();
            foreach (ElectricGoal2D goal in root.GetComponentsInChildren<ElectricGoal2D>(true))
                goal.ResetProgress();
        }

        private void OnDestroy()
        {
            ReleaseTrial();
            Unbind();
        }
    }
}
