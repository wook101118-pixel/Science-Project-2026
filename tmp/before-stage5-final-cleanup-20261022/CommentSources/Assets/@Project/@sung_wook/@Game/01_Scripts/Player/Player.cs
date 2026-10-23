using System;
using UnityEngine;

namespace sung_wook
{
    /// <summary>상태 배열의 인덱스다. 실제 전하 부호는 ElectricBall2D.ChargeSign으로 변환한다.</summary>
    public enum PlayerStateType
    {
        Neutral,
        Positive,
        Negative
    }

    /// <summary>
    /// 플레이어 전하 상태의 기준값을 관리하고 변경을 시각 컴포넌트와 구독자에게 알린다.
    /// 입력 해석과 물리 이동은 각각 기존 입력·이동 컴포넌트가 담당한다.
    /// </summary>
    public class Player : BaseGameEntity
    {
        public PlayerStateType CurrentState { get; private set; }
        [SerializeField] private PlayerStateType initialState = PlayerStateType.Neutral;
        [SerializeField, Range(0.01f, 10f), Tooltip("Space로 양전하/음전하를 선택할 때의 실제 전하량. 전하 단위 1은 접촉 실험에서 30 μC입니다.")]
        private float keyboardChargeMagnitude = 1f;
        // 이전 씬·프리팹의 직렬화 필드를 보존한다. Space는 이제 일반 입력이므로 이 옵션에 의존하지 않는다.
        [SerializeField, HideInInspector] private bool allowDebugChargeCycling;
        public event Action<PlayerStateType> StateChanged;
        
        private State<Player>[] _states;
        private StateMachine<Player> _stateMachine;
        private ChangeVisual _changeVisual;
        /// <summary>여러 Awake 또는 설치 경로에서 호출되어도 FSM과 시각 요소를 한 번만 만든다.</summary>
        public override void Setup()
        {
            if (_stateMachine != null) return;
            base.Setup();
            _changeVisual = GetComponentInChildren<ChangeVisual>(true);
            if (_changeVisual != null) _changeVisual.Setup();

            _states = new State<Player>[3];
            _states[(int)PlayerStateType.Neutral] = new PlayerOwnedStates.NeutralState();
            _states[(int)PlayerStateType.Positive] = new PlayerOwnedStates.PositiveState();
            _states[(int)PlayerStateType.Negative] = new PlayerOwnedStates.NegativeState();

            _stateMachine = new StateMachine<Player>();
            CurrentState = initialState;
            _stateMachine.Setup(this, _states[(int)initialState]);
            if (_changeVisual != null) _changeVisual.VisualChange(CurrentState);
        }

        private void Awake() => Setup();

        private void Update()
        {
            _stateMachine?.Execute();
        }

        /// <summary>Space로 중성 → 양전하 → 음전하를 선택하며 실제 전하량과 표시를 함께 변경한다.</summary>
        public void CycleState()
        {
            Setup();
            PlayerStateType nextState = (PlayerStateType)(((int)CurrentState + 1) % _states.Length);
            ElectricBall2D ball = GetComponent<ElectricBall2D>();
            if (ball != null)
            {
                float magnitude = float.IsNaN(keyboardChargeMagnitude) || float.IsInfinity(keyboardChargeMagnitude)
                    ? 1f : Mathf.Clamp(keyboardChargeMagnitude, 0.01f, 10f);
                ball.SetChargeMagnitude(nextState == PlayerStateType.Neutral ? 0f : magnitude);
            }
            ChangeState(nextState);
        }

        /// <summary>유효한 새 상태에 대해 FSM → 시각 표시 → 외부 알림 순서로 한 번만 갱신한다.</summary>
        public void ChangeState(PlayerStateType newState)
        {
            Setup();
            if (newState < PlayerStateType.Neutral || newState > PlayerStateType.Negative)
                return;
            if (CurrentState == newState) return;
            CurrentState = newState;

            _stateMachine.ChangeState(_states[(int)newState]);
            if (_changeVisual != null) _changeVisual.VisualChange(newState);
            StateChanged?.Invoke(newState);
            ElectricSfxPlayer.Play(SfxCue.Charge);
        }
    }
}
