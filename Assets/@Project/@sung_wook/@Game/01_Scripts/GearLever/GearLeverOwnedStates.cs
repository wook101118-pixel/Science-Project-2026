using sung_wook;
using UnityEngine;

namespace GearLeverOwnedStates
{
    public class NeutralState : State<GearLever>
    {
        public override void Enter(GearLever entity)
        {
            Debug.Log("Neutral");
        }

        public override void Execute(GearLever entity)
        {
            
        }

        public override void Exit(GearLever entity)
        {
            Debug.Log("Neutral Exit");
        }
    }

    public class PositiveState : State<GearLever>
    {
        public override void Enter(GearLever entity)
        {
            
        }

        public override void Execute(GearLever entity)
        {
            
        }

        public override void Exit(GearLever entity)
        {
            
        }
    }
    
    public class NegativeState : State<GearLever>
    {
        public override void Enter(GearLever entity)
        {
            
        }

        public override void Execute(GearLever entity)
        {
            
        }

        public override void Exit(GearLever entity)
        {
            
        }
    }
}