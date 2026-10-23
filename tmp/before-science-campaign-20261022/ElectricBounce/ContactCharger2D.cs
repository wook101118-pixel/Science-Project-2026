using UnityEngine;
using System;

namespace sung_wook
{
    /// <summary>고정 전극의 전하만 레버로 조절하며, 공이 실제로 닿으면 대전시킨다.</summary>
    [RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D))]
    [DisallowMultipleComponent]
    public sealed class ContactCharger2D : MonoBehaviour, IElectricForceProvider
    {
        [SerializeField] 
        private ElectricBall2D     ball;
        [SerializeField] 
        private StageLever2D       lever;
        [SerializeField] 
        private Transform          restPoint;
        [SerializeField] 
        private Transform          contactPoint;
        [SerializeField] 
        private Polarity           polarity          = Polarity.Positive;
        [SerializeField, Range(0f, 10f)]
        private float              chargeMagnitude   = 1f;
        [SerializeField, Min(0.0001f), Tooltip("Unity 거리 1에 해당하는 실제 거리(m). 기본값: 1 Unity 단위 = 1 m.")]
        private float              metersPerWorldUnit = 1f;
        [SerializeField, Min(0.000000001f), Tooltip("전하량 1에 해당하는 C. 기본값 0.00003 C = 30 μC.")]
        private float              coulombsPerChargeUnit = 0.00003f;
        [SerializeField, Min(1f), Tooltip("진공/공기의 근사는 1. 유전체 안에서는 해당 상대 유전율을 사용합니다.")]
        private float              relativePermittivity = 1f;
        [SerializeField] 
        private float              minimumDistance   = 0.5f;
        [SerializeField] 
        private float              maximumForce      = 60f; // N. 겹침 등 비정상 근접에서만 적용하는 안전 상한.
        [SerializeField, Min(0f), Tooltip("0이면 거리 제한 없이 역제곱 법칙을 적용합니다.")]
        private float              influenceRadius   = 0f;
        private Rigidbody2D        electrodeBody;

      
        public event Action<ElectricBall2D> ChargedFromNeutral;

        /// <summary>레버와 전극 표시, 실제 전하 계산이 공유하는 양방향 입력이다.</summary>
        public float ChargeFraction => chargeMagnitude > 0f
            ? (lever != null ? lever.SignedValue : (int)polarity) : 0f;
        public float SignedCharge => ChargeFraction * chargeMagnitude;
        public Polarity CurrentPolarity => SignedCharge > 0f ? Polarity.Positive
            : SignedCharge < 0f ? Polarity.Negative : Polarity.Neutral;
        
        public void ConnectParticipants(ElectricBall2D targetBall, StageLever2D inputLever)
        {
            ball = targetBall;
            lever = inputLever;
        }
        
        public void Configure(ElectricBall2D targetBall, StageLever2D inputLever,
            Transform restingPoint, Transform touchingPoint, Polarity sourcePolarity = Polarity.Positive)
        {
            ball = targetBall;
            lever = inputLever;
            restPoint = restingPoint;
            contactPoint = touchingPoint;
            polarity = sourcePolarity;
        }

        private void Awake()
        {
            ValidateSettings();
            electrodeBody = GetComponent<Rigidbody2D>();
        }

        private void OnValidate() => ValidateSettings();

        private void ValidateSettings()
        {
            chargeMagnitude = Mathf.Min(10f, FiniteAtLeast(chargeMagnitude, 0f, 1f));
            metersPerWorldUnit = FiniteAtLeast(metersPerWorldUnit, 0.0001f, 1f);
            coulombsPerChargeUnit = FiniteAtLeast(coulombsPerChargeUnit, 0.000000001f, 0.00003f);
            relativePermittivity = FiniteAtLeast(relativePermittivity, 1f, 1f);
            minimumDistance = FiniteAtLeast(minimumDistance, 0.01f, 0.5f);
            maximumForce = FiniteAtLeast(maximumForce, 0f, 60f);
            influenceRadius = FiniteAtLeast(influenceRadius, 0f, 0f);
        }

        private static float FiniteAtLeast(float value, float minimum, float fallback)
            => float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Max(minimum, value);

        private void Start()
        {
            ElectricStageParticipants2D.ResolveMissing(this, ref ball, ref lever);  
        } 

        private void FixedUpdate()
        {
            if (TryGetSample(out ElectricForceSample sample) && sample.IsInRange)
            {
                ball.AddElectricForce(sample.Force);
            }
        }

        /// <summary>읽기 전용 관찰. UI가 여러 번 호출해도 힘이 추가되거나 대전되지 않는다.</summary>
        public bool TryGetSample(out ElectricForceSample sample)
        {
            sample = default;
            if (electrodeBody == null)
            {
                electrodeBody = GetComponent<Rigidbody2D>();
            }
            if (ball == null || ball.Body == null || electrodeBody == null)
            {
                return false;
            }

            sample = ElectricForceSample.PointChargeSI(ball.Body.worldCenterOfMass,
                electrodeBody.worldCenterOfMass, ball.SignedCharge, SignedCharge,
                metersPerWorldUnit, coulombsPerChargeUnit, relativePermittivity,
                minimumDistance, maximumForce,
                isActiveAndEnabled && ball.CanReceiveForce, influenceRadius);
            return true;
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            ChargeOnContact(collision);
        }

        private void OnCollisionStay2D(Collision2D collision)
        {
            // 이미 접촉한 채로 재시작/중성화한 경우에도 전극과의 전하 상태를 일치시킨다.
            ChargeOnContact(collision);
        }

        private void ChargeOnContact(Collision2D collision)
        {
            if (!isActiveAndEnabled || ball == null || !ball.CanReceiveForce)
            {
                return;
            }

            ElectricBall2D touchedBall = collision.collider.GetComponentInParent<ElectricBall2D>();
            if (touchedBall == ball)
            {
                bool wasNeutral = ball.CurrentPolarity == Polarity.Neutral;
                // 전원에 연결된 전극과 접촉한 공은 현재 전극의 부호와 충전량을 따른다.
                // 접촉을 떠난 공의 전하는 이후 레버를 바꿔도 보존된다.
                ball.SetChargeMagnitude(Mathf.Abs(SignedCharge));
                ball.SetPolarity(CurrentPolarity);
                if (wasNeutral && ball.CurrentPolarity != Polarity.Neutral
                    && Mathf.Abs(ball.SignedCharge) > 0f)
                {
                    ChargedFromNeutral?.Invoke(ball);
                }
            }
        }
    }
}
