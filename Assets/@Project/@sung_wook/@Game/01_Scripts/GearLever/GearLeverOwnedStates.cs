namespace sung_wook
{
                 
                                                    
                                                 
                  
    public static class GearLeverOwnedStates
    {
                                         
        public sealed class NeutralState : State<GearLever>
        {
            public override void Enter(GearLever entity) { }
            public override void Execute(GearLever entity) { }
            public override void Exit(GearLever entity) { }
        }

                                          
        public sealed class PositiveState : State<GearLever>
        {
            public override void Enter(GearLever entity) { }
            public override void Execute(GearLever entity) { }
            public override void Exit(GearLever entity) { }
        }

                                          
        public sealed class NegativeState : State<GearLever>
        {
            public override void Enter(GearLever entity) { }
            public override void Execute(GearLever entity) { }
            public override void Exit(GearLever entity) { }
        }
    }
}
