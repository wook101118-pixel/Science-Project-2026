using UnityEngine;

namespace sung_wook
{
    /// <summary>입력 리더의 명령을 기존 이동 컴포넌트와 플레이어 상태에 전달한다.</summary>
    public class PlayerController : MonoBehaviour
    {
        public Player PlayerCompo { get; private set; }
        public PlayerMovement MoveCompo { get; private set; }
        public PlayerInputReader InputCompo { get; private set; }

        private void Awake()
        {
            PlayerCompo = GetComponent<Player>();
            MoveCompo = GetComponent<PlayerMovement>();
            InputCompo = GetComponent<PlayerInputReader>();
        }

        private void Update()
        {
            // 입력 전달은 매 프레임, 실제 Rigidbody2D 갱신은 PlayerMovement.FixedUpdate에서 한다.
            if (MoveCompo != null && MoveCompo.isActiveAndEnabled && InputCompo != null)
            {
                MoveCompo.SetMovex(InputCompo.MoveVector.x);
            }

            // 기존 ChangeState 경로로 시각 표시와 전하 알림을 함께 갱신한다.
            if (PlayerCompo != null && PlayerCompo.isActiveAndEnabled
                && InputCompo != null && InputCompo.StateChangePressed)
            {
                PlayerCompo.CycleState();
            }
        }
    }
}
