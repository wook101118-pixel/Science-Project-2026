using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace sung_wook
{
    public class GearLever : MonoBehaviour
    {
        private Camera _mainCamera;
        private Vector3 mousePosition;
        [SerializeField]
        private LayerMask _raycastLayerMask;
        
        private void Awake()
        {
            _mainCamera = Camera.main;
        }

        private void Update()
        {
            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                OnClick();
            }
        }

        private void OnClick()
        {
            Vector2 worldPos = _mainCamera.ScreenToWorldPoint(Mouse.current.position.ReadValue());
            RaycastHit2D hit = Physics2D.Raycast(worldPos, Vector2.zero, 0f, _raycastLayerMask);

            if (hit.collider != null)
            {
                print(hit.collider.name);
            }
        }
    }
}

