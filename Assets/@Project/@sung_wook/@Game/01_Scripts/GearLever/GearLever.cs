using UnityEngine;

namespace sung_wook
{
                                                         
    public enum GearLeverStateType
    {
        Neutral,
        Positive,
        Negative
    }
    
                                                                      
    [DisallowMultipleComponent]
    public class GearLever : BaseGameEntity
    {
        public GearLeverStateType CurrentState { get; private set; }
        public State<GearLever>[] State { get; private set; }
        private StateMachine<GearLever> _stateMachine;

        private void Awake() => Setup();

                                                                            
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

                                                                 
        public void ChangeState(GearLeverStateType newState)
        {
            Setup();
            if ((int)newState < 0 || (int)newState >= State.Length || CurrentState == newState) return;
            CurrentState = newState;
            _stateMachine.ChangeState(State[(int)newState]);
        }

                                                                     
        public override void Updated()
        {
            Setup();
            _stateMachine.Execute();
        }
    }
}
