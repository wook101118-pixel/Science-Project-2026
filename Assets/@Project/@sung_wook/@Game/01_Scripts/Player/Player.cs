using System;

namespace sung_wook
{
    public enum PlayerStateType
    {
        Neutral,
        Positive,
        Negative
    }

    public class Player : BaseGameEntity
    {
        public PlayerStateType CurrentState { get; private set; }
        
        private State<Player>[] _states;
        private StateMachine<Player> _stateMachine;
        private ChangeVisual _changeVisual;
        

        public override void Setup()
        {
            base.Setup();
            _changeVisual = GetComponentInChildren<ChangeVisual>();

            _states = new State<Player>[3];
            _states[(int)PlayerStateType.Neutral]  = new PlayerOwnedStates.NeutralState();
            _states[(int)PlayerStateType.Positive] = new PlayerOwnedStates.PositiveState();
            _states[(int)PlayerStateType.Negative] = new PlayerOwnedStates.NegativeState();

            _stateMachine = new StateMachine<Player>();
            _stateMachine.Setup(this, _states[(int)PlayerStateType.Neutral]);
        }

        private void Update()
        {
            _stateMachine.Execute();
        }


        public void ChangeState(PlayerStateType newState)
        {
            CurrentState = newState;

            _stateMachine.ChangeState(_states[(int)newState]);
            _changeVisual.VisualChange(newState);
        }
    }
}