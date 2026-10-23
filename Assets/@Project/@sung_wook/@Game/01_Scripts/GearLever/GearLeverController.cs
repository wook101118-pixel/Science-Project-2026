using UnityEngine;

namespace sung_wook
{
                 
                                                     
                                                  
                  
    [DisallowMultipleComponent]
    [RequireComponent(typeof(GearLever))]
    public class GearLeverController : MonoBehaviour
    {
        private GearLever gearLever;

                                                     
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

                                                              
        public void ConfigureLever(StageLever2D inputLever)
        {
            stageLever = inputLever;
            if (stageLever != null && stageLever.Reader != null) _leverReader = stageLever.Reader;
            ResolveReferences();
            if (gearLever != null) gearLever.Setup();
        }

                                                                       
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

