
namespace sung_wook
{
    public enum ElectricChargeType { Neutral, Positive, Negative }

    public class ElectricCharge : BaseGameEntity
    {
        private float moveSpeed = 5f;
        
        
        private ElectricChargeType _currentState;
        
        private State<ElectricCharge>[] _states;
        private StateMachine<ElectricCharge> _stateMachine;

        public override void Setup()
        {
            base.Setup();

            _states                                     = new State<ElectricCharge>[3];
            _states[(int)ElectricChargeType.Neutral]    = new GearLeverOwnedStates.NeutralState();
            _states[(int)ElectricChargeType.Positive]   = new GearLeverOwnedStates.PositiveState();
            _states[(int)ElectricChargeType.Negative]   = new GearLeverOwnedStates.NegativeState();

            _stateMachine = new StateMachine<ElectricCharge>();
            _stateMachine.Setup(this, _states[(int)ElectricChargeType.Neutral]);
        }

        public void ChangeState(ElectricChargeType newState)
        {
            _currentState = newState;
            
            _stateMachine.ChangeState(_states[(int)newState]);
        }
    }
}