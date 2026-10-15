using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace sung_wook
{
    public class ChangeVisual : MonoBehaviour
    {
        public GameObject root;
        
        public GameObject _neutralVisual;
        public GameObject _positiveVisual;
        public GameObject _negativeVisual;

        private GameObject[] _visuals;

        public PlayerStateType CurrentVisualState { get; private set; }
        
        private void Awake()
        {
            // 추후 모든 스크립트의 Setup은 GameManager가 담당
            Setup();
        }

        public void Setup()
        { 
            _visuals                                     = new GameObject[3];
            _visuals[(int)PlayerStateType.Neutral]    = CreateGameObject(_neutralVisual, "NeutralVisual");
            _visuals[(int)PlayerStateType.Positive]   = CreateGameObject(_positiveVisual, "PositiveVisual");
            _visuals[(int)PlayerStateType.Negative]   = CreateGameObject(_negativeVisual, "NegativeVisual");
            
            _visuals[(int)CurrentVisualState].SetActive(true);
        }

        private GameObject CreateGameObject(GameObject go, string name = "GameObject")
        {
            GameObject visual = Instantiate(go, root.transform);
            visual.name = name;
            visual.SetActive(false);
            return visual;
        }
        
        public void VisualChange(PlayerStateType newState)
        {
            _visuals[(int)CurrentVisualState]?.SetActive(false);
            
            CurrentVisualState = newState;
            
            _visuals[(int)CurrentVisualState]?.SetActive(true);
        }
    }
}