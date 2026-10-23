using UnityEngine;
using UnityEngine.InputSystem;

namespace sung_wook
{
    /// <summary>
    /// 첫 실험의 실제 바운스·좌우 입력·전하 전환·수집을 관찰한다.
    /// 레버와 접촉 대전은 2번 실험에서 다루므로 이곳의 완료 조건에 포함하지 않는다.
    /// 플레이어의 위치·속도·물리 설정을 바꾸거나 UI와 지형을 생성하지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]
    public sealed class TutorialController2D : MonoBehaviour, IElectricStageClearRule
    {
        private const float MovementDistance = 0.5f;
        private const float LeverAngleChange = 15f;
        private ElectricStageSelector selector;
        private ElectricStageSession2D session;
        private PlayerMovement movement;
        private PlayerInputReader input;
        private global::LeverMover leverMover;
        private Vector2 previousPosition;
        private bool hasPreviousPosition;
        private float previousLeverAngle, dragStartAngle;
        private bool observingDrag;
        private int chargeCycleProgress;
        private int previousRetryCount;
        private string recentFeedback = "";
        private float feedbackUntil;
        private bool subscribed;

        public bool IsInitialized { get; private set; }
        public bool IsComplete { get; private set; }
        public bool HasBounced { get; private set; }
        public bool HasMovedLeft => LeftMovement >= MovementDistance;
        public bool HasMovedRight => RightMovement >= MovementDistance;
        public bool HasCycledCharge => chargeCycleProgress >= 3;
        public bool HasDraggedLever { get; private set; }
        public bool HasContactCharged { get; private set; }
        public bool HasCollectedStar => Ball != null && Ball.Stars > 0;
        public float LeftMovement { get; private set; }
        public float RightMovement { get; private set; }
        public int SpaceCycleProgress => chargeCycleProgress;
        public ElectricBall2D Ball => selector != null ? selector.Ball : null;
        public StageLever2D Lever => selector != null ? selector.Lever : null;
        public ContactCharger2D Charger { get; private set; }
        public ElectricStar2D Star { get; private set; }
        public ElectricGoal2D Goal { get; private set; }
        public Vector2 SpawnPosition => selector != null && selector.TryGetSpawnPosition(out Vector2 position) ? position : Vector2.zero;
        public Vector2 LeverPosition => Lever != null ? (Vector2)Lever.transform.position : Vector2.zero;
        public Vector2 ContactPosition => Charger != null ? Charger.GetComponent<Rigidbody2D>().position : Vector2.zero;
        public int StepIndex => !HasBounced ? 0 : !HasMovedLeft || !HasMovedRight ? 1
            : !HasCycledCharge ? 2 : !HasCollectedStar ? 3 : 4;

        public string CurrentTaskText
        {
            get
            {
                if (!IsInitialized) return "첫 실험을 준비하고 있습니다.";
                if (IsComplete) return "전하의 세 상태를 관찰했습니다. 다음 실험으로 이동하세요.";
                switch (StepIndex)
                {
                    case 0: return "공을 가만히 두고 바닥에서 튀어 오르는 모습을 보세요.";
                    case 1: return !HasMovedLeft ? "A로 왼쪽에 조금 이동해 보세요."
                        : "D로 오른쪽에도 조금 이동해 보세요.";
                    case 2:
                        if (chargeCycleProgress == 1) return "양전하 +를 관찰하세요. Space로 음전하를 선택합니다.";
                        if (chargeCycleProgress == 2) return "음전하 -를 관찰하세요. Space로 다시 중성을 선택합니다.";
                        return Ball.CurrentPolarity == Polarity.Neutral
                            ? "Space로 양전하를 선택하고 공의 색과 기호를 확인하세요."
                            : "Space로 중성부터 세 전하 상태를 차례로 관찰하세요.";
                    case 3: return "세 전하 상태를 관찰했습니다. 별을 모으세요.";
                    default: return "출구로 이동해 접촉 대전 실험을 시작하세요.";
                }
            }
        }

