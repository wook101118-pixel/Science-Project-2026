using UnityEngine;

namespace sung_wook
{
    public enum SoundType {}
    
    public enum Sfx
    {
        Dead,
        Hit,
        LevelUp = 3,
        Lose,
        Melee,
        Range = 7,
        Select,
        Win
    }

    public class SoundManager : Singleton<SoundManager>
    {
        [Range(0f, 1f)] public float bgmVolume = 0.1f;
        
        [Header("# SFX")]
        public AudioClip[] sfxClips;
        [Range(0f, 1f)] public float sfxVolume = 0.5f;
        [Min(1)] public int channels = 16;
        
        private AudioSource _bgmPlayer;
        private AudioHighPassFilter _bgmEffect;
        private AudioSource[] _sfxPlayers;
        private int _channelIndex;

        public AudioSource audioSources;
        
        public AudioClip[] clips;

        private void Awake()
        {
            Setup();
            InitializePlayers();
        }

        private void Setup()
        {
            audioSources = GetComponent<AudioSource>();
        }
        
        private void InitializePlayers()
        {
            GameObject bgmObject = new GameObject("BgmPlayer");
            bgmObject.transform.SetParent(transform, false);
            _bgmPlayer = bgmObject.AddComponent<AudioSource>();
            _bgmPlayer.playOnAwake = false;
            _bgmPlayer.loop = true;
            _bgmPlayer.volume = bgmVolume;
            //_bgmPlayer.clip = bgmClip;
        
            if (Camera.main)
                _bgmEffect = Camera.main.GetComponent<AudioHighPassFilter>();
        
            GameObject sfxObject = new GameObject("SfxPlayer");
            sfxObject.transform.SetParent(transform, false);
            _sfxPlayers = new AudioSource[Mathf.Max(1, channels)];
        
            for (int index = 0; index < _sfxPlayers.Length; index++)
            {
                AudioSource source = sfxObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.bypassListenerEffects = true;
                source.volume = sfxVolume;
                _sfxPlayers[index] = source;
            }
        }

        public void PlaySound(SoundType soundType)
        {
            
        }
        
        public void PlayBgm(bool isPlay)
        {
            if (!_bgmPlayer || !_bgmPlayer.clip) return;
        
            if (isPlay)
            {
                if (!_bgmPlayer.isPlaying) _bgmPlayer.Play();
            }
            else
            {
                _bgmPlayer.Stop();
            }
        }
        
        public void EffectBgm(bool isPlay)
        {
            if (_bgmEffect) _bgmEffect.enabled = isPlay;
        }
        
        public void PlaySfx(Sfx sfx)
        {
            if (_sfxPlayers == null || sfxClips == null) return;
        
            for (int index = 0; index < _sfxPlayers.Length; index++)
            {
                int loopIndex = (index + _channelIndex) % _sfxPlayers.Length;
                if (_sfxPlayers[loopIndex].isPlaying) continue;
        
                int randomOffset = sfx == Sfx.Hit || sfx == Sfx.Melee ? Random.Range(0, 2) : 0;
                int clipIndex = (int)sfx + randomOffset;
                if (clipIndex < 0 || clipIndex >= sfxClips.Length || !sfxClips[clipIndex]) return;
        
                _channelIndex = loopIndex;
                _sfxPlayers[loopIndex].clip = sfxClips[clipIndex];
                _sfxPlayers[loopIndex].Play(); 
                return; 
            }
        }
    }   
}