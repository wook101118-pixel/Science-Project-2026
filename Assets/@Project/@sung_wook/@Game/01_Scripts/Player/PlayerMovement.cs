using UnityEngine;

namespace sung_wook
{
                 
                                                  
                                                       
                  
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D))]
    public class PlayerMovement : MonoBehaviour
    {
        private Rigidbody2D _rigid2D;

                                      
        [SerializeField] private float jumpForce = 7f;
        [SerializeField] private float moveSpeed = 4f;
        [SerializeField] private float maxMoveSpeed = 7f;
        [SerializeField] private float acceleration = 5f;
        [SerializeField] private float deceleration = 9f;
        private float moveX;

                                                                  
        public event System.Action<Vector2> LandedOnGround;

                                                          
        public float myCharge = 1f;
        public float otherCharge = 1f;
        public const float K = 10f;
        public const float MINDISTANCE = 0.5f;
        private bool preserveElectricMomentum;
        private float nextBounceTime;

        public Rigidbody2D Body
        {
            get
            {
                if (_rigid2D == null) _rigid2D = GetComponent<Rigidbody2D>();
                return _rigid2D;
            }
        }

        private void Awake() => _rigid2D = GetComponent<Rigidbody2D>();

        private void FixedUpdate()
        {
            if (!Body.simulated || Body.bodyType != RigidbodyType2D.Dynamic) return;

                                                          
            if (preserveElectricMomentum && Mathf.Abs(moveX) < 0.01f) return;
            float target = moveX * (maxMoveSpeed > 0f ? maxMoveSpeed : moveSpeed);
            float rate = Mathf.Abs(moveX) > 0.01f ? acceleration : deceleration;
            Vector2 velocity = Body.linearVelocity;
                                                          
            if (Mathf.Abs(moveX) > 0.01f && velocity.x * moveX < 0f)
                rate = Mathf.Max(acceleration, deceleration);
            velocity.x = Mathf.MoveTowards(velocity.x, target, rate * Time.fixedDeltaTime);
            Body.linearVelocity = velocity;
        }

                                                                  
        public void SetMovex(float value) => moveX = Mathf.Clamp(value, -1f, 1f);

                                                                          
        public void ApplyElectricForce(Vector2 force)
        {
            if (!isActiveAndEnabled || !Body.simulated || force.sqrMagnitude <= 0f) return;
            preserveElectricMomentum = true;
            Body.AddForce(force, ForceMode2D.Force);
        }

                                                                              
        public bool LaunchImpulse(Vector2 impulse)
        {
            if (!isActiveAndEnabled || !Body.simulated
                || Body.bodyType != RigidbodyType2D.Dynamic || impulse.sqrMagnitude <= 0f)
                return false;
            Body.linearVelocity = Vector2.zero;
            Body.angularVelocity = 0f;
            Body.AddForce(impulse, ForceMode2D.Impulse);
            preserveElectricMomentum = true;
                                                              
            nextBounceTime = Time.fixedTime + 0.15f;
            return true;
        }

                                                                    
        public void ResetMotion()
        {
            Body.linearVelocity = Vector2.zero;
            Body.angularVelocity = 0f;
            moveX = 0f;
            preserveElectricMomentum = false;
            nextBounceTime = Time.fixedTime + 0.15f;
        }

                     
                                                           
                                                    
                      
        public void AddForce(Transform pusher, float force, ForceMode2D forceMode)
        {
            if (pusher == null || !Body.simulated) return;
            Vector2 offset = Body.worldCenterOfMass - (Vector2)pusher.position;
            if (offset.sqrMagnitude < 0.000001f) return;
            var player = GetComponent<Player>();
            float charge = player != null ? ElectricBall2D.ChargeSign(player.CurrentState) : myCharge;
            float magnitude = ElectricMath.CoulombForce(charge, otherCharge, offset.magnitude,
                K * Mathf.Max(0f, force), MINDISTANCE, 100f);
            if (magnitude == 0f) return;
            preserveElectricMomentum = true;
            Body.AddForce(offset.normalized * magnitude, forceMode);
        }

        private void OnCollisionEnter2D(Collision2D collision) => TryBounce(collision);
        private void OnCollisionStay2D(Collision2D collision) => TryBounce(collision);

                                                                        
        private void TryBounce(Collision2D collision)
        {
            if (!isActiveAndEnabled || !Body.simulated || Time.fixedTime < nextBounceTime
                || !collision.gameObject.CompareTag("Ground") || Body.linearVelocity.y > 0.1f)
                return;
            for (int i = 0; i < collision.contactCount; i++)
            {
                                                              
                if (Vector2.Dot(collision.GetContact(i).normal, Vector2.up) < 0.6f) continue;
                preserveElectricMomentum = false;
                LandedOnGround?.Invoke(Body.position);
                if (jumpForce > 0f)
                {
                    Vector2 velocity = Body.linearVelocity;
                                                                 
                    velocity.y = jumpForce / Body.mass;
                    Body.linearVelocity = velocity;
                    ElectricSfxPlayer.Play(SfxCue.Bounce);
                }
                nextBounceTime = Time.fixedTime + 0.1f;
                break;
            }
        }
    }
}