        public string StatusText
        {
            get
            {
                if (Ball == null) return "";
                string charge = Ball.SignedCharge > 0f ? "+" : Ball.SignedCharge < 0f ? "−" : "0";
                return "좌우 " + ((HasMovedLeft ? 1 : 0) + (HasMovedRight ? 1 : 0)) + "/2"
                    + " · Space " + chargeCycleProgress + "/3 · 별 " + Ball.Stars + "/1 · 전하 " + charge;
            }
        }

        public string ShortInstructions => "공은 바닥에서 자동으로 튀어 오릅니다.\n"
            + "A/D로 양쪽 이동 → Space로 0 → + → - → 0\n"
            + "색과 기호를 관찰한 뒤 별 → 출구\n"
            + "R 다시 실험 · Esc 메뉴";
        public string RecentFeedback => !string.IsNullOrEmpty(session != null ? session.RecoveryFeedback : "")
            ? session.RecoveryFeedback : Time.realtimeSinceStartup < feedbackUntil ? recentFeedback : "";

        private bool CanObserve => IsInitialized && !IsComplete && isActiveAndEnabled && Ball != null
            && Time.timeScale > 0f && !SceneTransitionController.IsBusy
            && !(GetComponent<ElectricStageProgression>()?.BlocksGameplayInput ?? false);

        private void Awake()
        {
            ResolveReferences();
            Subscribe();
        }
        private void OnEnable()
        {
            ResolveReferences();
            Subscribe();
        }
        private void Start()
        {
            if (selector == null || selector.SelectedStage != 1 || session == null || Star == null || Goal == null)
            {
                Debug.LogError("Stage 01 is missing its saved player, star, or exit.", this);
                enabled = false;
                return;
            }
            IsInitialized = session.IsInitialized;
        }

        private void ResolveReferences()
        {
            selector = GetComponent<ElectricStageSelector>();
            session = GetComponent<ElectricStageSession2D>();
            if (selector == null || selector.Ball == null) return;
            movement = selector.Ball.GetComponent<PlayerMovement>();
            input = selector.Ball.GetComponent<PlayerInputReader>();
            Charger = selector.GetStageComponent<ContactCharger2D>(1);
            Star = selector.GetStageComponent<ElectricStar2D>(1);
            Goal = selector.GetStageComponent<ElectricGoal2D>(1);
            if (selector.Lever != null) leverMover = selector.Lever.GetComponentInChildren<global::LeverMover>(true);
        }
        private void Subscribe()
        {
            if (subscribed || movement == null || Ball == null || Goal == null) return;
            movement.LandedOnGround += OnLanded;
            Ball.Player.StateChanged += OnChargeChanged;
            if (Charger != null) Charger.ChargedFromNeutral += OnContactCharged;
            Goal.OnCleared.AddListener(OnGoalCleared);
            subscribed = true;
        }
        private void OnDisable()
        {
            if (!subscribed) return;
            if (movement != null) movement.LandedOnGround -= OnLanded;
            if (Ball != null) Ball.Player.StateChanged -= OnChargeChanged;
            if (Charger != null) Charger.ChargedFromNeutral -= OnContactCharged;
            if (Goal != null) Goal.OnCleared.RemoveListener(OnGoalCleared);
            subscribed = false;
        }

