using UnityEngine;
using UnityEngine.Serialization;

namespace sung_wook
{
    /// <summary>설정된 배경음을 한 번만 재생하고 씬 사이에서 음량과 음소거를 유지한다.</summary>
    [RequireComponent(typeof(AudioSource))]
    [DisallowMultipleComponent]
    public class BGM : Singleton<BGM>
    {
        private static BGM activePlayer;
        private AudioSource audioSource;
        private bool playOnStart;

        [FormerlySerializedAs("<Volume>k__BackingField")]
        [SerializeField, Range(0f, 1f)] private float volume = 0.5f;

        // 음악 자체의 기본 음량. 설정 슬라이더는 이 음량에 0~100%를 곱한다.
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
            // 저장된 음소거/음량을 적용한 뒤 Start에서 재생한다.
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
