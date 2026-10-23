using System;
using UnityEngine;

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
        public PlayerStateType CurrentState { get; private set; }
        [SerializeField] private PlayerStateType initialState = PlayerStateType.Neutral;
        [SerializeField, Range(0.01f, 10f), Tooltip("Space로 양전하/음전하를 선택할 때의 실제 전하량. 전하 단위 1은 접촉 실험에서 30 μC입니다.")]
        private float keyboardChargeMagnitude = 1f;
                                                                     
        [SerializeField, HideInInspector] private bool allowDebugChargeCycling;
        public event Action<PlayerStateType> StateChanged;
        
        private State<Player>[] _states;
        private StateMachine<Player> _stateMachine;
        private ChangeVisual _changeVisual;
                                                                              
        public override void Setup()
        {
            if (_stateMachine != null) return;
            base.Setup();
            _changeVisual = GetComponentInChildren<ChangeVisual>(true);
            if (_changeVisual != null) _changeVisual.Setup();

            _states = new State<Player>[3];
            _states[(int)PlayerStateType.Neutral] = new PlayerOwnedStates.NeutralState();
            _states[(int)PlayerStateType.Positive] = new PlayerOwnedStates.PositiveState();
            _states[(int)PlayerStateType.Negative] = new PlayerOwnedStates.NegativeState();

            _stateMachine = new StateMachine<Player>();
            CurrentState = initialState;
            _stateMachine.Setup(this, _states[(int)initialState]);
            if (_changeVisual != null) _changeVisual.VisualChange(CurrentState);
        }

        private void Awake() => Setup();

        private void Update()
        {
            _stateMachine?.Execute();
        }

                                                                               
        public void CycleState()
        {
            Setup();
            PlayerStateType nextState = (PlayerStateType)(((int)CurrentState + 1) % _states.Length);
            ElectricBall2D ball = GetComponent<ElectricBall2D>();
            if (ball != null)
            {
                float magnitude = float.IsNaN(keyboardChargeMagnitude) || float.IsInfinity(keyboardChargeMagnitude)
                    ? 1f : Mathf.Clamp(keyboardChargeMagnitude, 0.01f, 10f);
                ball.SetChargeMagnitude(nextState == PlayerStateType.Neutral ? 0f : magnitude);
            }
            ChangeState(nextState);
        }

                                                                              
        public void ChangeState(PlayerStateType newState)
        {
            Setup();
            if (newState < PlayerStateType.Neutral || newState > PlayerStateType.Negative)
                return;
            if (CurrentState == newState) return;
            CurrentState = newState;

            _stateMachine.ChangeState(_states[(int)newState]);
            if (_changeVisual != null) _changeVisual.VisualChange(newState);
            StateChanged?.Invoke(newState);
            ElectricSfxPlayer.Play(SfxCue.Charge);
        }
    }
}
