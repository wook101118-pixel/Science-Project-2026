using sung_wook;

namespace sung_wook
{
    public class StateMachine<T> where T : BaseGameEntity
    {
        private T _ownEntity;
        private State<T> _currentState;

        public void Setup(T owner, State<T> entryState)
        {
            _ownEntity = owner;
            _currentState = null;
            
            ChangeState(entryState);
        }

        public void ChangeState(State<T> newState)
        {
            if (newState == null) return;

            if (_currentState != null)
            {
                _currentState.Exit(_ownEntity);    
            }
            
            _currentState = newState;
            _currentState.Enter(_ownEntity);
        }
        
        public void Execute()
        {
            _currentState?.Execute(_ownEntity);
        }
    }
}