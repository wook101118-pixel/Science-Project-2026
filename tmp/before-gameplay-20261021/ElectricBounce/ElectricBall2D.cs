using UnityEngine;

namespace sung_wook
{
    /// <summary>계산에 사용할 전하의 부호. 기존 PlayerStateType의 정수 값과 구분한다.</summary>
    public enum Polarity
    {
        Negative = -1,
        Neutral = 0,
        Positive = 1
    }

    /// <summary>
    /// 전기 실험과 기존 플레이어를 연결하는 어댑터.
    /// 전하 상태는 Player, 이동·바운스는 PlayerMovement가 계속 소유한다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Player), typeof(PlayerMovement), typeof(Rigidbody2D))]
    public sealed class ElectricBall2D : MonoBehaviour
    {
        [SerializeField, Range(0f, 10f)]
        private float chargeMagnitude = 1f;
        private Player player;
        private PlayerMovement movement;

        public Rigidbody2D Body => Movement.Body;

        public Player Player
        {
            get
            {
                if (player == null)
                {
                    player = GetComponent<Player>();
                }
                return player;
            }
        }

        private PlayerMovement Movement
        {
            get
            {
                if (movement == null)
                {
                    movement = GetComponent<PlayerMovement>();
                }
                return movement;
            }
        }

        public Polarity CurrentPolarity => (Polarity)ChargeSign(Player.CurrentState);
        public float ChargeMagnitude => chargeMagnitude;
        public float SignedCharge => ChargeSign(Player.CurrentState) * chargeMagnitude;
        public int Stars { get; private set; }

        // 클리어로 정지했거나 비활성화된 공에는 다른 장치가 힘을 넣지 않는다.
        public bool CanReceiveForce => isActiveAndEnabled && Movement != null
            && Movement.isActiveAndEnabled && Body != null && Body.simulated
            && Body.bodyType == RigidbodyType2D.Dynamic;

        private void Awake()
        {
            ValidateCharge();
            player = GetComponent<Player>();
            movement = GetComponent<PlayerMovement>();
            player.Setup(); // 컴포넌트 Awake 순서와 무관하게 기존 상태/시각 참조를 준비한다.
        }

        private void OnValidate() => ValidateCharge();

        private void ValidateCharge()
        {
            if (float.IsNaN(chargeMagnitude) || float.IsInfinity(chargeMagnitude))
            {
                chargeMagnitude = 1f;
            }
            chargeMagnitude = Mathf.Clamp(chargeMagnitude, 0f, 10f);
        }

        /// <summary>게임 단위 전하량만 바꾼다. 부호와 기존 Player 상태는 유지한다.</summary>
        public void SetChargeMagnitude(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return;
            }
            chargeMagnitude = Mathf.Clamp(value, 0f, 10f);
        }

        /// <summary>기존 상태 열거형의 저장 값에 의존하지 않고 실제 전하 부호로 변환한다.</summary>
        public static int ChargeSign(PlayerStateType state)
        {
            switch (state)
            {
                case PlayerStateType.Positive:
                    return 1;
                case PlayerStateType.Negative:
                    return -1;
                default:
                    return 0;
            }
        }

        /// <summary>기존 상태 변경 경로를 사용하여 전하와 공의 시각 표현을 함께 갱신한다.</summary>
        public void SetPolarity(Polarity value)
        {
            PlayerStateType state = value == Polarity.Positive
                ? PlayerStateType.Positive
                : value == Polarity.Negative ? PlayerStateType.Negative : PlayerStateType.Neutral;
            Player.ChangeState(state);
        }

        public void SetPositive()
        {
            SetPolarity(Polarity.Positive);
        }

        public void SetNegative()
        {
            SetPolarity(Polarity.Negative);
        }

        public void SetNeutral()
        {
            SetPolarity(Polarity.Neutral);
        }

        /// <summary>출구 조건에서 읽는 현재 시도의 별 개수를 기록한다.</summary>
        public void CollectStar()
        {
            Stars++;
        }

        public void ResetStars()
        {
            Stars = 0;
        }

        /// <summary>연속적인 전기력을 기존 이동 담당에 전달한다. 자체 물리 루프는 만들지 않는다.</summary>
        public void AddElectricForce(Vector2 force)
        {
            if (CanReceiveForce)
            {
                Movement.ApplyElectricForce(force);
            }
        }

        /// <summary>발사 충격량과 바운스 간섭 처리는 기존 PlayerMovement에 위임한다.</summary>
        public bool LaunchImpulse(Vector2 impulse)
        {
            return CanReceiveForce && Movement.LaunchImpulse(impulse);
        }

        /// <summary>재시작 때 클리어 정지와 이전 운동을 해제하고 실험의 초기 상태로 돌아간다.</summary>
        public void ResetAt(Vector2 position, Polarity polarity)
        {
            enabled = true;
            Movement.enabled = true;
            Body.simulated = true;
            Body.position = position;
            Movement.ResetMotion();
            SetPolarity(polarity);
            ResetStars();
        }
    }
}
