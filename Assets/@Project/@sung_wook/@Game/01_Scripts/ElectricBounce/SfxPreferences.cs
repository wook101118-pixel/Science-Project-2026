using System;
using UnityEngine;

namespace sung_wook
{
                                                                
    public static class SfxPreferences
    {
        public const string VolumeKey = "ElectricBounce.Audio.SfxVolume";
        public const string MuteKey = "ElectricBounce.Audio.SfxMuted";

        private static bool loaded;
        private static float volume = 1f;
        private static bool muted;
        private static bool dirty;

        public static event Action Changed;
        public static float Volume { get { EnsureLoaded(); return volume; } }
        public static bool IsMuted { get { EnsureLoaded(); return muted; } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            loaded = false;
            volume = 1f;
            muted = false;
            dirty = false;
            Changed = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void EnsureLoaded()
        {
            if (loaded) return;
            float savedVolume = PlayerPrefs.GetFloat(VolumeKey, 1f);
            volume = float.IsNaN(savedVolume) || float.IsInfinity(savedVolume) ? 1f : Mathf.Clamp01(savedVolume);
            muted = PlayerPrefs.GetInt(MuteKey, 0) != 0;
            loaded = true;
        }

        public static void SetVolume(float value)
        {
            EnsureLoaded();
            if (float.IsNaN(value) || float.IsInfinity(value)) return;
            value = Mathf.Clamp01(value);
            if (Mathf.Approximately(volume, value)) return;
            volume = value;
            PlayerPrefs.SetFloat(VolumeKey, volume);
            dirty = true;
            Save();
            Changed?.Invoke();
        }

        public static void SetMuted(bool value)
        {
            EnsureLoaded();
            if (muted == value) return;
            muted = value;
            PlayerPrefs.SetInt(MuteKey, muted ? 1 : 0);
            dirty = true;
            Save();
            Changed?.Invoke();
        }

        public static void Save()
        {
            if (!dirty) return;
            PlayerPrefs.Save();
            dirty = false;
        }
    }
}
