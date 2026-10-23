namespace sung_wook
{
    /// <summary>출구가 학습 상태의 내부 구현을 몰라도 추가 클리어 조건을 확인할 수 있는 계약.</summary>
    public interface IElectricStageClearRule
    {
        bool CanClearGoal(ElectricBall2D ball);
    }
}
