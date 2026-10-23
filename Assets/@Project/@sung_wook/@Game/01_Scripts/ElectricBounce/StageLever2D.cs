using UnityEngine;

namespace sung_wook
{
                 
                                                          
                                                       
                  
    [DisallowMultipleComponent]
    public sealed class StageLever2D : MonoBehaviour
    {
        [SerializeField] private global::LeverReader reader;
        [SerializeField, Range(0f, 1f)] private float restRaw = 0.5f;
        [SerializeField, Range(0f, 1f)] private float activeRaw = 1f;
        [SerializeField, Range(-1f, 1f)] private float manualValue;
        [SerializeField] private bool useManualValue;

        private bool readerWarningReported;

        public global::LeverReader Reader => reader;

                                                                       
        public float SignedValue
        {
            get
            {
                if (useManualValue || reader == null) return Mathf.Clamp(manualValue, -1f, 1f);
                return TryReadRawValue(out float raw) ? raw * 2f - 1f : 0f;
            }
        }

                                                                                 
        public float Value
        {
            get
            {
                if (useManualValue || reader == null)
                {
                    return Mathf.Clamp01(manualValue);
                }

                float span = activeRaw - restRaw;
                if (Mathf.Abs(span) < 0.001f || !TryReadRawValue(out float raw))
                {
                    return 0f;
                }
                return Mathf.Clamp01((raw - restRaw) / span);
            }
        }

        private void Reset() => ResolveReader();
        private void Awake() => ResolveReader();

        private void ResolveReader()
        {
                                                            
            if (reader == null)
            {
                reader = GetComponentInChildren<global::LeverReader>(true);
            }
        }

        private bool TryReadRawValue(out float raw)
        {
            raw = 0f;
            try
            {
                raw = reader.Percent / 100f;
            }
            catch (System.NullReferenceException)
            {
                                                                   
                ReportMissingMover();
                return false;
            }
            catch (MissingReferenceException)
            {
                ReportMissingMover();
                return false;
            }

            if (float.IsNaN(raw) || float.IsInfinity(raw))
            {
                return false;
            }
            raw = Mathf.Clamp01(raw);
            return true;
        }

                                                                    
        public void Configure(global::LeverReader existingReader, float restingValue = 0.5f,
            float activeValue = 1f)
        {
            reader = existingReader;
            restRaw = Mathf.Clamp01(restingValue);
            activeRaw = Mathf.Clamp01(activeValue);
            useManualValue = false;
            readerWarningReported = false;
        }

                                                                             
        public void SetValue(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return;
            manualValue = Mathf.Clamp01(value);
            useManualValue = true;
        }

                                                                            
        public void SetSignedValue(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return;
            manualValue = Mathf.Clamp(value, -1f, 1f);
            useManualValue = true;
        }

                                                               
        public void UseLeverInput()
        {
            useManualValue = false;
            ResolveReader();
        }

        private void ReportMissingMover()
        {
            if (readerWarningReported)
            {
                return;
            }

            readerWarningReported = true;
            Debug.LogWarning("StageLever2D: connect the existing LeverReader's LeverMover reference.", this);
        }
    }
}
