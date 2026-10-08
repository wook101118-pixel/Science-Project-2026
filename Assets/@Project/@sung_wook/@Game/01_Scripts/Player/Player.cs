using UnityEngine;
using UnityEngine.InputSystem;

namespace sung_wook
{
    public class Player : MonoBehaviour
    {
        [SerializeField]
        private float jumpForce, moveSpeed;
        private Rigidbody2D _rigid;

        private void Awake()
        {
            _rigid = GetComponent<Rigidbody2D>();
        }

        private void Update()
        {
            if (Keyboard.current.aKey.isPressed)
            {
                transform.Translate(Vector2.left * (moveSpeed * Time.deltaTime));
            }
            else if (Keyboard.current.dKey.isPressed)
            {
                transform.Translate(Vector2.right * (moveSpeed * Time.deltaTime));
            }
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
