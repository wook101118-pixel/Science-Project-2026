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

        private void Awake()
        {
            // 추후 모든 스크립트의 Setup은 GameManager가 담당
            Setup();
        }

        private void Update()
        {
            if (Keyboard.current.tKey.wasPressedThisFrame)
            {
                ChangeState(ElectricChargeType.Negative, ElectricChargeType.Neutral);
            }
        }

        public void Setup()
        { 
            _visuals                                     = new GameObject[3];
            _visuals[(int)ElectricChargeType.Neutral]    = CreateGameObject(_neutralVisual, "NeutralVisual");
            _visuals[(int)ElectricChargeType.Positive]   = CreateGameObject(_positiveVisual, "PositiveVisual");
            _visuals[(int)ElectricChargeType.Negative]   = CreateGameObject(_negativeVisual, "NegativeVisual");
        }

        private GameObject CreateGameObject(GameObject go, string name = "GameObject")
        {
            GameObject visual = Instantiate(go, root.transform);
            visual.name = name;
            visual.SetActive(false);
            return visual;
        }
        
        public void ChangeState(ElectricChargeType currentState, ElectricChargeType newState)
        {
            _visuals[(int)currentState].SetActive(false);
            
            _visuals[(int)newState].SetActive(true);   
        }
    }
}