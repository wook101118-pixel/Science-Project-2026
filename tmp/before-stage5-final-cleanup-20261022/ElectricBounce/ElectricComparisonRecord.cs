using UnityEngine;

namespace sung_wook
{
    /// <summary>
    /// 씬, 입력, 화면에 의존하지 않는 거리 실험 기록입니다.
    /// 기준 예측값과 실제 발사에 사용한 표본을 구분하고 쿨롱 법칙의 비율을 해석합니다.
    /// </summary>
    public sealed class ElectricComparisonRecord
    {
        private const float DistanceTolerance = 0.01f;
        private const float ChargeTolerance = 0.0001f;

        public bool HasBaseline { get; private set; }
        public bool HasResult { get; private set; }
        public bool HasQualifiedComparison { get; private set; }
        public bool HasSingleVariableComparison { get; private set; }
        public ElectricForceSample BaselineSample { get; private set; }
        public ElectricForceSample ResultSample { get; private set; }
        public ElectricForceSample QualifiedBaselineSample { get; private set; }
        public ElectricForceSample QualifiedResultSample { get; private set; }

        /// <summary>새 실험에서는 이전 스테이지의 기록을 사용하지 않습니다.</summary>
        public void Reset()
        {
            HasBaseline = false;
            HasResult = false;
            HasQualifiedComparison = false;
            HasSingleVariableComparison = false;
            BaselineSample = default;
            ResultSample = default;
            QualifiedBaselineSample = QualifiedResultSample = default;
        }

        /// <summary>기준을 다시 저장하면 이전 기준에 대응하던 결과도 함께 지웁니다.</summary>
        public void RecordBaseline(ElectricForceSample sample)
        {
            BaselineSample = sample;
            HasBaseline = true;
            HasResult = false;
            HasQualifiedComparison = false;
            HasSingleVariableComparison = false;
            ResultSample = default;
            QualifiedBaselineSample = QualifiedResultSample = default;
        }

        /// <summary>발사 성공 이벤트에서 받은 표본만 결과로 기록합니다.</summary>
        public void RecordResult(ElectricForceSample sample)
        {
            RecordResult(sample, false);
        }

        /// <summary>
        /// 고정 출발점에서 기준과 다른 조건으로 발사했을 때만 비교 목표를 달성한다.
        /// 공이 날아가며 자연히 달라진 거리는 조작한 조건으로 보지 않는다.
        /// 달성 기록은 새 기준 저장 또는 재시작 전까지 유지한다.
        /// </summary>
        public void RecordResult(ElectricForceSample sample, bool launchedFromPreparedTrial)
        {
            ResultSample = sample;
            HasResult = true;
            if (HasBaseline && launchedFromPreparedTrial && IsValidSample(BaselineSample)
                && IsValidSample(sample)
                && Vector2.Distance(BaselineSample.Position, sample.Position) < DistanceTolerance
                && HaveConditionsChanged(BaselineSample, sample))
            {
                HasQualifiedComparison = true;
                HasSingleVariableComparison |= CountChangedConditions(BaselineSample, sample) == 1;
                // 공중에서 추가 발사해도 실제 준비 비교의 선택 풀이를 잃거나 새로 얻지 않는다.
                QualifiedBaselineSample = BaselineSample;
                QualifiedResultSample = sample;
            }
        }

        /// <summary>허용 오차보다 큰 거리·공 전하량·전극 전하량의 변화만 비교 조건으로 인정한다.</summary>
        public static bool HaveConditionsChanged(ElectricForceSample before, ElectricForceSample after)
            => CountChangedConditions(before, after) > 0;

        /// <summary>실제 적격 발사의 선택 도전에도 같은 허용 오차를 사용한다.</summary>
        public static int CountChangedConditions(ElectricForceSample before, ElectricForceSample after)
        {
            return (Mathf.Abs(before.Distance - after.Distance) >= DistanceTolerance ? 1 : 0)
                + (Mathf.Abs(before.SignedCharge - after.SignedCharge) > ChargeTolerance ? 1 : 0)
                + (Mathf.Abs(before.SourceCharge - after.SourceCharge) > ChargeTolerance ? 1 : 0);
        }

        private static bool IsValidSample(ElectricForceSample sample)
        {
            return sample.IsInRange && sample.Distance > 0f && sample.ForceMagnitude > 0f
                && !float.IsNaN(sample.Distance) && !float.IsInfinity(sample.Distance)
                && !float.IsNaN(sample.SignedCharge) && !float.IsInfinity(sample.SignedCharge)
                && !float.IsNaN(sample.SourceCharge) && !float.IsInfinity(sample.SourceCharge);
        }

        /// <summary>비교할 기준이 없으면 비율을 추정하지 않고 기록 상태만 설명합니다.</summary>
        public string DescribeResult()
        {
            if (!HasResult)
                return "아직 발사 결과가 없습니다. 공을 발사해 실제 결과를 기록하세요.";
            if (!HasBaseline)
                return "발사 순간의 거리와 전하량, 힘을 기록했습니다. 실험 위치로 복귀해 기준값을 저장하면 다음 발사와 비교할 수 있습니다.";
            return DescribeComparison(BaselineSample, ResultSample);
        }

        /// <summary>
        /// 적용 힘과 이론 힘의 비율을 각각 계산합니다. 게임 안전 제한이 작동하면 둘은 다를 수 있습니다.
        /// F = k|qQ|/r²의 비교이며 충격량이나 점프 높이의 비율로 해석하지 않습니다.
        /// </summary>
        public static string DescribeComparison(ElectricForceSample before, ElectricForceSample after)
        {
            if (before.ForceMagnitude <= 0.0001f || before.TheoreticalForceMagnitude <= 0.0001f
                || before.Distance <= 0.0001f)
                return "기준 힘 또는 거리가 0이어서 배수를 계산할 수 없습니다. 전하량과 거리가 0보다 큰 조건으로 기준을 다시 저장하세요.";

            float actualRatio = after.ForceMagnitude / before.ForceMagnitude;
            float expectedRatio = after.TheoreticalForceMagnitude / before.TheoreticalForceMagnitude;
            string result = $"발사 결과: 기준 대비 힘 {actualRatio:0.00}배. 쿨롱 법칙에 따른 예상은 {expectedRatio:0.00}배입니다. ";
            bool sameCharges = Mathf.Approximately(before.SignedCharge, after.SignedCharge)
                && Mathf.Approximately(before.SourceCharge, after.SourceCharge);
            bool sameDistance = Mathf.Abs(before.Distance - after.Distance) < 0.01f;

            if (sameCharges)
                result += $"전하량이 같을 때 힘은 거리 제곱에 반비례합니다. 거리 {after.Distance / before.Distance:0.00}배 → 이론 힘 {expectedRatio:0.00}배.";
            else if (sameDistance)
                result += "거리가 같을 때 힘은 두 전하량의 곱에 비례합니다.";
            else
                result += "거리와 전하량을 함께 바꿨습니다. 하나씩 바꾸면 각 변수의 영향을 비교하기 쉽습니다.";

            if (before.IsLimited || after.IsLimited)
                result += " 이 조건에는 게임의 최소 거리 또는 최대 힘 제한이 적용되어 실제 배수가 이론과 다를 수 있습니다.";
            return result + " 이 값은 발사에 사용한 힘이며 점프 높이의 배수가 아닙니다.";
        }
    }
}
