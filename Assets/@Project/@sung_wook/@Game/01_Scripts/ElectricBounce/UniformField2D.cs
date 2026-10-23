using UnityEngine;

namespace sung_wook
{
                 
                                                         
                                                
                  
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
