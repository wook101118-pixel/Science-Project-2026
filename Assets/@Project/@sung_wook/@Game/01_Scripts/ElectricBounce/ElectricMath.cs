using System;

namespace sung_wook
{
                 
                                                     
                                                           
                  
    public static class ElectricMath
    {
                                                                
        public const double VacuumCoulombConstant = 8.9875517862e9;

                     
                                                           
                                                                   
                      
        public static double EffectiveUnityCoulombConstant(double metersPerWorldUnit,
            double coulombsPerChargeUnit, double relativePermittivity = 1d)
        {
            RequirePositiveFinite(metersPerWorldUnit, nameof(metersPerWorldUnit));
            RequirePositiveFinite(coulombsPerChargeUnit, nameof(coulombsPerChargeUnit));
            RequirePositiveFinite(relativePermittivity, nameof(relativePermittivity));

                                                        
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

                     
                                                    
                                            
                      
        public static float CoulombForce(float q1, float q2, float distance,
            float k, float minimumDistance, float maximumForce)
        {
            float r = Math.Max(Math.Abs(distance), Math.Max(0.01f, minimumDistance));
            float force = Math.Max(0f, k) * q1 * q2 / (r * r);
            float limit = Math.Max(0f, maximumForce);
            return Math.Max(-limit, Math.Min(limit, force));
        }

                                                                  
        public static float FieldForce(float charge, float voltage, float gap)
        {
            return charge * voltage / Math.Max(0.01f, Math.Abs(gap));
        }

                                                                       
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
