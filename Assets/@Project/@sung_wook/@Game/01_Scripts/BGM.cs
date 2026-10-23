using UnityEngine;
using UnityEngine.Serialization;

namespace sung_wook
{
                                                                   
    [RequireComponent(typeof(AudioSource))]
    [DisallowMultipleComponent]
    public class BGM : Singleton<BGM>
    {
        private static BGM activePlayer;
        private AudioSource audioSource;
        private bool playOnStart;

        [FormerlySerializedAs("<Volume>k__BackingField")]
        [SerializeField, Range(0f, 1f)] private float volume = 0.5f;

                                                    
        public float Volume
        {
            get => volume;
            set
            {
                if (float.IsNaN(value) || float.IsInfinity(value)) return;
                volume = Mathf.Clamp01(value);
                ApplyPreferences();
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => activePlayer = null;

        private void Awake()
        {
            audioSource = GetComponent<AudioSource>();
            playOnStart = audioSource.playOnAwake;
                                              
            audioSource.playOnAwake = false;
            if (activePlayer != null && activePlayer != this)
            {
                audioSource.Stop();
                Destroy(gameObject);
                return;
            }

            activePlayer = this;
            transform.SetParent(null, true);
            DontDestroyOnLoad(gameObject);
            ApplyPreferences();
        }

        private void OnEnable()
        {
            if (activePlayer != this) return;
            BGMPreferences.Changed += ApplyPreferences;
            ApplyPreferences();
        }

        private void Start()
        {
            if (activePlayer == this && playOnStart && audioSource.clip != null && !audioSource.isPlaying)
                audioSource.Play();
        }

        private void OnDisable()
        {
            BGMPreferences.Changed -= ApplyPreferences;
            BGMPreferences.Save();
        }

        private void ApplyPreferences()
        {
            if (audioSource == null || activePlayer != this) return;
            audioSource.volume = Mathf.Clamp01(volume) * BGMPreferences.Volume;
            audioSource.mute = BGMPreferences.IsMuted;
        }

        private void OnValidate()
        {
            volume = float.IsNaN(volume) || float.IsInfinity(volume) ? 0.5f : Mathf.Clamp01(volume);
            if (Application.isPlaying) ApplyPreferences();
        }

        private void OnApplicationPause(bool paused) { if (paused) BGMPreferences.Save(); }
        private void OnApplicationQuit() => BGMPreferences.Save();
        private void OnDestroy() { if (activePlayer == this) activePlayer = null; }
    }
}
