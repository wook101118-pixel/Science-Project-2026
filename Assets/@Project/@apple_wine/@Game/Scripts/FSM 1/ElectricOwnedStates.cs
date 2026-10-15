using apple_wine;
using UnityEngine;

namespace ElectricOwnedStates
{
    public class NeutralState : State<ElectricCharge>
    {
        public override void Enter(ElectricCharge entity)
        {
            Debug.Log("전기적으로 중성");
            ;
        }

        public override void Execute(ElectricCharge entity)
        {
            
        }

        public override void Exit(ElectricCharge entity)
        {
            
        }
    }

    public class PositiveState : State<ElectricCharge>
    {
        public override void Enter(ElectricCharge entity)
        {
            
        }

        public override void Execute(ElectricCharge entity)
        {
            
        }

        public override void Exit(ElectricCharge entity)
        {
            
        }
    }
    
    public class NegativeState : State<ElectricCharge>
    {
        public override void Enter(ElectricCharge entity)
        {
            
        }

        public override void Execute(ElectricCharge entity)
        {
            
        }

        public override void Exit(ElectricCharge entity)
        {
            
        }
    }
}