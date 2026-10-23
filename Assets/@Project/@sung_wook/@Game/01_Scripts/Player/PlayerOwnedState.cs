namespace sung_wook
{
                 
                                                    
                                                          
                  
    public static class PlayerOwnedStates
    {
                                              
        public class NeutralState : State<Player>
        {
            public override void Enter(Player entity) { }
            public override void Execute(Player entity) { }
            public override void Exit(Player entity) { }
        }

                                       
        public class PositiveState : State<Player>
        {
            public override void Enter(Player entity) { }
            public override void Execute(Player entity) { }
            public override void Exit(Player entity) { }
        }

                                       
        public class NegativeState : State<Player>
        {
            public override void Enter(Player entity) { }
            public override void Execute(Player entity) { }
            public override void Exit(Player entity) { }
        }
    }
}
