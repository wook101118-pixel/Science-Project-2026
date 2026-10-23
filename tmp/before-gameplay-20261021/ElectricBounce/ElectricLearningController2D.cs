using UnityEngine;

namespace sung_wook
{
    /// <summary>
    /// 실험 준비, 전하 선택, 비교 기록과 각 스테이지의 학습 완료 조건을 관리합니다.
    /// 입력은 ElectricLearningInput2D, 비교 해석은 ElectricComparisonRecord에 맡깁니다.
    /// 기존 이동과 레버 입력을 소유하지 않으며 비교 실험을 준비할 때만 공을 고정합니다.
    /// </summary>
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

        public bool IsPreparingTrial => heldBody != null;
        public bool HasBaseline => comparison.HasBaseline;
        public bool HasResult => comparison.HasResult;
        public ElectricForceSample BaselineSample => comparison.BaselineSample;
        public ElectricForceSample ResultSample => comparison.ResultSample;
        public string LastExplanation { get; private set; } = "현재 씬의 실험 장치를 조작하세요.";
        public bool ContactChallengeSatisfied { get; private set; }
        public bool ComparisonChallengeSatisfied => comparison.HasQualifiedComparison;
        public bool NegativeChallengeSatisfied { get; private set; }
        public bool VoltageChallengeSatisfied { get; private set; }
        public float RequiredLaunchVoltage => 50f;
        public float LastLaunchVoltage { get; private set; }
        public bool CurrentLearningGoalSatisfied => selector != null && CanClearGoal(selector.Ball);

        /// <summary>콘솔 관측이나 별도 제작한 UI에서 사용할 읽기 전용 학습 목표이다.</summary>
        public string LearningGoalSummary
        {
            get
            {
                switch (SelectedStage)
                {
                    case 2: return "중성 공을 전극에 접촉시켜 대전하기";
                    case 3: return "기준 저장 → 거리 또는 전하량 변경 → 비교 발사";
                    case 4: return "음전하가 아래쪽 전기장에서 위로 힘 받기";
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
                        ? "기준 저장 완료 · 고정 출발점에서 조건을 바꿔 발사하세요."
                        : "미완료 · 먼저 고정된 공의 기준값을 저장하세요.";
                    case 4: return NegativeChallengeSatisfied
                        ? "음전하와 아래쪽 전기장을 유지해야 출구를 통과합니다."
                        : "미완료 · 음전하가 전기장 안에서 위로 힘을 받아야 합니다.";
                    case 5: return hasVoltageLaunchResult
                        ? $"마지막 발사 {LastLaunchVoltage:0.0} V · {RequiredLaunchVoltage:0} V 이상으로 다시 발사하세요."
                        : $"미완료 · {RequiredLaunchVoltage:0} V 이상 충전 후 발사 영역에서 발사하세요.";
                    default: return "학습 목표를 달성해야 출구가 열립니다.";
                }
            }
        }

        // 외부 입력과 관측에는 읽기 전용 상태와 명시적인 실험 명령을 제공합니다.
        public bool CanDisplay => isActiveAndEnabled && selector != null && selector.Ball != null;
        public int SelectedStage => selector != null ? selector.SelectedStage : 2;
        public int CollectedStars => CanDisplay ? selector.Ball.Stars : 0;
        public bool IsStageCleared => selector != null
            && selector.GetStageComponent<ElectricGoal2D>(SelectedStage) is ElectricGoal2D goal
            && goal.IsCleared;
        public bool HasVoltageLauncher => voltageLauncher != null;
        public float CurrentVoltage => voltageLauncher != null ? voltageLauncher.Voltage : 0f;

