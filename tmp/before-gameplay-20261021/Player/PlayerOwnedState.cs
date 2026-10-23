namespace sung_wook
{
    /// <summary>
    /// 기존 플레이어 FSM의 전하 상태 타입을 모아 둔다. 전하 변경은 Player,
    /// 물리 이동은 PlayerMovement가 담당하므로 상태 콜백에서는 중복 실행하지 않는다.
    /// </summary>
    public static class PlayerOwnedStates
    {
        /// <summary>순전하가 없는 중성 상태다.</summary>
        public class NeutralState : State<Player>
        {
            public override void Enter(Player entity) { }
            public override void Execute(Player entity) { }
            public override void Exit(Player entity) { }
        }

        /// <summary>양전하 상태다.</summary>
        public class PositiveState : State<Player>
        {
            public override void Enter(Player entity) { }
            public override void Execute(Player entity) { }
            public override void Exit(Player entity) { }
        }

        /// <summary>음전하 상태다.</summary>
        public class NegativeState : State<Player>
        {
            public override void Enter(Player entity) { }
            public override void Execute(Player entity) { }
            public override void Exit(Player entity) { }
        }
    }
}
