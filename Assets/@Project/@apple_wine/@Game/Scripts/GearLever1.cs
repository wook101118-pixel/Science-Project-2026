using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace apple_wine
{
    public class GearLever : MonoBehaviour
    {
        [SerializeField]private  ElectricityBox _electricChargeBox;
        [SerializeField] private LayerMask _raycastLayerMask;

        private Stack<GameObject> boxStack = new Stack<GameObject>();
        private bool isActive = true;
        private Camera _mainCamera;
        private Vector3 mousePosition;

        private void Awake()
        {
            _mainCamera = Camera.main;
        }

        private void Update()
        {
            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                BoxStateChange();
            }

            if (Mouse.current.rightButton.wasPressedThisFrame)
            {
                BoxOnOff();
            }
        }

        private bool OnClick()
        {
            Vector2 worldPos = _mainCamera.ScreenToWorldPoint(Mouse.current.position.ReadValue());
            RaycastHit2D hit = Physics2D.Raycast(worldPos, Vector2.zero, 0f, _raycastLayerMask);
            return hit.collider != null;

        }

        private GameObject OnCheckObject()
        {
            Vector2 worldPos = _mainCamera.ScreenToWorldPoint(Mouse.current.position.ReadValue());
            RaycastHit2D hit = Physics2D.Raycast(worldPos, Vector2.zero, 0f, _raycastLayerMask);
            return hit.collider.gameObject;
        }

        private void BoxStateChange()
        {
            if (!isActive) return;
            
            if (OnClick())
            {
                _electricChargeBox.BoxStateChange();
            }
        }

        private void BoxOnOff()
        {
            isActive =!isActive;
            if (!isActive)
            {
                _electricChargeBox.enabled = false;
            }
            else
            {
                _electricChargeBox.enabled = true;
            }
        }
    }
}

