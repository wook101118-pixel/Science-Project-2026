using UnityEngine;

namespace sung_wook
{
    /// <summary>
    /// 기존 LeverReader에서 단방향 0~1 및 양방향 -1~1 실험 입력을 읽는 어댑터.
    /// 손잡이 회전과 마우스 드래그는 기존 레버가 담당하고 이 컴포넌트는 값을 읽기만 한다.
    /// </summary>
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

        /// <summary>레버 중앙은 0, 양전하 끝은 +1, 음전하 끝은 -1인 원래 입력이다.</summary>
        public float SignedValue
        {
            get
            {
                if (useManualValue || reader == null) return Mathf.Clamp(manualValue, -1f, 1f);
                return TryReadRawValue(out float raw) ? raw * 2f - 1f : 0f;
            }
        }

        /// <summary>휴지 위치를 0, 작동 위치를 1로 읽는다. 수동 모드는 명시적으로 선택할 때만 우선한다.</summary>
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
            // 전역 검색으로 다른 스테이지의 레버가 우연히 연결되지 않게 자기 계층만 탐색한다.
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
                // 기존 외부 LeverReader는 내부 LeverMover 연결이 비면 예외를 던진다.
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

        /// <summary>기존 리더와 보정 끝점을 연결하고 실제 레버 입력을 다시 사용한다.</summary>
        public void Configure(global::LeverReader existingReader, float restingValue = 0.5f,
            float activeValue = 1f)
        {
            reader = existingReader;
            restRaw = Mathf.Clamp01(restingValue);
            activeRaw = Mathf.Clamp01(activeValue);
            useManualValue = false;
            readerWarningReported = false;
        }

        /// <summary>학습 UI나 검증에서 수동 입력을 명시적으로 선택한다. 기존 리더 참조는 보존한다.</summary>
        public void SetValue(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return;
            manualValue = Mathf.Clamp01(value);
            useManualValue = true;
        }

        /// <summary>양방향 실험 입력. 기존 Value를 사용하는 장치에는 여전히 0~1만 전달한다.</summary>
        public void SetSignedValue(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return;
            manualValue = Mathf.Clamp(value, -1f, 1f);
            useManualValue = true;
        }

        /// <summary>수동 제어를 끝내고 원래 레버의 드래그 입력으로 복귀한다.</summary>
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
