using UnityEngine;

namespace sung_wook
{
    public class PlayerMovement : MonoBehaviour
    {
        private Rigidbody2D _rigid2D;
        [SerializeField]
        private float jumpForce, moveSpeed, maxMoveSpeed, acceleration, deceleration;
        private float moveX;
        public float myCharge = 1f;
        public float otherCharge = 1f;
        public const float K = 10f;          // 게임용 상수 (실제값 8.99e9을 사용하면 예외 처리 됨)
        public const float MINDISTANCE = 0.5f;

        private void Awake()
        {
            _rigid2D = GetComponent<Rigidbody2D>();
        }
        private void FixedUpdate()
        {
            HorizontalMovement();
        }
        private void HorizontalMovement()
        {
            float target = moveX * maxMoveSpeed;
            float rate = Mathf.Abs(moveX) > 0.01f ? acceleration : deceleration;

            Vector2 velocity = _rigid2D.linearVelocity;
            velocity.x = Mathf.MoveTowards(velocity.x, target, rate * Time.fixedDeltaTime);
            _rigid2D.linearVelocity = velocity;
        }

        public void SetMovex(float moveX)
        {
            this.moveX = moveX;
        }

        public void AddForce(Transform pusher, float force, ForceMode2D forceMode)
        {
            Vector2 dir = _rigid2D.position - (Vector2)pusher.position;  
            float r = Mathf.Max(dir.magnitude, MINDISTANCE);

            float forceMag = K * myCharge * otherCharge / (r * r);
            _rigid2D.AddForce(dir.normalized * forceMag, forceMode);
        }
        

        private void OnCollisionEnter2D(Collision2D collision)
        {
            if (collision.gameObject.CompareTag("Ground"))
            {
                Jump();
            }
        }

        private void Jump()
        {
            _rigid2D.AddForce(Vector2.up * jumpForce, ForceMode2D.Impulse);
        }
    }   
}