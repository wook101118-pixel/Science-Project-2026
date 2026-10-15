using System;

namespace sung_wook
{
    public enum GearLeverStateType
    {
        Neutral,
        Positive,
        Negative
    }
    
    public class GearLever : BaseGameEntity
    {
        public GearLeverStateType CurrentState {get; private set;}
        public State<GearLever>[] State { get; private set; }
        private StateMachine<GearLever> _stateMachine;

        public override void Setup()
        {
            base.Setup();

            State = new State<GearLever>[3];
            State[(int)GearLeverStateType.Neutral] = new GearLeverOwnedStates.NeutralState();
            State[(int)GearLeverStateType.Positive] = new GearLeverOwnedStates.PositiveState();
            State[(int)GearLeverStateType.Negative] = new GearLeverOwnedStates.NegativeState();

            _stateMachine = new StateMachine<GearLever>();
            _stateMachine.Setup(this, State[(int)GearLeverStateType.Neutral]);
        }

        public void ChangeState(GearLeverStateType newState)
        {  
            CurrentState = newState;
            _stateMachine.ChangeState(State[(int)newState]);
        }
    }
}