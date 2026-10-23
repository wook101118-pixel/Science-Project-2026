using UnityEngine;
using UnityEngine.InputSystem;

namespace sung_wook
{
    /// <summary>
    /// 레버로 점전하와 공의 거리를 바꾸고, 한 번의 입력을 한 번의 발사로 소비한다.
    /// 비교 실험을 위해 F = kqQ/r²를 정해진 시간 동안의 충격량 J = FΔt로 단순화한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DistanceLauncher2D : MonoBehaviour, IElectricForceProvider
    {
        [SerializeField] private ElectricBall2D ball;
        [SerializeField] private StageLever2D lever;
        [SerializeField] private Transform source;
        [SerializeField] private Transform farPoint;
        [SerializeField] private Transform nearPoint;
        [SerializeField] private Polarity sourcePolarity = Polarity.Positive;
        [SerializeField, Range(0f, 10f)] private float sourceMagnitude = 1f;
        [SerializeField] private float coulombConstant = 16f;
        [SerializeField] private float minimumDistance = 0.5f;
        [SerializeField] private float maximumForce = 80f;
        [SerializeField] private float pulseSeconds = 0.4f;
        [SerializeField] private float activationRadius = 3f;
        [SerializeField] private float cooldown = 0.5f;
        [SerializeField] private bool keyboardLaunch = true;

        private bool launchRequested;
        private float nextLaunchTime;

        public float SourceChargeMagnitude => sourceMagnitude;
        public ElectricForceSample LastLaunchSample { get; private set; }
        public bool HasLaunchSample { get; private set; }

        // 학습 제어기는 발사 직전에 실험 위치 고정을 해제하고, 성공 후 실제 샘플을 기록한다.
        public event System.Action Launching;
        public event System.Action<ElectricForceSample> Launched;

        /// <summary>분리 씬의 공·레버만 연결하고 저장된 거리·전하량·발사 설정은 보존한다.</summary>
        public void ConnectParticipants(ElectricBall2D targetBall, StageLever2D inputLever)
        {
            ball = targetBall;
            lever = inputLever;
        }

        private void Awake() => ValidateSourceCharge();
        private void Start() => ElectricStageParticipants2D.ResolveMissing(this, ref ball, ref lever);
        private void OnValidate() => ValidateSourceCharge();

        private void ValidateSourceCharge()
        {
            if (float.IsNaN(sourceMagnitude) || float.IsInfinity(sourceMagnitude))
            {
                sourceMagnitude = 1f;
            }
            sourceMagnitude = Mathf.Clamp(sourceMagnitude, 0f, 10f);
        }

        /// <summary>공의 전하와 독립적으로 전극의 전하량을 조절한다. 단위는 게임 단위다.</summary>
        public void SetSourceChargeMagnitude(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return;
            }
            sourceMagnitude = Mathf.Clamp(value, 0f, 10f);
        }

        /// <summary>전극 위치와 기존 레버를 연결한다. 별도의 공이나 입력 컨트롤러는 만들지 않는다.</summary>
        public void Configure(ElectricBall2D targetBall, StageLever2D inputLever,
            Transform electrode, Transform distantPoint, Transform closePoint)
        {
            ball = targetBall;
            lever = inputLever;
            source = electrode;
            farPoint = distantPoint;
            nearPoint = closePoint;
        }

        /// <summary>UI와 키보드는 동일한 요청을 사용하고 실제 물리 변경은 FixedUpdate에서 한다.</summary>
        public void RequestLaunch()
        {
            launchRequested = true;
        }

        /// <summary>새 실험의 재사용 대기 시간과 이전 발사 기록을 초기화한다.</summary>
        public void ResetLaunch()
        {
            launchRequested = false;
            nextLaunchTime = 0f;
            HasLaunchSample = false;
            LastLaunchSample = default;
        }

        private void OnDisable()
        {
            // 다른 스테이지로 이동한 뒤 남은 요청이 뒤늦게 실행되지 않게 한다.
            launchRequested = false;
        }

        private void Update()
        {
            if (keyboardLaunch && Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
            {
                RequestLaunch();
            }
        }

        private void FixedUpdate()
        {
            UpdateSourcePosition();
            ProcessLaunchRequest();
        }

        private void UpdateSourcePosition()
        {
            if (source != null && farPoint != null && nearPoint != null && lever != null)
            {
                source.position = Vector3.Lerp(farPoint.position, nearPoint.position, lever.Value);
            }
        }

        private void ProcessLaunchRequest()
        {
            if (!launchRequested)
            {
                return;
            }

            launchRequested = false;
            if (Time.fixedTime < nextLaunchTime || pulseSeconds <= 0f
                || !TryGetSample(out ElectricForceSample sample) || !sample.IsInRange
                || sample.Force.sqrMagnitude <= 0f)
            {
                return;
            }

            Launching?.Invoke();
            if (!ball.LaunchImpulse(sample.Force * pulseSeconds))
            {
                return;
            }

            nextLaunchTime = Time.fixedTime + cooldown;
            LastLaunchSample = sample;
            HasLaunchSample = true;
            ElectricSfxPlayer.Play(SfxCue.Launch);
            Launched?.Invoke(sample);
        }

        /// <summary>발사에 사용할 힘을 관찰한다. 중성·인력 상태에서는 이 발사대가 작동하지 않는다.</summary>
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
