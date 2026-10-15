using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace sung_wook
{
    public class GearLeverController : MonoBehaviour
    {
        private GearLever gearLever;
        private Camera _mainCamera;
        private Vector3 mousePosition;
        
        private LeverReader leverReader;
        [SerializeField]
        private LayerMask _raycastLayerMask;
        
        private void Awake()
        {
            _mainCamera = Camera.main;
            leverReader = GetComponentInChildren<LeverReader>();
            gearLever = GetComponent<GearLever>();
        }

        private void Update()
        {
            
        }

        private void OnClick()
        {
            Vector2 worldPos = _mainCamera.ScreenToWorldPoint(Mouse.current.position.ReadValue());
            RaycastHit2D hit = Physics2D.Raycast(worldPos, Vector2.zero, 0f, _raycastLayerMask);
        
            if (hit.collider != null)
            {
                Debug.Log(gearLever.CurrentState);
            }
        }
    }
}