        /// <summary>전체 재시작은 실제 실험과 함께 첫 조작 기록도 초기화한다. 발판 복귀는 호출하지 않는다.</summary>
        public void ResetTutorial()
        {
            HasBounced = HasDraggedLever = HasContactCharged = IsComplete = false;
            LeftMovement = RightMovement = 0f;
            chargeCycleProgress = 0;
            hasPreviousPosition = observingDrag = false;
            previousRetryCount = session != null ? session.RetryCount : 0;
            previousLeverAngle = leverMover != null ? leverMover.Angle : 0f;
            Report("색과 기호를 보며 전하의 세 상태를 관찰하세요.");
        }
        private void OnLanded(Vector2 position)
        {
            if (!CanObserve || HasBounced) return;
            HasBounced = true;
            Report("점프 키 없이 바닥에서 자동으로 튀어 오릅니다.");
        }
        private void OnChargeChanged(PlayerStateType state)
        {
            // A contact changes the same Player state, so require a real Space input edge here.
            if (!CanObserve || !HasMovedLeft || !HasMovedRight || HasCycledCharge
                || Keyboard.current == null || !Keyboard.current.spaceKey.wasPressedThisFrame) return;
            int sign = ElectricBall2D.ChargeSign(state);
            if (sign > 0) chargeCycleProgress = 1;
            else if (sign < 0 && chargeCycleProgress == 1) chargeCycleProgress = 2;
            else if (sign == 0 && chargeCycleProgress == 2) chargeCycleProgress = 3;
            else chargeCycleProgress = 0;
            Report(sign == 0 ? "중성 전하 0 · 전기력도 0입니다."
                : sign > 0 ? "양전하 + · 실제 힘과 모습이 함께 바뀝니다."
                : "음전하 − · 실제 힘과 모습이 함께 바뀝니다.");
        }
        private void OnContactCharged(ElectricBall2D chargedBall)
        {
            if (!CanObserve || !HasCycledCharge || !HasDraggedLever || chargedBall != Ball) return;
            HasContactCharged = true;
            Report("실제 접촉으로 대전됐습니다. 왼쪽 끝 별을 모으세요.");
        }
        private void OnGoalCleared()
        {
            if (Goal == null || !Goal.IsCleared) return;
            IsComplete = true;
        }
        private void Update()
        {
            if (!IsInitialized && session != null && session.IsInitialized) IsInitialized = true;
            if (!CanObserve)
            {
                observingDrag = false;
                return;
            }
            if (leverMover == null || Mouse.current == null) return;
            float angle = leverMover.Angle;
            if (HasCycledCharge && Mouse.current.leftButton.isPressed)
            {
                if (!observingDrag) { dragStartAngle = previousLeverAngle; observingDrag = true; }
                if (!HasDraggedLever && Mathf.Abs(angle - dragStartAngle) >= LeverAngleChange)
                {
                    HasDraggedLever = true;
                    Report("레버는 고정된 전극의 전하만 바꿉니다. 중성 공으로 전극에 닿으세요.");
                }
            }
            else observingDrag = false;
            previousLeverAngle = angle;
        }
        private void FixedUpdate()
        {
            if (!CanObserve || !HasBounced || input == null)
            {
                hasPreviousPosition = false;
                return;
            }
            Vector2 position = Ball.Body.position;
            if (session != null && session.RetryCount != previousRetryCount)
            {
                previousRetryCount = session.RetryCount;
                previousPosition = position;
                hasPreviousPosition = true;
                return;
            }
            if (hasPreviousPosition)
            {
                float delta = position.x - previousPosition.x;
                // Ignore checkpoint teleport distances; successful movement needs actual input and displacement.
                if (Mathf.Abs(delta) <= 0.5f)
                {
                    if (input.MoveVector.x < -0.1f && delta < 0f) LeftMovement += -delta;
                    if (input.MoveVector.x > 0.1f && delta > 0f) RightMovement += delta;
                }
            }
            previousPosition = position;
            hasPreviousPosition = true;
        }
        public bool CanClearGoal(ElectricBall2D candidate) => candidate != null && candidate == Ball
            && HasBounced && HasMovedLeft && HasMovedRight && HasCycledCharge && HasCollectedStar;

        private void Report(string message)
        {
            recentFeedback = message;
            feedbackUntil = Time.realtimeSinceStartup + 3f;
        }
    }
}
