using UnityEngine;

namespace sung_wook
{
                                                                  
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public sealed class DisplayPreferencesMonitor : MonoBehaviour
    {
        private static DisplayPreferencesMonitor instance;

        internal static void ResetInstance() => instance = null;

        internal static void EnsureCreated()
        {
            if (instance != null) return;
            instance = Object.FindFirstObjectByType<DisplayPreferencesMonitor>(FindObjectsInactive.Include);
            if (instance != null)
            {
                instance.enabled = true;
                return;
            }
            new GameObject("Display Preferences").AddComponent<DisplayPreferencesMonitor>();
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Update() => DisplayPreferences.ObserveNativeScreen();

        private void OnApplicationQuit() => DisplayPreferences.Save();

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }
    }
}
