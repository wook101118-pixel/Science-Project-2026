using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace sung_wook
{
                 
                                                       
                                            
                  
    [DisallowMultipleComponent]
    public sealed class VoltageLauncher2D : MonoBehaviour
    {
        [SerializeField] private ElectricBall2D ball;
        [SerializeField] private StageLever2D lever;
        [SerializeField] private BoxCollider2D launchZone;
        [SerializeField] private Transform lowerPlate;
        [SerializeField] private Transform upperPlate;
        [SerializeField] private Slider voltageGauge;
        [SerializeField, Min(0.01f)] private float maximumVoltage = 100f;
        [SerializeField, Min(0f)] private float chargeRate = 25f;
        [SerializeField, Min(0f)] private float leakRate;
        [SerializeField, Min(0f)] private float targetVoltage = 50f;
        [SerializeField, Min(0f)] private float cooldown = 0.5f;
        [SerializeField] private bool keyboardLaunch = true;
        [SerializeField] private UnityEvent onTargetReached = new UnityEvent();
        [SerializeField] private UnityEvent onLaunched = new UnityEvent();

        private bool targetReported;
        private bool launchRequested;
        private float nextLaunchTime;

        public float Voltage { get; private set; }
        public bool IsAtTarget => Voltage >= targetVoltage;
        public float NormalizedVoltage => Voltage / Mathf.Max(0.01f, maximumVoltage);
        public float TargetVoltage => targetVoltage;
        public BoxCollider2D LaunchZone => launchZone;
        public Slider VoltageGauge => voltageGauge;
        public bool IsInsideLaunchZone => ball != null && ball.Body != null && launchZone != null
            && launchZone.isActiveAndEnabled && launchZone.OverlapPoint(ball.Body.worldCenterOfMass);
        public bool CanLaunch => string.IsNullOrEmpty(LaunchBlockReason);
        public string LaunchBlockReason
        {
            get
            {
                if (!CanAcceptLaunch) return "일시정지 또는 화면 전환 중입니다.";
                if (Time.fixedTime < nextLaunchTime) return "발사 대기 중 · 잠시 후 다시 누르세요.";
                if (ball == null || ball.Body == null || !ball.CanReceiveForce || launchZone == null
                    || !launchZone.isActiveAndEnabled || lowerPlate == null || upperPlate == null
                    || (upperPlate.position - lowerPlate.position).sqrMagnitude < 0.0001f)
                    return "발사 장치가 준비되지 않았습니다.";
                if (!IsInsideLaunchZone) return "발사 영역 밖 · 표시된 전극 사이로 이동하세요.";
                if (ball.SignedCharge <= 0f) return "양전하가 필요해요 · + 버튼 또는 Space로 바꾸세요.";
                if (Voltage <= 0f || Voltage < targetVoltage)
                    return $"충전 중 · 레버를 왼쪽으로 당겨 {targetVoltage:0} V까지 충전하세요.";
                return "";
            }
        }
        public UnityEvent OnTargetReached => onTargetReached;
        public UnityEvent OnLaunched => onLaunched;
        public bool HasLaunchSample { get; private set; }
        public float LastLaunchVoltage { get; private set; }
        public string LastCommandFeedback { get; private set; } = "";
        public float LastCommandFeedbackTime { get; private set; } = float.NegativeInfinity;

                                                                                 
        public event System.Action<float> LaunchedWithVoltage;

                                                                              
        public void ConnectParticipants(ElectricBall2D targetBall, StageLever2D inputLever)
        {
            ball = targetBall;
            lever = inputLever;
        }

        private void Awake()
        {
            if (launchZone != null)
            {
                launchZone.isTrigger = true;
            }
            ConfigureGauge();
        }

        private void Start() => ElectricStageParticipants2D.ResolveMissing(this, ref ball, ref lever);

                                                                          
        public void Configure(ElectricBall2D targetBall, StageLever2D inputLever,
            BoxCollider2D zone, Transform bottomPlate, Transform topPlate, Slider gauge = null)
        {
            ball = targetBall;
            lever = inputLever;
            launchZone = zone;
            lowerPlate = bottomPlate;
            upperPlate = topPlate;
            voltageGauge = gauge;
            if (launchZone != null)
            {
                launchZone.isTrigger = true;
            }
            ConfigureGauge();
        }

        private void ConfigureGauge()
        {
            if (voltageGauge == null)
            {
                return;
            }

                                                        
            voltageGauge.minValue = 0f;
            voltageGauge.maxValue = 1f;
            voltageGauge.wholeNumbers = false;
            voltageGauge.interactable = false;
            UpdateGauge();
        }

                                                                   
        public void RequestLaunch()
        {
            if (!CanAcceptLaunch)
            {
                launchRequested = false;
                ReportCommand(LaunchBlockReason);
                return;
            }
            launchRequested = true;
        }

        private bool CanAcceptLaunch => isActiveAndEnabled && Time.timeScale > 0f
            && !SceneTransitionController.IsBusy;

                                                 
        internal void CancelPendingLaunch() => launchRequested = false;

                                                                    
        public void ResetCharge()
        {
            Voltage = 0f;
            targetReported = false;
            launchRequested = false;
            nextLaunchTime = 0f;
            HasLaunchSample = false;
            LastLaunchVoltage = 0f;
            LastCommandFeedback = "";
            LastCommandFeedbackTime = float.NegativeInfinity;
            UpdateGauge();
        }

        private void OnDisable() => launchRequested = false;

        private void Update()
        {
            if (!CanAcceptLaunch)
            {
                launchRequested = false;
                return;
            }
            if (keyboardLaunch && Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
            {
                RequestLaunch();
            }
        }

        private void FixedUpdate()
        {
            IntegrateCharge();
            ReportTargetReached();
            ProcessLaunchRequest();
            UpdateGauge();
        }

        private void IntegrateCharge()
        {
            float input = lever != null ? lever.Value : 0f;
            float rate = input > 0.01f ? chargeRate * input : -leakRate;
            Voltage = Mathf.Clamp(Voltage + rate * Time.fixedDeltaTime, 0f, maximumVoltage);
        }

        private void ReportTargetReached()
        {
            if (IsAtTarget && !targetReported)
            {
                                                         
                targetReported = true;
                onTargetReached.Invoke();
            }
            if (!IsAtTarget)
            {
                targetReported = false;
            }
        }

        private void ProcessLaunchRequest()
        {
            if (!CanAcceptLaunch)
            {
                launchRequested = false;
                return;
            }
            if (!launchRequested)
            {
                return;
            }

            launchRequested = false;
            TryLaunch();
        }

        private void UpdateGauge()
        {
            if (voltageGauge != null)
            {
                voltageGauge.SetValueWithoutNotify(NormalizedVoltage);
            }
        }

        private void TryLaunch()
        {
            string blocked = LaunchBlockReason;
            if (!string.IsNullOrEmpty(blocked))
            {
                ReportCommand(blocked);
                return;
            }

            Vector2 axis = upperPlate.position - lowerPlate.position;

            float launchVoltage = Voltage;
            float speed = ElectricMath.SpeedFromVoltage(ball.SignedCharge, launchVoltage, ball.Body.mass);
            if (speed <= 0f || !ball.LaunchImpulse(axis.normalized * speed * ball.Body.mass))
            {
                ReportCommand("발사 불가 · 충전 상태를 확인하세요.");
                return;
            }

                                                          
                                                              
            LastLaunchVoltage = launchVoltage;
            HasLaunchSample = true;
            Voltage = 0f;
            targetReported = false;
            nextLaunchTime = Time.fixedTime + cooldown;
            ElectricSfxPlayer.Play(SfxCue.Launch);
            ReportCommand($"{launchVoltage:0.0} V 발사 · 착지할 곳을 확인하세요.");
            LaunchedWithVoltage?.Invoke(launchVoltage);
            onLaunched.Invoke();
        }

        private void ReportCommand(string message)
        {
            LastCommandFeedback = message;
            LastCommandFeedbackTime = Time.realtimeSinceStartup;
        }
    }
}
