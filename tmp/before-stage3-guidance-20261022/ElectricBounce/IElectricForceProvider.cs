namespace sung_wook
{
    /// <summary>
    /// 전기장과 전기력의 읽기 전용 관측 계약. 화면은 구체적인 전극/발사대 종류에 의존하지 않는다.
    /// 구현은 힘을 추가하지 않고 실제 물리 처리와 같은 계산 결과를 반환해야 한다.
    /// </summary>
    public interface IElectricForceProvider
    {
        bool TryGetSample(out ElectricForceSample sample);
    }
}
