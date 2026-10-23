using UnityEngine;

namespace sung_wook
{
                                                                            
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class ElectricStageChallenge2D : MonoBehaviour
    {
        private ElectricStageSelector selector;
        private ElectricStageSession2D session;
        private ElectricLearningController2D lesson;
        private Player observedPlayer;
        private ElectricGoal2D goal;
        private VoltageLauncher2D voltageLauncher;
        private int polarityChanges;
        private bool finished;
        private float attractionSeconds, repulsionSeconds, efficientVoltage;

        public int PolarityChanges => polarityChanges;
        public int EarnedFlags { get; private set; }
        public bool UsedSingleVariable { get; private set; }
        public int NewlyEarnedFlags { get; private set; }
        public bool ImprovedRetryRecord { get; private set; }
        public bool ImprovedVoltageRecord { get; private set; }
        public string ResultSummaryText { get; private set; } = "";
        public string ExtraChallengeName => selector == null ? ""
            : selector.SelectedStage == 2 ? "인력·척력 모두 체험"
            : selector.SelectedStage == 3 ? "한 변수만 바꿔 비교"
            : selector.SelectedStage == 4 ? "전하 전환 4회 이내" : "표시 50.0~55.0 V 발사";

                                                                               
        public static bool IsEfficientLaunchVoltage(float voltage) => !float.IsNaN(voltage)
            && !float.IsInfinity(voltage) && voltage >= 50f && Mathf.RoundToInt(voltage * 10f) <= 550;

        public string CompactText
        {
            get
            {
                if (selector == null || session == null || selector.SelectedStage < 2) return "";
                int flags = PlayerPrefs.GetInt(Key + ".Flags", 0) & 3;
                string history = (flags & 2) != 0 ? "기록 달성" : "미획득";
                string extra = selector.SelectedStage == 2
                    ? "인력 " + (attractionSeconds >= .15f ? "완료" : "대기") + " · 척력 " + (repulsionSeconds >= .15f ? "완료" : "대기")
                    : selector.SelectedStage == 3 ? "한 변수 비교 " + (lesson != null && lesson.SingleVariableComparisonSatisfied ? "완료" : "진행")
                    : selector.SelectedStage == 4 ? "전하 전환 " + polarityChanges + "/4회 이내"
                    : "50~55V: " + (IsEfficientLaunchVoltage(efficientVoltage) ? efficientVoltage.ToString("0.0") + "V 발사" : "현재 " + (voltageLauncher != null ? voltageLauncher.Voltage : 0f).ToString("0.0") + "V");
                return "선택·복귀 0회: 현재 " + session.RetryCount + "회 · " + ((flags & 1) != 0 ? "기록 달성" : "미획득")
                    + "\n" + extra + " · " + history;
            }
        }

        public string ProgressText
        {
            get
            {
                if (selector == null || session == null) return "";
                if (selector.SelectedStage == 1) return "선택 도전은 2번 스테이지부터 시작합니다.";
                int saved = PlayerPrefs.GetInt(Key + ".Flags", 0) & 3;
                string first = session.RetryCount == 0 ? "진행 · 현재 0회" : "이번 시도 미달 · 현재 " + session.RetryCount + "회";
                string second;
                switch (selector.SelectedStage)
                {
                    case 2:
                        second = "인력 " + (attractionSeconds >= 0.15f ? "체험 완료" : "미체험")
                            + " / 척력 " + (repulsionSeconds >= 0.15f ? "체험 완료" : "미체험");
                        break;
                    case 3:
                        second = lesson != null && lesson.SingleVariableComparisonSatisfied
                            ? "한 변수 비교 완료" : "거리 유지 · 공 q 또는 전극 Q만 변경";
                        break;
                    case 4:
                        second = "현재 " + polarityChanges + "회 / 4회 이내"
                            + (polarityChanges > 4 ? " · 이번 시도 미달" : "");
                        break;
                    default:
                        second = IsEfficientLaunchVoltage(efficientVoltage) ? efficientVoltage.ToString("0.0") + " V 발사 완료"
                            : "현재 " + (voltageLauncher != null ? voltageLauncher.Voltage : 0f).ToString("0.0") + " V · 표시 목표 50.0~55.0 V";
                        break;
                }
                return "선택 도전 · 기본 클리어와 무관\n"
                    + "복귀 0회: " + first + ((saved & 1) != 0 ? " · 획득 기록 있음" : "") + "\n"
                    + ExtraChallengeName + ": " + second + ((saved & 2) != 0 ? " · 획득 기록 있음" : "");
            }
        }

        private string Key => "ElectricBounce.Mastery.Stage" + selector.SelectedStage;
        public string Hint
        {
            get
            {
                if (selector == null || session == null) return "";
                if (selector.SelectedStage == 1) return ProgressText;
                int flags = PlayerPrefs.GetInt(Key + ".Flags", 0) & 3;
                int badges = (flags & 1) + ((flags >> 1) & 1);
                int fewest = PlayerPrefs.GetInt(Key + ".FewestRetries", -1);
                string record = fewest >= 0 ? "최소 복귀 " + fewest + "회" : "첫 완주 도전";
                if (selector.SelectedStage == 5 && PlayerPrefs.HasKey(Key + ".BestVoltage"))
                    record = "최저 " + PlayerPrefs.GetFloat(Key + ".BestVoltage").ToString("0.0") + " V";
                return ProgressText + "\n기록 " + badges + "/2 · " + record;
            }
        }

        public void Configure(ElectricStageSelector owner, ElectricStageSession2D stageSession)
        {
            Unbind();
            selector = owner;
            session = stageSession;
            lesson = owner.GetComponent<ElectricLearningController2D>();
            observedPlayer = owner.Ball != null ? owner.Ball.Player : null;
            goal = owner.GetStageComponent<ElectricGoal2D>(owner.SelectedStage);
            voltageLauncher = owner.GetStageComponent<VoltageLauncher2D>(5);
            if (observedPlayer != null) observedPlayer.StateChanged += OnPolarityChanged;
            if (goal != null) goal.OnCleared.AddListener(RecordClear);
            if (voltageLauncher != null) voltageLauncher.LaunchedWithVoltage += OnVoltageLaunched;
        }

        public void ResetAttempt()
        {
            finished = false;
            polarityChanges = 0;
            attractionSeconds = repulsionSeconds = 0f;
            efficientVoltage = float.PositiveInfinity;
            EarnedFlags = 0;
            UsedSingleVariable = false;
            NewlyEarnedFlags = 0;
            ImprovedRetryRecord = false;
            ImprovedVoltageRecord = false;
            ResultSummaryText = "";
        }

        private void OnPolarityChanged(PlayerStateType state)
        {
            if (!finished && session != null && session.IsInitialized) polarityChanges++;
        }

        private void OnVoltageLaunched(float voltage)
        {
            if (!finished && lesson != null && voltage >= lesson.RequiredLaunchVoltage)
                efficientVoltage = Mathf.Min(efficientVoltage, voltage);
        }

        private void FixedUpdate()
        {
            if (finished || selector == null || lesson == null || selector.SelectedStage != 2
                || !lesson.ContactChallengeSatisfied || !selector.TryGetCurrentForceSample(out ElectricForceSample sample)
                || !sample.IsInRange || sample.ForceMagnitude < 0.01f) return;
            float product = sample.SignedCharge * sample.SourceCharge;
            if (product > 0f) repulsionSeconds += Time.fixedDeltaTime;
            else if (product < 0f) attractionSeconds += Time.fixedDeltaTime;
        }

        private void RecordClear()
        {
            RecordCompletion();
        }

        public void RecordCompletion()
        {
            if (finished || selector == null || session == null || goal == null || !goal.IsCleared) return;
            finished = true;
            if (selector.SelectedStage == 1)
            {
                ResultSummaryText = "기본 조작과 별 수집을 완료했습니다.\n2번 접촉 대전 스테이지가 열렸습니다.";
                return;
            }
            EarnedFlags = session.RetryCount == 0 ? 1 : 0;
            bool extra = false;
            switch (selector.SelectedStage)
            {
                case 2: extra = attractionSeconds >= 0.15f && repulsionSeconds >= 0.15f; break;
                case 3:
                    if (lesson != null && lesson.ComparisonChallengeSatisfied)
                    {
                        UsedSingleVariable = lesson.SingleVariableComparisonSatisfied;
                        extra = UsedSingleVariable;
                    }
                    break;
                case 4: extra = polarityChanges <= 4; break;
                case 5: extra = IsEfficientLaunchVoltage(efficientVoltage); break;
            }
            if (extra) EarnedFlags |= 2;
            int previousFlags = PlayerPrefs.GetInt(Key + ".Flags", 0) & 3;
            NewlyEarnedFlags = EarnedFlags & ~previousFlags;
            PlayerPrefs.SetInt(Key + ".Flags", previousFlags | EarnedFlags);
            int previous = PlayerPrefs.GetInt(Key + ".FewestRetries", -1);
            if (previous < 0 || session.RetryCount < previous)
            {
                ImprovedRetryRecord = true;
                PlayerPrefs.SetInt(Key + ".FewestRetries", session.RetryCount);
            }
            if (selector.SelectedStage == 5 && !float.IsInfinity(efficientVoltage))
            {
                float previousVoltage = PlayerPrefs.GetFloat(Key + ".BestVoltage", float.PositiveInfinity);
                if (float.IsNaN(previousVoltage) || efficientVoltage < previousVoltage)
                {
                    ImprovedVoltageRecord = true;
                    PlayerPrefs.SetFloat(Key + ".BestVoltage", efficientVoltage);
                }
            }
            PlayerPrefs.Save();
            string headline = NewlyEarnedFlags != 0 ? "선택 도전 신규 획득!"
                : ImprovedRetryRecord || ImprovedVoltageRecord ? "개인 기록 갱신!" : "완주 기록을 저장했습니다.";
            ResultSummaryText = headline + "\n"
                + "복귀 0회: " + ResultLabel(1) + " (이번 " + session.RetryCount + "회)\n"
                + ExtraChallengeName + ": " + ResultLabel(2) + "\n"
                + "최소 복귀 " + PlayerPrefs.GetInt(Key + ".FewestRetries", session.RetryCount) + "회"
                + (ImprovedRetryRecord ? " · 새 기록" : "");
            if (selector.SelectedStage == 5 && PlayerPrefs.HasKey(Key + ".BestVoltage"))
                ResultSummaryText += "\n최저 발사 " + PlayerPrefs.GetFloat(Key + ".BestVoltage").ToString("0.0") + " V"
                    + (ImprovedVoltageRecord ? " · 새 기록" : "");
                                                            
        }

        private string ResultLabel(int flag) => (EarnedFlags & flag) == 0
            ? ((PlayerPrefs.GetInt(Key + ".Flags", 0) & flag) != 0 ? "이번엔 미달 · 이전 기록 유지" : "이번엔 미달")
            : (NewlyEarnedFlags & flag) != 0 ? "신규 달성" : "달성 · 이전 기록 유지";

        private void Unbind()
        {
            if (observedPlayer != null) observedPlayer.StateChanged -= OnPolarityChanged;
            if (goal != null) goal.OnCleared.RemoveListener(RecordClear);
            if (voltageLauncher != null) voltageLauncher.LaunchedWithVoltage -= OnVoltageLaunched;
        }

        private void OnDestroy() => Unbind();
    }
}
