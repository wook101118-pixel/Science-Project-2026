using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace sung_wook
{
    /// <summary>
    /// 레버 입력을 전압으로 축적하고, qV = ½mv²에 해당하는 속력으로 공을 발사한다.
    /// 반데그라프의 벨트·방전 과정은 생략한 학습용 저장 전압 모형이다.
    /// </summary>
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
        public UnityEvent OnTargetReached => onTargetReached;
        public UnityEvent OnLaunched => onLaunched;
        public bool HasLaunchSample { get; private set; }
        public float LastLaunchVoltage { get; private set; }

        /// <summary>성공한 발사에 사용한 소비 전 전압을 전달한다. 기존 Inspector 이벤트도 유지한다.</summary>
        public event System.Action<float> LaunchedWithVoltage;

        /// <summary>분리 씬의 참가자만 연결한다. 기존 충전 영역·게이지·UnityEvent를 유지한다.</summary>
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

        /// <summary>기존 공·레버와 발사 영역을 연결한다. 게이지 없이도 충전과 발사는 동작한다.</summary>
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

            // 게이지는 관찰용이다. UI 이벤트가 충전 상태를 역으로 바꾸지 않게 한다.
            voltageGauge.minValue = 0f;
            voltageGauge.maxValue = 1f;
            voltageGauge.wholeNumbers = false;
            voltageGauge.interactable = false;
            UpdateGauge();
        }

        /// <summary>키보드/UI의 요청을 모아 다음 물리 프레임에 한 번만 처리한다.</summary>
        public void RequestLaunch() => launchRequested = true;

        /// <summary>재시작 시 전압과 미처리 요청, 목표 도달 알림을 함께 초기화한다.</summary>
        public void ResetCharge()
        {
            Voltage = 0f;
            targetReported = false;
            launchRequested = false;
            nextLaunchTime = 0f;
            HasLaunchSample = false;
            LastLaunchVoltage = 0f;
            UpdateGauge();
        }

        private void OnDisable() => launchRequested = false;

        private void Update()
        {
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
                // 임계값 위에 머무르는 매 프레임마다 같은 이벤트를 발생시키지 않는다.
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
            if (Time.fixedTime < nextLaunchTime || ball == null || !ball.CanReceiveForce
                || launchZone == null || !launchZone.isActiveAndEnabled
                || lowerPlate == null || upperPlate == null)
            {
                return;
            }
            if (!launchZone.OverlapPoint(ball.Body.worldCenterOfMass))
            {
                return;
            }

            Vector2 axis = upperPlate.position - lowerPlate.position;
            if (axis.sqrMagnitude < 0.0001f)
            {
                return;
            }

            float launchVoltage = Voltage;
            float speed = ElectricMath.SpeedFromVoltage(ball.SignedCharge, launchVoltage, ball.Body.mass);
            if (speed <= 0f || !ball.LaunchImpulse(axis.normalized * speed * ball.Body.mass))
            {
                return;
            }

            // 영역 밖 요청이나 중성 공은 실패한다. 성공한 발사에만 저장 전압을 소비한다.
            // 소비 후의 0 V 대신 발사 순간의 값을 기록해야 목표 전압 달성을 정확히 판정한다.
            LastLaunchVoltage = launchVoltage;
            HasLaunchSample = true;
            Voltage = 0f;
            targetReported = false;
            nextLaunchTime = Time.fixedTime + cooldown;
            ElectricSfxPlayer.Play(SfxCue.Launch);
            LaunchedWithVoltage?.Invoke(launchVoltage);
            onLaunched.Invoke();
        }
    }
}
