using System;

namespace sung_wook
{
    public enum PlayerStateType
    {
        Neutral,
        Positive,
        Negative
    }

    public class Player : BaseGameEntity
    {
       //private float moveSpeed = 5f;

       public bool IsSamePositive {get; private set;} = false;
       public bool PositiveAndNegative {get; private set;} = false;
       public bool IsSameNegative {get; private set;} = false;

        public PlayerStateType CurrentState { get; private set; }

        public State<Player>[] States { get; private set; }
        private StateMachine<Player> _stateMachine;
        public ChangeVisual ChangeVisual { get; private set; }
        public PlayerController PlayerController {get; private set;}
        

        public override void Setup()
        {
            base.Setup();
            PlayerController = GetComponent<PlayerController>();
            ChangeVisual = GetComponentInChildren<ChangeVisual>();

            States = new State<Player>[3];
            States[(int)PlayerStateType.Neutral]  = new PlayerOwnedStates.NeutralState();
            States[(int)PlayerStateType.Positive] = new PlayerOwnedStates.PositiveState();
            States[(int)PlayerStateType.Negative] = new PlayerOwnedStates.NegativeState();

            _stateMachine = new StateMachine<Player>();
            _stateMachine.Setup(this, States[(int)PlayerStateType.Neutral]);
        }

        private void Update()
        {
            _stateMachine.Execute();
        }


        public void ChangeState(PlayerStateType newState)
        {
            CurrentState = newState;

            _stateMachine.ChangeState(States[(int)newState]);
            ChangeVisual.VisualChange(newState);
        }
    }
}