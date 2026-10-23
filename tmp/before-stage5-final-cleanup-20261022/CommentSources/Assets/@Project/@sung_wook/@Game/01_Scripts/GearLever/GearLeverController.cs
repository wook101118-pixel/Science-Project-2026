using UnityEngine;

namespace sung_wook
{
    /// <summary>
    /// 기존 LeverReader와 스테이지용 StageLever2D의 참조를 연결한다.
    /// 실제 마우스 입력과 손잡이 회전은 기존 LeverMover가 계속 담당한다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(GearLever))]
    public class GearLeverController : MonoBehaviour
    {
        private GearLever gearLever;

        // 필드명과 타입을 유지해 기존 씬·프리팹의 Inspector 연결을 보존한다.
        [SerializeField] private LayerMask _raycastLayerMask;
        [SerializeField] private global::LeverReader _leverReader;
        [SerializeField] private StageLever2D stageLever;

        public global::LeverReader Reader => _leverReader;
        public StageLever2D StageLever => stageLever;
        public float LeverValue => stageLever != null ? stageLever.Value : 0f;
        public LayerMask RaycastLayerMask => _raycastLayerMask;

        private void Reset() => ResolveReferences();

        private void Awake()
        {
            ResolveReferences();
            if (gearLever != null) gearLever.Setup();
        }

        /// <summary>설치 도구가 전달한 어댑터를 기존 입력 리더에 연결한다.</summary>
        public void ConfigureLever(StageLever2D inputLever)
        {
            stageLever = inputLever;
            if (stageLever != null && stageLever.Reader != null) _leverReader = stageLever.Reader;
            ResolveReferences();
            if (gearLever != null) gearLever.Setup();
        }

        /// <summary>이미 지정된 참조를 우선하고 누락된 연결만 같은 레버 계층에서 보완한다.</summary>
        private void ResolveReferences()
        {
            if (gearLever == null) gearLever = GetComponent<GearLever>();
            if (stageLever == null) stageLever = GetComponentInChildren<StageLever2D>(true);
            if (_leverReader == null && stageLever != null) _leverReader = stageLever.Reader;
            if (_leverReader == null) _leverReader = GetComponentInChildren<global::LeverReader>(true);
            if (stageLever != null && stageLever.Reader == null && _leverReader != null)
            {
                stageLever.Configure(_leverReader);
            }
        }
    }
}

