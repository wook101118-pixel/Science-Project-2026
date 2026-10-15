using sung_wook;
using UnityEngine;

namespace PlayerOwnedStates
{
    public class NeutralState : State<Player>
    {
        public override void Enter(Player entity)
        {
            
        }

        public override void Execute(Player entity)
        {
            if (UnityEngine.InputSystem.Keyboard.current.spaceKey.wasReleasedThisFrame)
            {
                entity.ChangeState(PlayerStateType.Positive);
            }
        }

        public override void Exit(Player entity)
        {
            
        }
    }

    public class PositiveState : State<Player>
    {
        public override void Enter(Player entity)
        {
            
        }

        public override void Execute(Player entity)
        {
            Debug.Log("PositiveEnter");
            if (UnityEngine.InputSystem.Keyboard.current.spaceKey.wasReleasedThisFrame)
            {
                entity.ChangeState(PlayerStateType.Negative);
            }
        }

        public override void Exit(Player entity)
        {
            
        }
    }
    
    public class NegativeState : State<Player>
    {
        public override void Enter(Player entity)
        {
            
        }

        public override void Execute(Player entity)
        {
            Debug.Log("NegativeEnter");
            if (UnityEngine.InputSystem.Keyboard.current.spaceKey.wasReleasedThisFrame)
            {
                entity.ChangeState(PlayerStateType.Neutral);
            }
        }

        public override void Exit(Player entity)
        {
            
        }
    }
}