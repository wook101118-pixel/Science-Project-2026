using sung_wook;
using UnityEngine;

namespace sung_wook
{
    public enum ElectricBoxType
    {
        Neutral,
        Positive,
        Negative
    }
    
    public class ElectricBox : BaseGameEntity
    {
        [SerializeField] private float repulsiveForce = 5f;
        [SerializeField] private float gravitationalForce = 5f;
        
        private State<ElectricBox>[] _states;
        private StateMachine<ElectricBox> _stateMachine;

        public override void Setup()
        {
            base.Setup();
            _stateMachine = new StateMachine<ElectricBox>();
            _states = new State<ElectricBox>[3];
            //_states[(int)ElectricBoxType.Neutral] = 
        }
    }
}