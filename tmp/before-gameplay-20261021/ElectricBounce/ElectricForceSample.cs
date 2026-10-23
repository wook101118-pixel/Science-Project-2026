using UnityEngine;

namespace sung_wook
{
    /// <summary>
    /// 물리 처리와 학습 표시가 함께 사용하는 한 시점의 관찰값.
    /// 샘플 생성은 힘을 가하거나 발사하지 않는다. 기본 수치는 게임 단위이며,
    /// UsesSIUnits가 참인 샘플은 단위 배율과 SI 관찰값도 제공한다.
    /// 발사대의 Force는 연속 힘이 아니라 발사 순간 사용할 펄스 힘이다.
    /// </summary>
    public struct ElectricForceSample
    {
        public Vector2 Position;
        public Vector2 Field;
        public Vector2 Force;
        public float SignedCharge;
        public float SourceCharge;
        public float Distance;
        // 거리/힘 안전 제한을 적용하기 전 값. 표시에서 이론과 실제 적용값을 구분한다.
        public float TheoreticalForceMagnitude;
        public bool IsLimited;
        public bool IsInRange;
        public bool UsesSIUnits;
        public float MetersPerWorldUnit;
        public float CoulombsPerChargeUnit;

        public float ForceMagnitude => Force.magnitude;
        public float FieldMagnitude => Field.magnitude;
        public float DistanceMeters => UsesSIUnits
            ? FiniteFloat((double)Distance * MetersPerWorldUnit) : 0f;
        public float SignedChargeCoulombs => UsesSIUnits
            ? FiniteFloat((double)SignedCharge * CoulombsPerChargeUnit) : 0f;
        public float SourceChargeCoulombs => UsesSIUnits
            ? FiniteFloat((double)SourceCharge * CoulombsPerChargeUnit) : 0f;
        public Vector2 ForceNewtons => UsesSIUnits
            ? ScaleFinite(Force, MetersPerWorldUnit) : Vector2.zero;
        public Vector2 FieldNewtonsPerCoulomb => UsesSIUnits && CoulombsPerChargeUnit > 0f
            ? ScaleFinite(Field, (double)MetersPerWorldUnit / CoulombsPerChargeUnit)
            : Vector2.zero;
        public float TheoreticalForceNewtons => UsesSIUnits
            ? FiniteFloat((double)TheoreticalForceMagnitude * MetersPerWorldUnit) : 0f;

