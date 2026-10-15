using System;
using UnityEngine;

namespace apple_wine
{
    public class ElectricityBox : MonoBehaviour
    {
        //[SerializeField]private 
            
        private ElectricCharge _electricCharge;
        private bool _isNegative;
        private ChangeVisual1 _visualChanger;

        private void Awake()
        {
            _electricCharge = GetComponent<ElectricCharge>();
            _visualChanger = GetComponent<ChangeVisual1>();
        }

        public void BoxStateChange()
        {
            _isNegative = !_isNegative;
            
            if (_isNegative)
            {
                Debug.Log("음전하");
                _electricCharge.ChangeState(ElectricChargeType.Negative);
                _visualChanger.ChangeState(ElectricChargeType.Positive, ElectricChargeType.Negative);
            }
            else if (!_isNegative)
            {
                Debug.Log("양전하");
                _electricCharge.ChangeState(ElectricChargeType.Positive);
                _visualChanger.ChangeState(ElectricChargeType.Negative, ElectricChargeType.Positive);
                
            }
        }
    }
}