        /// <summary>기존 선택기와 발사 이벤트를 연결합니다. 반복 호출해도 이벤트가 중복되지 않습니다.</summary>
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
            contactCharger = owner != null ? owner.GetStageComponent<ContactCharger2D>(2) : null;
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
                distanceLauncher.Launching -= OnDistanceLaunching;
                distanceLauncher.Launched -= OnLaunched;
            }
            if (voltageLauncher != null)
                voltageLauncher.LaunchedWithVoltage -= OnVoltageLaunched;
            launchingPreparedTrial = false;
        }

        /// <summary>스테이지 재시작 시 이전 기록과 도전 판정을 모두 초기화합니다.</summary>
        public void ResetLesson()
        {
            ReleaseTrial();
            ResetLearningRecords();
            if (selector == null || selector.Ball == null)
                return;
            selector.Ball.SetChargeMagnitude(1f);
            distanceLauncher?.SetSourceChargeMagnitude(1f);
            LastExplanation = SelectedStage == 4
                ? "음전하로 대전시킨 뒤, 전기장이 아래를 향하도록 레버를 조절하세요. 공이 위로 힘을 받으면 별과 출구를 향해 이동하세요."
                : SelectedStage == 2
                    ? "중성 공에 전극을 접촉시켜 전하가 바뀌는 모습을 관찰하세요."
                    : SelectedStage == 5
                        ? $"레버로 {RequiredLaunchVoltage:0} V 이상 충전한 뒤 전극 사이에서 발사하세요. 충전만으로는 목표가 완료되지 않습니다."
                        : "레버로 조건을 조절하고 결과를 확인하세요.";
            if (SelectedStage == 3)
                PrepareTrial();
        }

        /// <summary>소유 스테이지 변경과 재시작이 같은 기록 초기화 규칙을 사용한다. 물리 상태는 바꾸지 않는다.</summary>
        private void ResetLearningRecords()
        {
            comparison.Reset();
            ContactChallengeSatisfied = false;
            NegativeChallengeSatisfied = false;
            VoltageChallengeSatisfied = false;
            launchingPreparedTrial = false;
            hasVoltageLaunchResult = false;
            LastLaunchVoltage = 0f;
        }

        /// <summary>
        /// 기준과 결과를 유지하며 공을 같은 출발점에 고정합니다.
        /// 발사 직전 원래 Rigidbody 제약을 복원하면 기존 플레이어가 충격량을 적용합니다.
        /// </summary>
        public void PrepareTrial()
        {
            if (!Application.isPlaying || selector == null || SelectedStage != 3
                || selector.Ball == null || !selector.TryGetSpawnPosition(out Vector2 position))
                return;
            ReleaseTrial();
            launchingPreparedTrial = false;
            distanceLauncher?.ResetLaunch();
            ElectricBall2D ball = selector.Ball;
            float magnitude = ball.ChargeMagnitude;
            ResetDestination();
            ball.ResetAt(position, Polarity.Positive);
            ball.SetChargeMagnitude(magnitude);
            heldBody = ball.Body;
            if (heldBody == null)
                return;
            savedConstraints = heldBody.constraints;
            heldBody.constraints = RigidbodyConstraints2D.FreezeAll;
            LastExplanation = "공을 같은 위치에 고정했습니다. 레버로 거리를 조절하고 B로 기준값을 저장한 뒤, 거리 또는 전하량을 바꾸세요. E로 발사하고 L로 관측값과 결과를 콘솔에서 확인합니다.";
        }

        private void ReleaseTrial()
        {
            if (heldBody == null)
                return;
            heldBody.constraints = savedConstraints;
            heldBody = null;
        }

        /// <summary>전하 선택을 기존 플레이어에 전달합니다. 4번 실험은 같은 위치에서 다시 시작합니다.</summary>
        public void SetBallCharge(float signedCharge)
        {
            if (selector == null || selector.Ball == null || float.IsNaN(signedCharge)
                || float.IsInfinity(signedCharge))
                return;
            ElectricBall2D ball = selector.Ball;
            Polarity polarity = signedCharge > 0f ? Polarity.Positive
                : signedCharge < 0f ? Polarity.Negative : Polarity.Neutral;
            NegativeChallengeSatisfied = false;
            if (SelectedStage == 4 && selector.TryGetSpawnPosition(out Vector2 position))
            {
                ResetDestination();
                ball.ResetAt(position, polarity);
            }
            else
            {
                ball.SetPolarity(polarity);
            }
            ball.SetChargeMagnitude(Mathf.Abs(signedCharge));
            // 점전하 모형이므로 금속 내부의 유도 대전은 계산하지 않습니다.
            LastExplanation = polarity == Polarity.Negative
                ? "음전하는 전기장과 반대 방향으로 전기력을 받습니다. 보라색 E와 주황색 F의 방향을 비교하세요."
                : polarity == Polarity.Positive
                    ? "양전하는 전기장과 같은 방향으로 전기력을 받습니다."
                    : "이 점전하 모형에서 중성 공의 전기력은 0입니다. 전기장은 그대로 존재합니다. 실제 금속의 유도 현상은 이 실험에서 다루지 않습니다.";
        }

        /// <summary>힘을 추가하지 않고 현재 물리 장치가 사용하는 표본을 그대로 읽습니다.</summary>
        public bool TryGetCurrentSample(out ElectricForceSample sample)
        {
            sample = default;
            return selector != null && selector.TryGetCurrentForceSample(out sample);
        }

        /// <summary>고정된 공이 유효한 발사 범위에 있을 때만 비교 기준을 저장합니다.</summary>
        public void RecordBaseline()
        {
            if (SelectedStage != 3 || !IsPreparingTrial
                || !TryGetCurrentSample(out ElectricForceSample sample) || !sample.IsInRange
                || sample.ForceMagnitude <= 0f)
            {
                LastExplanation = "먼저 실험 위치로 복귀한 뒤, 발사 범위 안에서 기준값을 저장하세요.";
                return;
            }
            comparison.RecordBaseline(sample);
            LastExplanation = "기준값을 저장했습니다. 공 위치는 그대로 두고 거리 또는 전하량 하나를 바꿔 비교하세요.";
        }

        /// <summary>발사기가 공의 고정을 풀기 전에 준비 상태를 기록한다. 공중 재발사는 비교로 인정하지 않는다.</summary>
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
                    ? " 기준과 같은 조건입니다. 실험 위치로 복귀해 거리 또는 전하량을 바꾼 뒤 다시 발사하세요."
                    : " 공중에서 달라진 거리는 비교 조건으로 인정하지 않습니다. 실험 위치로 복귀해 조건을 바꾸고 발사하세요.";
        }

        /// <summary>충전 완료 알림이 아니라 실제 성공한 발사와 소비 전 전압을 근거로 판정한다.</summary>
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
        }

        // 스테이지/발사 입력은 기존 선택기/발사기가 소유한다. 별도 UI도 이 명령에 연결할 수 있다.
        public void SelectStage(int stageNumber) => selector?.SelectStage(stageNumber);
        public void RestartStage() => selector?.RestartStage();

        public void SetSourceChargeMagnitude(float magnitude)
        {
            if (SelectedStage == 3 && IsPreparingTrial)
                distanceLauncher?.SetSourceChargeMagnitude(magnitude);
        }

        public void RequestLaunch()
        {
            if (SelectedStage == 3)
                distanceLauncher?.RequestLaunch();
            else if (SelectedStage == 5)
                voltageLauncher?.RequestLaunch();
        }

        private void FixedUpdate()
        {
            if (selector == null || selector.Ball == null)
                return;
            ObserveNegativeChallenge();
        }

        /// <summary>연결된 전극에서 온 실제 중성→대전 충돌만 접촉 학습의 완료 증거로 사용한다.</summary>
        private void OnContactCharged(ElectricBall2D chargedBall)
        {
            if (selector == null || SelectedStage != 2 || chargedBall != selector.Ball)
                return;
            ContactChallengeSatisfied = true;
            string polarityName = chargedBall.CurrentPolarity == Polarity.Positive ? "양전하" : "음전하";
            LastExplanation = "접촉으로 공이 " + polarityName
                + "를 띠었습니다. 같은 부호의 전극과 공 사이에 척력이 작용합니다.";
        }

        /// <summary>범위 안에서 음전하가 위쪽 전기력을 실제로 받았는지 확인합니다.</summary>
        private void ObserveNegativeChallenge()
        {
            if (SelectedStage != 4 || !TryGetCurrentSample(out ElectricForceSample sample)
                || !sample.IsInRange || sample.SignedCharge >= 0f)
                return;
            if (sample.Force.y > 0.01f)
            {
                NegativeChallengeSatisfied = true;
                LastExplanation = "전기장은 아래, 음전하가 받는 힘은 위입니다. 음전하 조건을 확인했습니다. 별을 모으고 출구로 이동하세요.";
            }
            else if (sample.Force.y < -0.01f)
            {
                LastExplanation = "전기장이 위를 향해 음전하가 아래로 힘을 받고 있습니다. 레버를 되돌려 전기장 방향을 바꿔 보세요.";
            }
        }

        /// <summary>별 수집과 별개로 각 실험의 실제 완료 기록이 있어야 출구를 통과한다.</summary>
        public bool CanClearGoal(ElectricBall2D candidate)
        {
            if (selector == null || candidate == null || candidate != selector.Ball)
                return false;
            switch (SelectedStage)
            {
                case 2: return ContactChallengeSatisfied;
                case 3: return ComparisonChallengeSatisfied;
                case 4: return candidate.CurrentPolarity == Polarity.Negative
                    && NegativeChallengeSatisfied && field != null && field.FieldDirection.y < -0.5f;
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
