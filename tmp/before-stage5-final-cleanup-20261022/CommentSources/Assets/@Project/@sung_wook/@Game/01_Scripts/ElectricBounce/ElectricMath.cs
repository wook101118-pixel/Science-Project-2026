using System;

namespace sung_wook
{
    /// <summary>
    /// Unity 오브젝트와 무관한 전기 실험 계산. 입력·표시·씬 연결을 알지 않는다.
    /// 기존 계산은 학습용 게임 단위를 사용하며, SI 계산은 명시적인 단위 변환을 통해 사용한다.
    /// </summary>
    public static class ElectricMath
    {
        /// <summary>진공의 쿨롱 상수, N·m²/C² (2022 CODATA).</summary>
        public const double VacuumCoulombConstant = 8.9875517862e9;

        /// <summary>
        /// SI 쿨롱 상수를 Unity 좌표/전하 단위의 상수로 바꾼다. 질량 단위는 kg이다.
        /// F(Unity) = F(N)/metersPerWorldUnit이므로 길이 배율의 세제곱으로 나눈다.
        /// </summary>
        public static double EffectiveUnityCoulombConstant(double metersPerWorldUnit,
            double coulombsPerChargeUnit, double relativePermittivity = 1d)
        {
            RequirePositiveFinite(metersPerWorldUnit, nameof(metersPerWorldUnit));
            RequirePositiveFinite(coulombsPerChargeUnit, nameof(coulombsPerChargeUnit));
            RequirePositiveFinite(relativePermittivity, nameof(relativePermittivity));

            // 중간 제곱/세제곱의 불필요한 넘침을 줄이고 double 정밀도로 변환한다.
            double chargePerLength = coulombsPerChargeUnit / metersPerWorldUnit;
            double constant = (VacuumCoulombConstant / relativePermittivity)
                * chargePerLength * chargePerLength / metersPerWorldUnit;
            RequirePositiveFinite(constant, "effectiveCoulombConstant");
            return constant;
        }

        private static void RequirePositiveFinite(double value, string parameterName)
        {
            if (value <= 0d || double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(parameterName,
                    "The value must be positive and finite.");
            }
        }

        /// <summary>
        /// k q1 q2 / r²의 부호 있는 힘. 양수는 척력, 음수는 인력이다.
        /// 겹침에서 발산하지 않도록 최소 거리와 최대 힘을 적용한다.
        /// </summary>
        public static float CoulombForce(float q1, float q2, float distance,
            float k, float minimumDistance, float maximumForce)
        {
            float r = Math.Max(Math.Abs(distance), Math.Max(0.01f, minimumDistance));
            float force = Math.Max(0f, k) * q1 * q2 / (r * r);
            float limit = Math.Max(0f, maximumForce);
            return Math.Max(-limit, Math.Min(limit, force));
        }

        /// <summary>평행판 내부의 균일장 E = V/d에서 F = qE를 계산한다.</summary>
        public static float FieldForce(float charge, float voltage, float gap)
        {
            return charge * voltage / Math.Max(0.01f, Math.Abs(gap));
        }

        /// <summary>정지 출발, 손실 없음의 가정에서 qV = ½mv²를 속력으로 변환한다.</summary>
        public static float SpeedFromVoltage(float charge, float voltage, float mass)
        {
            float work = charge * voltage;
            if (work <= 0f || mass <= 0f)
            {
                return 0f;
            }
            return (float)Math.Sqrt(2f * work / mass);
        }
    }
}
