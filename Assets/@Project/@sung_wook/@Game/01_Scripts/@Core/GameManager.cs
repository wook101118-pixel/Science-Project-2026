using System;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

namespace sung_wook
{
    public class GameManager : Singleton<GameManager>
    {
        [field : SerializeField]public PlayerController PlayerController {get; private set;}
        [field : SerializeField]public Player PlayerCompo {get; private set;}
        [field : SerializeField]public GearLeverController GearLeverController {get; private set;}
        [field : SerializeField]public GearLever LeverCompo {get; private set;}
        
        private void Awake()
        {
            PlayerCompo.Setup();
        }

        private void Start()
        { 
        }

        private void OnEnable()
        {
            
        }

        private void CheckSameState()
        {
            string playerState =  PlayerCompo.CurrentState.ToString();
            string leverState =  LeverCompo.CurrentState.ToString();

            if (Equals(playerState, leverState))
            {
                PlayerController.AddForce(GearLeverController.transform, 1, ForceMode2D.Force);
            }
        }

        private void Update()
        {
            CheckSameState();
        }

        private void OnDisable()
        {
            
        }
    }
}