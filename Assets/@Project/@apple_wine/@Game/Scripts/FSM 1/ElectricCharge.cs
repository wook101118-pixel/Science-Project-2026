
namespace apple_wine
{
    public enum ElectricChargeType { Neutral = -1, Positive = 0, Negative = 1 }

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
            _states[(int)ElectricChargeType.Neutral]    = new ElectricOwnedStates.NeutralState();
            _states[(int)ElectricChargeType.Positive]   = new ElectricOwnedStates.PositiveState();
            _states[(int)ElectricChargeType.Negative]   = new ElectricOwnedStates.NegativeState();

            _stateMachine = new StateMachine<ElectricCharge>();
            _stateMachine.Setup(this, _states[(int)ElectricChargeType.Neutral]);
        }

        public void ChangeState(ElectricChargeType newState)
        {
            _currentState = newState;
            
            _stateMachine?.ChangeState(_states[(int)newState]);
        }
    }
}