        /// <summary>
        /// 접촉 전극의 점전하 근사. 위치/거리는 Unity 단위, 전하는 기존 전하 단위다.
        /// SI 거리/전하 배율과 상대 유전율을 명시하며 최대 힘은 N으로 지정한다.
        /// 반경 0은 무제한이다. 기존 게임 단위 PointCharge의 동작은 바꾸지 않는다.
        /// </summary>
        internal static ElectricForceSample PointChargeSI(Vector2 position, Vector2 source,
            float charge, float sourceCharge, float metersPerWorldUnit,
            float coulombsPerChargeUnit, float relativePermittivity, float distanceFloorUnity,
            float maxForceNewtons, bool canApply, float radiusUnity)
        {
            var empty = new ElectricForceSample
            {
                Position = IsFinite(position) ? position : Vector2.zero,
                UsesSIUnits = true,
                MetersPerWorldUnit = IsPositiveFinite(metersPerWorldUnit) ? metersPerWorldUnit : 1f,
                CoulombsPerChargeUnit = IsPositiveFinite(coulombsPerChargeUnit) ? coulombsPerChargeUnit : 1f
            };

            // Unity 물리에 NaN/Infinity를 전달하지 않는다. 잘못된 설정은 힘을 비활성화한다.
            if (!IsFinite(position) || !IsFinite(source) || !IsFinite(charge)
                || !IsFinite(sourceCharge) || !IsPositiveFinite(metersPerWorldUnit)
                || !IsPositiveFinite(coulombsPerChargeUnit) || !IsPositiveFinite(relativePermittivity)
                || !IsFinite(distanceFloorUnity) || distanceFloorUnity < 0f
                || !IsFinite(maxForceNewtons) || maxForceNewtons < 0f
                || !IsFinite(radiusUnity) || radiusUnity < 0f)
            {
                return empty;
            }

            double constant = ElectricMath.EffectiveUnityCoulombConstant(metersPerWorldUnit,
                coulombsPerChargeUnit, relativePermittivity);
            double limit = (double)maxForceNewtons / metersPerWorldUnit;
            if (constant > float.MaxValue || constant < float.Epsilon || limit > float.MaxValue)
            {
                return empty;
            }

            float k = (float)constant;
            float floor = Mathf.Max(0.01f, distanceFloorUnity);
            Vector2 offset = position - source;
            float distanceSquared = offset.sqrMagnitude;
            // PointCharge는 float로 계산하므로 중간값도 유한하게 유지한다.
            if (!IsFinite(offset) || !IsFinite(distanceSquared) || !IsFinite(floor * floor)
                || !IsFinite(k * charge) || !IsFinite(k * charge * sourceCharge)
                || !IsFinite(k * sourceCharge))
            {
                return empty;
            }

            var sample = PointCharge(position, source, charge, sourceCharge, k, floor,
                (float)limit, canApply, radiusUnity == 0f ? float.MaxValue : radiusUnity, false);
            if (!IsFinite(sample.Force) || !IsFinite(sample.Field)
                || !IsFinite(sample.TheoreticalForceMagnitude))
            {
                return empty;
            }
            sample.UsesSIUnits = true;
            sample.MetersPerWorldUnit = metersPerWorldUnit;
            sample.CoulombsPerChargeUnit = coulombsPerChargeUnit;
            return sample;
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool IsFinite(Vector2 value) => IsFinite(value.x) && IsFinite(value.y);
        private static bool IsPositiveFinite(float value) => value > 0f && IsFinite(value);

        private static float FiniteFloat(double value)
        {
            if (double.IsNaN(value)) return 0f;
            if (value > float.MaxValue) return float.MaxValue;
            if (value < -float.MaxValue) return -float.MaxValue;
            return (float)value;
        }

        private static Vector2 ScaleFinite(Vector2 value, double scale)
        {
            return new Vector2(FiniteFloat(value.x * scale), FiniteFloat(value.y * scale));
        }

        /// <summary>
        /// 기존 게임 단위의 점전하 계산. SI 사용자는 PointChargeSI로 단위를 명시한다.
        /// repulsionOnly는 발사대의 같은 부호 조건이며, 전기장 자체의 부호와는 별개다.
        /// </summary>
        internal static ElectricForceSample PointCharge(Vector2 position, Vector2 source,
            float charge, float sourceCharge, float constant, float distanceFloor,
            float forceLimit, bool canApply, float radius, bool repulsionOnly)
        {
            Vector2 offset = position - source;
            float distance = offset.magnitude;
            float k = Mathf.Max(0f, constant);
            float floor = Mathf.Max(0.01f, distanceFloor);
            float limit = Mathf.Max(0f, forceLimit);
            bool inRange = canApply && distance >= 0.001f && distance <= Mathf.Max(0f, radius);
            float theoretical = distance >= 0.001f
                ? Mathf.Abs(k * charge * sourceCharge / (distance * distance))
                : 0f;
            float effectiveDistance = Mathf.Max(floor, distance);
            Vector2 direction = distance >= 0.001f ? offset / distance : Vector2.zero;
            float force = ElectricMath.CoulombForce(charge, sourceCharge, distance, k, floor, limit);
            bool eligible = inRange && (!repulsionOnly || charge * sourceCharge > 0f);

            // 전기장은 시험 전하 q에 의존하지 않는다. 중성 공에도 E는 있고 F만 0이다.
            return new ElectricForceSample
            {
                Position = position,
                SignedCharge = charge,
                SourceCharge = sourceCharge,
                Distance = distance,
                Field = inRange
                    ? direction * (k * sourceCharge / (effectiveDistance * effectiveDistance))
                    : Vector2.zero,
                Force = eligible ? direction * force : Vector2.zero,
                TheoreticalForceMagnitude = theoretical,
                IsLimited = theoretical > 0f && (distance < floor || theoretical > limit),
                IsInRange = inRange
            };
        }
    }
}
