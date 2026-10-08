namespace sung_wook
{
    public abstract class State<T> where T : BaseGameEntity
    {
        public abstract void Enter(T entity);
        public abstract void Execute(T entity);
        public abstract void Exit(T entity);
    }
}