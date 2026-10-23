using UnityEngine;
using UnityEngine.InputSystem;

namespace sung_wook
{
                 
                                                     
                                                            
                  
    [DisallowMultipleComponent]
    public sealed class DistanceLauncher2D : MonoBehaviour, IElectricForceProvider
    {
        [SerializeField] private ElectricBall2D ball;
        [SerializeField] private StageLever2D lever;
        [SerializeField] private Transform source;
        [SerializeField] private Transform farPoint;
        [SerializeField] private Transform nearPoint;
        [SerializeField] private Polarity sourcePolarity = Polarity.Positive;
        [SerializeField, Range(0f, 10f)] private float sourceMagnitude = 0.5f;
        [SerializeField] private float coulombConstant = 16f;
        [SerializeField] private float minimumDistance = 0.5f;
        [SerializeField] private float maximumForce = 80f;
        [SerializeField] private float pulseSeconds = 0.4f;
        [SerializeField] private float activationRadius = 3f;
        [SerializeField] private float cooldown = 0.5f;
        [SerializeField] private bool keyboardLaunch = true;

        private bool launchRequested;
        private float nextLaunchTime;
        private float previousLeverValue;
        private bool hasLeverValue;
        private System.Func<string> comparisonLaunchBlockReason;

        public float SourceChargeMagnitude => sourceMagnitude;
        public ElectricForceSample LastLaunchSample { get; private set; }
        public bool HasLaunchSample { get; private set; }
        public string LastCommandFeedback { get; private set; } = "";
        public float LastCommandFeedbackTime { get; private set; } = float.NegativeInfinity;
        public bool CanLaunch => string.IsNullOrEmpty(LaunchBlockReason);

                                                                   
        public string LaunchBlockReason
        {
            get
            {
                if (!CanAcceptLaunch) return "일시정지 또는 화면 전환 중입니다.";
                if (Time.fixedTime < nextLaunchTime) return "발사 대기 중 · 잠시 후 다시 누르세요.";
                if (pulseSeconds <= 0f || !TryGetSample(out ElectricForceSample sample))
                    return "발사 장치가 준비되지 않았습니다.";
                if (!sample.IsInRange) return "발사 범위 밖 · Backspace로 복귀하세요.";
                if (sample.Force.sqrMagnitude <= 0f)
                    return sample.SignedCharge == 0f ? "중성은 발사할 수 없습니다 · 양전하를 선택하세요."
                        : sample.SourceCharge == 0f ? "전극 전하가 0입니다 · 레버를 움직이세요."
                        : sample.SignedCharge * sample.SourceCharge <= 0f
                            ? "공과 전극을 같은 부호로 맞추세요."
                            : "전기력이 0입니다 · 전하량을 확인하세요.";
                return comparisonLaunchBlockReason?.Invoke() ?? "";
            }
        }

                                                               
        public void SetComparisonLaunchRequirement(System.Func<string> requirement)
            => comparisonLaunchBlockReason = requirement;

                                                           
        public event System.Action Launching;
        public event System.Action<ElectricForceSample> Launched;

                                                                       
        public void ConnectParticipants(ElectricBall2D targetBall, StageLever2D inputLever)
        {
            ball = targetBall;
            lever = inputLever;
            RememberLeverValue();
        }

        private void Awake() => ValidateSourceCharge();
        private void Start()
        {
            ElectricStageParticipants2D.ResolveMissing(this, ref ball, ref lever);
            RememberLeverValue();
        }
        private void OnValidate() => ValidateSourceCharge();

        private void ValidateSourceCharge()
        {
            if (float.IsNaN(sourceMagnitude) || float.IsInfinity(sourceMagnitude))
            {
                sourceMagnitude = 1f;
            }
            sourceMagnitude = Mathf.Clamp(sourceMagnitude, 0f, 10f);
        }

                                                                      
        public void SetSourceChargeMagnitude(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return;
            }
            sourceMagnitude = Mathf.Clamp(value, 0f, 10f);
                                              
            RememberLeverValue();
        }

                                                                            
        public void Configure(ElectricBall2D targetBall, StageLever2D inputLever,
            Transform electrode, Transform distantPoint, Transform closePoint)
        {
            ball = targetBall;
            lever = inputLever;
            source = electrode;
            farPoint = distantPoint;
            nearPoint = closePoint;
            RememberLeverValue();
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

                                                                
        public void ResetLaunch()
        {
            launchRequested = false;
            nextLaunchTime = 0f;
            HasLaunchSample = false;
            LastLaunchSample = default;
            LastCommandFeedback = "";
            LastCommandFeedbackTime = float.NegativeInfinity;
        }

        private void OnDisable()
        {
                                                    
            launchRequested = false;
        }

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
            UpdateSourceCharge();
            ProcessLaunchRequest();
        }

        private void RememberLeverValue()
        {
            hasLeverValue = lever != null;
            if (hasLeverValue) previousLeverValue = lever.Value;
        }

        private void UpdateSourceCharge()
        {
            if (lever == null) return;
            float value = lever.Value;
            if (!hasLeverValue || !Mathf.Approximately(value, previousLeverValue))
                sourceMagnitude = Mathf.Lerp(0.5f, 1f, value);
            previousLeverValue = value;
            hasLeverValue = true;
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
            string blocked = LaunchBlockReason;
            if (!string.IsNullOrEmpty(blocked))
            {
                ReportCommand(blocked);
                return;
            }
            if (!TryGetSample(out ElectricForceSample sample)) return;

            Launching?.Invoke();
            if (!ball.LaunchImpulse(sample.Force * pulseSeconds))
            {
                ReportCommand("발사 불가 · Backspace로 복귀하세요.");
                return;
            }

            nextLaunchTime = Time.fixedTime + cooldown;
            LastLaunchSample = sample;
            HasLaunchSample = true;
            ElectricSfxPlayer.Play(SfxCue.Launch);
            ReportCommand("발사 성공 · 착지할 곳을 확인하세요.");
            Launched?.Invoke(sample);
        }

        private void ReportCommand(string message)
        {
            LastCommandFeedback = message;
            LastCommandFeedbackTime = Time.realtimeSinceStartup;
        }

                                                                            
        public bool TryGetSample(out ElectricForceSample sample)
        {
            sample = default;
            if (ball == null || ball.Body == null || source == null)
            {
                return false;
            }

            sample = ElectricForceSample.PointCharge(ball.Body.worldCenterOfMass, source.position,
                ball.SignedCharge, (int)sourcePolarity * sourceMagnitude, coulombConstant,
                minimumDistance, maximumForce, isActiveAndEnabled && ball.CanReceiveForce,
                activationRadius, true);
            return true;
        }
    }
}
