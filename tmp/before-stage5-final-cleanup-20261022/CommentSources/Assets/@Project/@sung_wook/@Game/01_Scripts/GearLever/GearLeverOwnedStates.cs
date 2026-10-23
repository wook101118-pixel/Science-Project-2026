namespace sung_wook
{
    /// <summary>
    /// 기존 레버 FSM의 상태 타입을 모아 둔다. 전극의 극성과 힘은 스테이지 장치가
    /// 결정하므로 각 상태에서 입력을 다시 읽거나 전기력을 중복 적용하지 않는다.
    /// </summary>
    public static class GearLeverOwnedStates
    {
        /// <summary>중성 레버 상태다.</summary>
        public sealed class NeutralState : State<GearLever>
        {
            public override void Enter(GearLever entity) { }
            public override void Execute(GearLever entity) { }
            public override void Exit(GearLever entity) { }
        }

        /// <summary>양전하 레버 상태다.</summary>
        public sealed class PositiveState : State<GearLever>
        {
            public override void Enter(GearLever entity) { }
            public override void Execute(GearLever entity) { }
            public override void Exit(GearLever entity) { }
        }

        /// <summary>음전하 레버 상태다.</summary>
        public sealed class NegativeState : State<GearLever>
        {
            public override void Enter(GearLever entity) { }
            public override void Execute(GearLever entity) { }
            public override void Exit(GearLever entity) { }
        }
    }
}
