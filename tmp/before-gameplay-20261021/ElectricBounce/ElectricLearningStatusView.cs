using UnityEngine;
using UnityEngine.UI;

namespace sung_wook
{
    /// <summary>
    /// 씬에 미리 배치한 학습 카드·완료 배지·효과음을 기존 실험 상태에 연결합니다.
    /// 오브젝트 생성, 외형 설정, 입력 처리, 물리나 학습 판정 변경은 하지 않습니다.
    /// 참고: 고박사 비행 슈팅 #02, 7:44 UI 갱신 / 29:45 AudioSource 초기화.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ElectricLearningStatusView : MonoBehaviour
    {
        [SerializeField]
        private ElectricLearningController2D learning;
        [SerializeField]
        private ElectricStageSelector selector;
        [SerializeField]
        private Text progressText;
        [SerializeField]
        private Text measurementText;
        [SerializeField]
        private Text chargeText;
        [SerializeField, Tooltip("{0}: 전하 기호, {1}: 상태 이름. 위치·폰트·크기는 Live Charge의 Text에서 편집합니다.")]
        private string chargeFormat = "현재 전하 {0} {1}";
        [SerializeField]
        private GameObject learningPending;
        [SerializeField]
        private GameObject learningComplete;
        [SerializeField]
        private GameObject starPending;
        [SerializeField]
        private GameObject starComplete;
        [SerializeField]
        private GameObject exitLocked;
        [SerializeField]
        private GameObject exitReady;
        [SerializeField]
        private GameObject clearedBadge;
        [SerializeField]
        private AudioClip recordSound;
        [SerializeField]
        private AudioClip collectSound;
        [SerializeField]
        private AudioClip completeSound;

        private AudioSource audioSource;
        private DistanceLauncher2D distanceLauncher;
        private bool hasSnapshot;
        private bool previousLearning;
        private bool previousBaseline;
        private bool previousChargeReady;
        private bool previousClear;
        private int previousStars;

        /// <summary>분리 씬의 기존 Text·배지를 현재 세션에 연결한다. UI 배치와 스타일은 변경하지 않는다.</summary>
        public void Configure(ElectricStageSelector owner)
        {
            selector = owner;
            learning = owner.GetComponent<ElectricLearningController2D>();
            distanceLauncher = owner.GetStageComponent<DistanceLauncher2D>(3);
            hasSnapshot = false;
        }

        private void Awake()
        {
            // 영상의 컴포넌트 초기화 방식을 따르되, 기존 실험 참조는 Inspector 연결을 사용합니다.
            audioSource = GetComponent<AudioSource>();
            if (selector != null)
            {
                distanceLauncher = selector.GetStageComponent<DistanceLauncher2D>(3);
            }
            ResolveLearning();
        }

        private void OnEnable()
        {
            // 스테이지를 고를 때 이미 완료된 기록을 새 성공 사건처럼 재생하지 않습니다.
            hasSnapshot = false;
        }

        private void OnDisable()
        {
            if (audioSource != null)
            {
                audioSource.Stop();
            }
        }

        private void Update()
        {
            RefreshDisplay();
        }

        /// <summary>읽기 전용 학습 상태를 표시합니다. 검사 도구에서도 같은 표시 경로를 사용합니다.</summary>
        public void RefreshDisplay()
        {
            ResolveLearning();
            if (learning == null || selector == null || !learning.CanDisplay)
            {
                return;
            }

            bool learned = learning.CurrentLearningGoalSatisfied;
            int stars = learning.CollectedStars;
            bool cleared = learning.IsStageCleared;
            bool ready = learned && stars > 0;
            bool chargeReady = learning.SelectedStage == 5
                && learning.CurrentVoltage >= learning.RequiredLaunchVoltage;

            ShowPair(learningPending, learningComplete, learned);
            ShowPair(starPending, starComplete, stars > 0);
            ShowPair(exitLocked, exitReady, ready);
            Show(clearedBadge, cleared);

            SetText(progressText, BuildProgress(learned, ready, cleared, chargeReady));
            SetText(measurementText, BuildMeasurement());
            SetText(chargeText, BuildCharge());

            // 동일 상태를 매 프레임 표시해도 소리는 상태가 바뀐 순간에만 한 번 재생합니다.
            if (hasSnapshot)
            {
                if (ElectricSfxPlayer.IsAvailable)
                {
                    // 별과 출구 효과음은 실제 수집·통과 사건에서 재생합니다.
                    if (!cleared)
                    {
                        if (learned && !previousLearning)
                            ElectricSfxPlayer.Play(SfxCue.LearningComplete);
                        else if ((learning.HasBaseline && !previousBaseline)
                            || (chargeReady && !previousChargeReady))
                            ElectricSfxPlayer.Play(SfxCue.Record);
                    }
                }
                else if (cleared && !previousClear)
                {
                    PlaySound(completeSound);
                }
                else if ((learned && !previousLearning) || stars > previousStars)
                {
                    PlaySound(collectSound);
                }
                else if ((learning.HasBaseline && !previousBaseline)
                    || (chargeReady && !previousChargeReady))
                {
                    PlaySound(recordSound);
                }
            }

            previousLearning = learned;
            previousBaseline = learning.HasBaseline;
            previousStars = stars;
            previousChargeReady = chargeReady;
            previousClear = cleared;
            hasSnapshot = true;
        }

        /// <summary>선택기가 Awake에서 연결하는 기존 학습 컴포넌트를 한 번 찾아 재사용합니다.</summary>
        private void ResolveLearning()
        {
            if (learning == null && selector != null)
            {
                learning = selector.GetComponent<ElectricLearningController2D>();
            }
        }

        private string BuildProgress(bool learned, bool ready, bool cleared, bool chargeReady)
        {
            if (cleared) return "실험 성공!  R 다시 시작";
            if (ready) return "출구 준비 완료 · 문으로 이동하세요";
            if (learned) return "학습 완료 · 별을 모아 주세요";

            switch (learning.SelectedStage)
            {
                case 2: return "레버로 전극을 공에 접촉";
                case 3: return learning.HasBaseline
                    ? "조건을 바꾼 뒤 E로 비교 발사"
                    : "같은 출발점에서 B로 기준 저장";
                case 4: return "X 음전하 · 전기장을 아래로";
                case 5: return chargeReady
                    ? "50 V 도달 · 전극 사이에서 E 발사"
                    : "레버로 50 V 이상 충전하세요";
                default: return "실험을 준비하세요";
            }
        }

        private string BuildMeasurement()
        {
            switch (learning.SelectedStage)
            {
                case 2:
                    if (selector.TryGetCurrentForceSample(out ElectricForceSample contact)
                        && contact.UsesSIUnits)
                        return $"거리 {contact.DistanceMeters:0.000} m   힘 {contact.ForceNewtons.magnitude:0.000} N\n"
                            + $"전극 {contact.SourceChargeCoulombs * 1e6f:+0.0;−0.0;0} μC"
                            + (contact.IsLimited ? " · 근접 안전 제한" : " · 같은 부호는 척력, 다른 부호는 인력");
                    return learning.ContactChallengeSatisfied
                        ? "접촉 대전 확인\n같은 전하끼리 밀어내요"
                        : "접촉 전 0 → 접촉 후 + 또는 −\n전극의 움직임을 관찰하세요";
                case 3:
                    if (learning.HasResult)
                    {
                        return "거리  " + learning.BaselineSample.Distance.ToString("0.00")
                            + " → " + learning.ResultSample.Distance.ToString("0.00")
                            + "\n힘  " + learning.BaselineSample.ForceMagnitude.ToString("0.0")
                            + " → " + learning.ResultSample.ForceMagnitude.ToString("0.0");
                    }
                    if (learning.HasBaseline && selector.TryGetCurrentForceSample(out ElectricForceSample current))
                    {
                        return "기준 r " + learning.BaselineSample.Distance.ToString("0.00")
                            + " / 현재 " + current.Distance.ToString("0.00")
                            + "\n힘 " + learning.BaselineSample.ForceMagnitude.ToString("0.0")
                            + " → " + current.ForceMagnitude.ToString("0.0");
                    }
                    return "기준 저장 → 조건 변경 → 발사\n관측 수치는 게임 단위입니다";
                case 4:
                    if (selector.TryGetCurrentForceSample(out ElectricForceSample sample))
                    {
                        return "전기장  " + Direction(sample.Field.y)
                            + "   힘  " + Direction(sample.Force.y)
                            + "\n음전하는 장과 반대 방향으로 힘 받아요";
                    }
                    return "전기장 E와 힘 F를 비교하세요";
                case 5:
                    return "현재  " + learning.CurrentVoltage.ToString("0.0") + " V / 목표 50 V\n"
                        + (learning.VoltageChallengeSatisfied
                            ? "확인한 발사  " + learning.LastLaunchVoltage.ToString("0.0") + " V"
                            : "충전 뒤 실제 발사가 필요해요");
                default:
                    return "";
            }
        }

        private string BuildCharge()
        {
            // 외부 도구로 전하량만 0으로 바꾼 경우에도 선택 상태와 실제 수치를 구분합니다.
            // 따라서 기호와 이름은 물리 계산값이 아닌 기존 Player의 상태를 기준으로 표시합니다.
            PlayerStateType chargeState = selector.Ball.Player.CurrentState;
            string sign = chargeState == PlayerStateType.Positive ? "+"
                : chargeState == PlayerStateType.Negative ? "−" : "0";
            string state = chargeState == PlayerStateType.Positive ? "양전하"
                : chargeState == PlayerStateType.Negative ? "음전하" : "중성";
            string label = string.Format(chargeFormat, sign, state);
            float charge = selector.Ball.SignedCharge;
            if (selector.TryGetCurrentForceSample(out ElectricForceSample sample) && sample.UsesSIUnits)
                return label + " · q " + (sample.SignedChargeCoulombs * 1e6f).ToString("+0.#;−0.#;0") + " μC";
            if (learning.SelectedStage == 3 && distanceLauncher != null)
            {
                return label + " · q " + charge.ToString("+0.#;−0.#;0")
                    + " / Q +" + distanceLauncher.SourceChargeMagnitude.ToString("0.#");
            }
            return label;
        }

        private static string Direction(float value)
        {
            return value > 0.001f ? "↑" : value < -0.001f ? "↓" : "0";
        }

        private static void ShowPair(GameObject pending, GameObject complete, bool value)
        {
            Show(pending, !value);
            Show(complete, value);
        }

        private static void Show(GameObject target, bool value)
        {
            if (target != null && target.activeSelf != value)
            {
                target.SetActive(value);
            }
        }

        private static void SetText(Text target, string value)
        {
            if (target != null && target.text != value)
            {
                target.text = value;
            }
        }

        private void PlaySound(AudioClip clip)
        {
            if (audioSource != null && clip != null)
            {
                audioSource.PlayOneShot(clip);
            }
        }
    }
}
