using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace sung_wook
{
    public class PlayerController : MonoBehaviour
    {
        [SerializeField]
        private float jumpForce, moveSpeed, maxMoveSpeed, acceleration, deceleration;
        private Rigidbody2D _rigid;

        private float moveX;
        
        
        public float myCharge = 1f;
        public float otherCharge = 1f;
        public const float K = 10f;          // 게임용 상수 (실제값 8.99e9을 사용하면 예외 처리 됨)
        public const float MINDISTANCE = 0.5f; 
        public bool IsStop { get; private set; } = false;
        
        private void Awake()
        {
            _rigid = GetComponent<Rigidbody2D>();
        }

        private void Update()
        {
            if (IsStop) return;
            
            if (Keyboard.current.aKey.isPressed)
            {
                moveX = -1;
            }
            else if (Keyboard.current.dKey.isPressed)
            {
                moveX = 1;
            }
            else if (Keyboard.current.aKey.wasReleasedThisFrame ||  Keyboard.current.dKey.wasReleasedThisFrame)
            {
                moveX = 0;
            }
            
        }

        private void FixedUpdate()
        {
            HorizontalMovement();
        }
        

        private void HorizontalMovement()
        {
            float target = moveX * maxMoveSpeed;
            float rate = Mathf.Abs(moveX) > 0.01f ? acceleration : deceleration;

            Vector2 velocity = _rigid.linearVelocity;
            velocity.x = Mathf.MoveTowards(velocity.x, target, rate * Time.fixedDeltaTime);
            _rigid.linearVelocity = velocity;
        }

        public void AddForce(Transform pusher, float force, ForceMode2D forceMode)
        {
            if (IsStop) return;
            
            Vector2 dir = _rigid.position - (Vector2)pusher.position;  
            float r = Mathf.Max(dir.magnitude, MINDISTANCE);

            float forceMag = K * myCharge * otherCharge / (r * r);
            _rigid.AddForce(dir.normalized * forceMag, forceMode);
        }

        public void SetStop(bool isStop)
        {
            IsStop = isStop;
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
            _rigid.AddForce(Vector2.up * jumpForce, ForceMode2D.Impulse);
        }
    }   
}
