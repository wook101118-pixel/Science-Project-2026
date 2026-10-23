using UnityEngine;

namespace sung_wook
{
    /// <summary>
    /// 평행판 사이의 균일 전기장. 레버는 판의 극성을 바꾸고 공의 부호가 힘의 방향을 정한다.
    /// 전기장 E는 양전하에서 음전하로 향하며, 공에는 F = qE를 적용한다.
    /// </summary>
    [RequireComponent(typeof(BoxCollider2D))]
    [DisallowMultipleComponent]
    public sealed class UniformField2D : MonoBehaviour, IElectricForceProvider
    {
        [SerializeField] private ElectricBall2D ball;
        [SerializeField] private StageLever2D lever;
        [SerializeField] private Transform lowerPlate;
        [SerializeField] private Transform upperPlate;
        [SerializeField] private SpriteRenderer lowerRenderer;
        [SerializeField] private SpriteRenderer upperRenderer;
        [SerializeField, Min(0f)] private float voltage = 40f;
        [SerializeField, Min(0f)] private float maximumForce = 100f;

        private BoxCollider2D zone;
        private bool lowerIsPositive;
        private Vector2 fieldDirection;

        /// <summary>분리 씬의 공·레버만 연결한다. 판 간격·렌더러·전압은 변경하지 않는다.</summary>
        public void ConnectParticipants(ElectricBall2D targetBall, StageLever2D inputLever)
        {
            ball = targetBall;
            lever = inputLever;
        }

        public Vector2 FieldDirection
        {
            get
            {
                RefreshFieldDirection();
                return fieldDirection;
            }
        }

        /// <summary>기존 공·레버와 평행판을 연결한다. 렌더러는 선택 사항이며 물리 계산과 독립적이다.</summary>
        public void Configure(ElectricBall2D targetBall, StageLever2D inputLever,
            Transform bottomPlate, Transform topPlate, SpriteRenderer bottomVisual = null,
            SpriteRenderer topVisual = null, float fieldVoltage = 40f)
        {
            ball = targetBall;
            lever = inputLever;
            lowerPlate = bottomPlate;
            upperPlate = topPlate;
            lowerRenderer = bottomVisual;
            upperRenderer = topVisual;
            voltage = Mathf.Max(0f, fieldVoltage);
        }

        private void Awake()
        {
            zone = GetComponent<BoxCollider2D>();
            zone.isTrigger = true;
        }

        private void Start() => ElectricStageParticipants2D.ResolveMissing(this, ref ball, ref lever);

        private void FixedUpdate()
        {
            if (!RefreshFieldDirection())
            {
                return;
            }

            UpdatePlateVisuals();
            if (TryGetSample(out ElectricForceSample sample) && sample.IsInRange)
            {
                // 충돌 콜백 수와 관계없이 공 하나에 한 물리 프레임당 한 번만 적용한다.
                ball.AddElectricForce(sample.Force);
            }
        }

        private bool RefreshFieldDirection()
        {
            if (lever == null || lowerPlate == null || upperPlate == null)
            {
                fieldDirection = Vector2.zero;
                return false;
            }

            // 0.4~0.6 구간은 직전 극성을 유지해 레버 중앙의 작은 흔들림을 흡수한다.
            if (lever.Value >= 0.6f)
            {
                lowerIsPositive = true;
            }
            else if (lever.Value <= 0.4f)
            {
                lowerIsPositive = false;
            }

            Vector2 axis = upperPlate.position - lowerPlate.position;
            fieldDirection = axis.normalized * (lowerIsPositive ? 1f : -1f);
            return axis.sqrMagnitude >= 0.0001f;
        }

        private void UpdatePlateVisuals()
        {
            Color positive = Color.red;
            Color negative = new Color(0.1f, 0.45f, 1f);
            if (lowerRenderer != null)
            {
                lowerRenderer.color = lowerIsPositive ? positive : negative;
            }
            if (upperRenderer != null)
            {
                upperRenderer.color = lowerIsPositive ? negative : positive;
            }
        }

        /// <summary>
        /// 실제 적용과 화살표/설명에서 공유하는 샘플. 공 중심이 장의 영역 안에 있을 때만 유효하다.
        /// 가장자리 효과는 생략하고 E = V/d를 사용한다.
        /// </summary>
        public bool TryGetSample(out ElectricForceSample sample)
        {
            sample = default;
            if (!RefreshFieldDirection() || ball == null || ball.Body == null)
            {
                return false;
            }
            if (zone == null)
            {
                zone = GetComponent<BoxCollider2D>();
            }
            if (zone == null)
            {
                return false;
            }

            Vector2 position = ball.Body.worldCenterOfMass;
            float gap = Vector2.Distance(lowerPlate.position, upperPlate.position);
            float field = Mathf.Max(0f, voltage) / Mathf.Max(0.01f, gap);
            float theoreticalForce = ball.SignedCharge * field;
            float limit = Mathf.Max(0f, maximumForce);
            bool inRange = isActiveAndEnabled && zone.enabled && ball.CanReceiveForce
                && zone.OverlapPoint(position);

            sample = new ElectricForceSample
            {
                Position = position,
                SignedCharge = ball.SignedCharge,
                SourceCharge = 0f,
                Distance = gap,
                Field = inRange ? fieldDirection * field : Vector2.zero,
                Force = inRange
                    ? fieldDirection * Mathf.Clamp(theoreticalForce, -limit, limit)
                    : Vector2.zero,
                TheoreticalForceMagnitude = Mathf.Abs(theoreticalForce),
                IsLimited = Mathf.Abs(theoreticalForce) > limit,
                IsInRange = inRange
            };
            return true;
        }
    }
}
