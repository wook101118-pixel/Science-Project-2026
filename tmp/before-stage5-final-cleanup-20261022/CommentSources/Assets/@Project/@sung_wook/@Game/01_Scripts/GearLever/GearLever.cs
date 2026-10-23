using UnityEngine;

namespace sung_wook
{
    /// <summary>기존 레버 상태 머신에서 사용하는 전하 상태 식별자다.</summary>
    public enum GearLeverStateType
    {
        Neutral,
        Positive,
        Negative
    }
    
    /// <summary>레버의 상태만 관리하며, 드래그 입력과 전기력 계산은 별도 컴포넌트에 맡긴다.</summary>
    [DisallowMultipleComponent]
    public class GearLever : BaseGameEntity
    {
        public GearLeverStateType CurrentState { get; private set; }
        public State<GearLever>[] State { get; private set; }
        private StateMachine<GearLever> _stateMachine;

        private void Awake() => Setup();

        /// <summary>Awake와 외부 연결 도구의 호출 순서에 관계없이 상태 머신을 한 번만 만든다.</summary>
        public override void Setup()
        {
            if (_stateMachine != null) return;
            base.Setup();

            State = new State<GearLever>[3];
            State[(int)GearLeverStateType.Neutral] = new GearLeverOwnedStates.NeutralState();
            State[(int)GearLeverStateType.Positive] = new GearLeverOwnedStates.PositiveState();
            State[(int)GearLeverStateType.Negative] = new GearLeverOwnedStates.NegativeState();

            _stateMachine = new StateMachine<GearLever>();
            CurrentState = GearLeverStateType.Neutral;
            _stateMachine.Setup(this, State[(int)GearLeverStateType.Neutral]);
        }

        /// <summary>유효한 상태로 변경할 때만 상태 머신의 전환 콜백을 실행한다.</summary>
        public void ChangeState(GearLeverStateType newState)
        {
            Setup();
            if ((int)newState < 0 || (int)newState >= State.Length || CurrentState == newState) return;
            CurrentState = newState;
            _stateMachine.ChangeState(State[(int)newState]);
        }

        /// <summary>기존 BaseGameEntity 갱신 경로에서 현재 상태를 실행한다.</summary>
        public override void Updated()
        {
            Setup();
            _stateMachine.Execute();
        }
    }
}
