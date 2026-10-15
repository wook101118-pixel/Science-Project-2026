using System;
using UnityEngine;

namespace apple_wine
{
    public class ElectricityBox : MonoBehaviour
    {
        private ElectricCharge _electricCharge;
        private bool _isNegative;

        private void Awake()
        {
            _electricCharge = GetComponent<ElectricCharge>();
        }

        public void BoxStateChange()
        {
            _isNegative = !_isNegative;
            
            if (_isNegative)
            {
                Debug.Log("음전하");
                _electricCharge.ChangeState(ElectricChargeType.Negative);
            }
            else if (!_isNegative)
            {
                Debug.Log("양전하");
                _electricCharge.ChangeState(ElectricChargeType.Positive);
            }
        }
    }
}