using UnityEngine;

namespace sung_wook
{
                                                                          
    public class ChangeVisual : MonoBehaviour
    {
        public GameObject root;
        
        public GameObject _neutralVisual;
        public GameObject _positiveVisual;
        public GameObject _negativeVisual;
        [SerializeField] protected GameObject existingNeutralVisual;

        private GameObject[] _visuals;

        public PlayerStateType CurrentVisualState { get; protected set; }
        
        protected virtual void Awake() => Setup();

                                                                        
        public virtual void Setup()
        {
            if (_visuals != null) return;
            if (root == null) root = gameObject;
                                                               
            if (existingNeutralVisual == null)
            {
                Transform authoredVisual = root.transform.Find("Visual");
                if (authoredVisual != null) existingNeutralVisual = authoredVisual.gameObject;
            }
            _visuals = new GameObject[3];
            _visuals[(int)PlayerStateType.Neutral] = existingNeutralVisual != null
                ? existingNeutralVisual : CreateGameObject(_neutralVisual, "NeutralVisual");
            _visuals[(int)PlayerStateType.Positive] = CreateGameObject(_positiveVisual, "PositiveVisual");
            _visuals[(int)PlayerStateType.Negative] = CreateGameObject(_negativeVisual, "NegativeVisual");
            foreach (GameObject visual in _visuals)
            {
                if (visual != null) visual.SetActive(false);
            }
            
            if (_visuals[(int)CurrentVisualState] != null)
                _visuals[(int)CurrentVisualState].SetActive(true);
        }

        private GameObject CreateGameObject(GameObject go, string name = "GameObject")
        {
            if (go == null) return null;
            GameObject visual = Instantiate(go, root.transform);
            visual.name = name;
            visual.SetActive(false);
            return visual;
        }
        
                                                                 
        public virtual void VisualChange(PlayerStateType newState)
        {
            Setup();
            if (_visuals[(int)CurrentVisualState] != null)
                _visuals[(int)CurrentVisualState].SetActive(false);
            
            CurrentVisualState = newState;
            
            if (_visuals[(int)CurrentVisualState] != null)
                _visuals[(int)CurrentVisualState].SetActive(true);
        }
    }
}
