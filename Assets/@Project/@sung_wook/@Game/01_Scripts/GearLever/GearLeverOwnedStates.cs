using sung_wook;
using UnityEngine;

namespace GearLeverOwnedStates
{
    public class NeutralState : State<ElectricCharge>
    {
        public override void Enter(ElectricCharge entity)
        {
            Debug.Log("Neutral");
        }

        public override void Execute(ElectricCharge entity)
        {
            
        }

        public override void Exit(ElectricCharge entity)
        {
            Debug.Log("Neutral Exit");
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