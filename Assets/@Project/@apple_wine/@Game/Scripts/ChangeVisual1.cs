using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace apple_wine
{
    public class ChangeVisual1 : MonoBehaviour
    {
        public GameObject root;
        
        public GameObject _positiveVisual;
        public GameObject _negativeVisual;

        private GameObject[] _visuals;

        private void Awake()
        {
                                                 
            Setup();
        }

        private void Start()
        {
            ChangeState(ElectricChargeType.Positive, ElectricChargeType.Positive);
        }

        public void Setup()
        { 
            _visuals                                     = new GameObject[2];
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