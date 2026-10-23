using UnityEngine;

namespace sung_wook
{
    /// <summary>
    /// 기존 Rigidbody2D의 좌우 이동·자동 바운스·전기력 적용을 담당한다.
    /// 스테이지는 이 컴포넌트의 공개 메서드로 힘을 전달하므로 이동 제어가 중복되지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D))]
    public class PlayerMovement : MonoBehaviour
    {
        private Rigidbody2D _rigid2D;

        // 기존 씬의 직렬화된 이동 설정을 그대로 유지한다.
        [SerializeField] private float jumpForce = 7f;
        [SerializeField] private float moveSpeed = 4f;
        [SerializeField] private float maxMoveSpeed = 7f;
        [SerializeField] private float acceleration = 5f;
        [SerializeField] private float deceleration = 9f;
        private float moveX;

        /// <summary>실제 지지면 착지만 전달한다. 구간 복귀는 이 위치를 사용한다.</summary>
        public event System.Action<Vector2> LandedOnGround;

        // 이전 AddForce 호출 경로와의 호환성을 위해 공개 전하 설정과 상수를 유지한다.
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

            // 조작하지 않는 동안에는 발사·전기력으로 얻은 수평 운동량을 감속시키지 않는다.
            if (preserveElectricMomentum && Mathf.Abs(moveX) < 0.01f) return;
            float target = moveX * (maxMoveSpeed > 0f ? maxMoveSpeed : moveSpeed);
            float rate = Mathf.Abs(moveX) > 0.01f ? acceleration : deceleration;
            Vector2 velocity = Body.linearVelocity;
            // 반대 방향 입력은 먼저 제동한다. 발사 관성은 무입력 상태에서 계속 보존한다.
            if (Mathf.Abs(moveX) > 0.01f && velocity.x * moveX < 0f)
                rate = Mathf.Max(acceleration, deceleration);
            velocity.x = Mathf.MoveTowards(velocity.x, target, rate * Time.fixedDeltaTime);
            Body.linearVelocity = velocity;
        }

        /// <summary>컨트롤러의 수평 입력을 저장하고 다음 물리 프레임에서 사용한다.</summary>
        public void SetMovex(float value) => moveX = Mathf.Clamp(value, -1f, 1f);

        /// <summary>장치가 계산한 지속 전기력을 기존 Rigidbody2D에 주입하는 진입점이다.</summary>
        public void ApplyElectricForce(Vector2 force)
        {
            if (!isActiveAndEnabled || !Body.simulated || force.sqrMagnitude <= 0f) return;
            preserveElectricMomentum = true;
            Body.AddForce(force, ForceMode2D.Force);
        }

        /// <summary>이전 속도를 초기화하고 단발 충격량으로 발사한다. 성공했을 때만 true를 반환한다.</summary>
        public bool LaunchImpulse(Vector2 impulse)
        {
            if (!isActiveAndEnabled || !Body.simulated
                || Body.bodyType != RigidbodyType2D.Dynamic || impulse.sqrMagnitude <= 0f)
                return false;
            Body.linearVelocity = Vector2.zero;
            Body.angularVelocity = 0f;
            Body.AddForce(impulse, ForceMode2D.Impulse);
            preserveElectricMomentum = true;
            // 발사 직후 남아 있는 바닥 접촉 콜백이 발사 속도를 자동 점프로 덮어쓰지 않게 한다.
            nextBounceTime = Time.fixedTime + 0.15f;
            return true;
        }

        /// <summary>재시작 시 속도와 입력, 전기 운동량 보존 상태를 함께 초기화한다.</summary>
        public void ResetMotion()
        {
            Body.linearVelocity = Vector2.zero;
            Body.angularVelocity = 0f;
            moveX = 0f;
            preserveElectricMomentum = false;
            nextBounceTime = Time.fixedTime + 0.15f;
        }

        /// <summary>
        /// 기존 레버 호출부를 위한 호환 경로다. 실제 플레이어 상태가 있으면 그 부호를 사용해
        /// 중성은 힘이 없고, 같은 부호는 반발하며 다른 부호는 끌리도록 계산한다.
        /// </summary>
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

        /// <summary>Ground에 아래쪽으로 내려앉는 접촉에서만 기존 자동 바운스를 실행한다.</summary>
        private void TryBounce(Collision2D collision)
        {
            if (!isActiveAndEnabled || !Body.simulated || Time.fixedTime < nextBounceTime
                || !collision.gameObject.CompareTag("Ground") || Body.linearVelocity.y > 0.1f)
                return;
            for (int i = 0; i < collision.contactCount; i++)
            {
                // 위를 향한 접촉 법선만 지지면으로 인정해 벽·천장 접촉에서는 튀지 않게 한다.
                if (Vector2.Dot(collision.GetContact(i).normal, Vector2.up) < 0.6f) continue;
                preserveElectricMomentum = false;
                LandedOnGround?.Invoke(Body.position);
                if (jumpForce > 0f)
                {
                    Vector2 velocity = Body.linearVelocity;
                    // 기존 jumpForce를 충격량으로 해석한 질량 의존 점프 속도를 유지한다.
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
