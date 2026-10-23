using System;
using System.Collections.Generic;
using UnityEngine;

namespace sung_wook
{
    public enum SfxCue { Bounce, Charge, Star, Launch, Record, LearningComplete, StageClear, UiClick }

                                                                            
    [DefaultExecutionOrder(-900)]
    [DisallowMultipleComponent]
    public sealed class ElectricSfxPlayer : MonoBehaviour
    {
        [Serializable]
        public sealed class CueDefinition
        {
            public SfxCue cue;
            public AudioClip clip;
            [Range(0f, 1f)] public float volume = 0.5f;
            [Range(0.5f, 2f)] public float pitch = 1f;
            [Min(0f)] public float minimumInterval = 0.06f;
        }

        [SerializeField] private AudioSource[] gameplaySources;
        [SerializeField] private AudioSource uiSource;
        [SerializeField] private CueDefinition[] cues;
        [SerializeField, Range(0f, 1f)] private float masterVolume = 0.7f;

        private static ElectricSfxPlayer instance;
        private readonly float[] lastPlayedTimes = new float[8];
        private readonly Dictionary<AudioSource, float> sourceVolumes = new Dictionary<AudioSource, float>();
        private int nextSource;
        private bool gameplayPaused;

        public static bool IsAvailable => instance != null && instance.isActiveAndEnabled;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => instance = null;

        private void Awake()
        {
                                                              
            if (instance != null && instance != this) { enabled = false; return; }
            if (gameplaySources == null || gameplaySources.Length == 0 || uiSource == null)
            {
                Debug.LogError("Electric SFX의 저장된 AudioSource 참조가 비었습니다.", this);
                enabled = false;
                return;
            }
            instance = this;
            for (int i = 0; i < lastPlayedTimes.Length; i++) lastPlayedTimes[i] = float.NegativeInfinity;
                                                         
            uiSource.ignoreListenerPause = true;
        }

        private void OnEnable()
        {
            if (instance != this) return;
            SfxPreferences.Changed += ApplyPreferences;
            ApplyPreferences();
        }

        private void OnDisable()
        {
            if (instance != this) return;
            SfxPreferences.Changed -= ApplyPreferences;
            SfxPreferences.Save();
        }

        private void OnApplicationPause(bool paused) { if (paused) SfxPreferences.Save(); }
        private void OnApplicationQuit() => SfxPreferences.Save();

        private void LateUpdate() => SetGameplayPaused(Time.timeScale <= 0f || SceneTransitionController.IsBusy);

        public static bool Play(SfxCue cue)
        {
            if (!IsAvailable || (int)cue < 0 || (int)cue >= instance.lastPlayedTimes.Length) return false;
            return instance.PlayCue(cue);
        }

        private bool PlayCue(SfxCue cue)
        {
            bool menuSound = cue == SfxCue.UiClick || cue == SfxCue.StageClear;
            if (!menuSound && (gameplayPaused || Time.timeScale <= 0f || SceneTransitionController.IsBusy)) return false;
            if (cue == SfxCue.StageClear && SceneTransitionController.IsBusy) return false;

            CueDefinition definition = null;
            if (cues != null)
                foreach (CueDefinition candidate in cues)
                    if (candidate != null && candidate.cue == cue) { definition = candidate; break; }
            if (definition == null || definition.clip == null
                || Time.unscaledTime - lastPlayedTimes[(int)cue] < definition.minimumInterval) return false;

            AudioSource source = menuSound ? uiSource : AvailableGameplaySource(cue);
            if (source == null) return false;
            source.clip = definition.clip;
            sourceVolumes[source] = definition.volume;
            ApplySourcePreferences(source, definition.volume);
            source.pitch = Mathf.Clamp(definition.pitch, 0.5f, 2f);
            source.Play();
            lastPlayedTimes[(int)cue] = Time.unscaledTime;
            return true;
        }

        private void ApplyPreferences()
        {
            foreach (AudioSource source in gameplaySources)
            {
                if (source == null) continue;
                sourceVolumes.TryGetValue(source, out float cueVolume);
                ApplySourcePreferences(source, cueVolume);
            }
            if (uiSource != null)
            {
                sourceVolumes.TryGetValue(uiSource, out float cueVolume);
                ApplySourcePreferences(uiSource, cueVolume);
            }
        }

        private void ApplySourcePreferences(AudioSource source, float cueVolume)
        {
            source.volume = Mathf.Clamp01(masterVolume * cueVolume * SfxPreferences.Volume);
            source.mute = SfxPreferences.IsMuted;
        }

        private AudioSource AvailableGameplaySource(SfxCue cue)
        {
            for (int i = 0; i < gameplaySources.Length; i++)
            {
                int index = (nextSource + i) % gameplaySources.Length;
                AudioSource source = gameplaySources[index];
                if (source == null || !source.isActiveAndEnabled || source.isPlaying) continue;
                nextSource = (index + 1) % gameplaySources.Length;
                return source;
            }
                                                
            if (cue == SfxCue.Bounce) return null;
            AudioSource replacement = gameplaySources[nextSource];
            nextSource = (nextSource + 1) % gameplaySources.Length;
            return replacement;
        }

        public static void SetGameplayPaused(bool paused)
        {
            if (!IsAvailable || instance.gameplayPaused == paused) return;
            instance.gameplayPaused = paused;
            foreach (AudioSource source in instance.gameplaySources)
            {
                if (source == null) continue;
                if (paused) source.Pause();
                else source.UnPause();
            }
        }

        public static void StopGameplay()
        {
            if (!IsAvailable) return;
            foreach (AudioSource source in instance.gameplaySources)
                if (source != null) source.Stop();
        }

        private void OnDestroy()
        {
            if (instance != this) return;
            StopGameplay();
            if (uiSource != null) uiSource.Stop();
            instance = null;
        }
    }
}
