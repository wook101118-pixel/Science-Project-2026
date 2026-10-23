using UnityEngine;
using System;

namespace sung_wook
{
                                                                 
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
        private float              maximumForce      = 60f;                                 
        [SerializeField, Min(0f), Tooltip("0이면 거리 제한 없이 역제곱 법칙을 적용합니다.")]
        private float              influenceRadius   = 0f;
        private Rigidbody2D        electrodeBody;

      
        public event Action<ElectricBall2D> ChargedFromNeutral;

                                                                  
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
