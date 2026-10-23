using UnityEngine;

namespace sung_wook
{
    /// <summary>전하 상태별 시각 오브젝트의 생성·활성화만 담당하며 플레이어 상태는 변경하지 않는다.</summary>
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

        /// <summary>이미 배치된 중성 표시를 재사용하고 필요한 전하 표시를 한 번만 생성한다.</summary>
        public virtual void Setup()
        {
            if (_visuals != null) return;
            if (root == null) root = gameObject;
            // 현재 Player에 이미 배치된 회색 Visual/0을 중복 생성하지 않고 재사용한다.
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
        
        /// <summary>호출자가 전달한 전하 상태에 해당하는 표시 하나만 활성화한다.</summary>
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
