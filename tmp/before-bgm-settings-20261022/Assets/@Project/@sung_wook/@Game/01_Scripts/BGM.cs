using System;

using UnityEngine;

namespace sung_wook
{
    public class BGM : Singleton<BGM>
    {
        private AudioSource audioSource;
        [field:SerializeField]public float Volume { get; set; } = 0.5f;
    
        private void Awake()
        {
            audioSource = GetComponent<AudioSource>();
        }

        private void Update()
        {
            audioSource.volume = Volume;
        }
    }   
}
