using System;
using System.Collections.Generic;
using UnityEngine;

namespace sung_wook
{
                                                                        
    public static class DisplayPreferences
    {
        public const string WidthKey = "ElectricBounce.Display.Width";
        public const string HeightKey = "ElectricBounce.Display.Height";
        public const string FullscreenKey = "ElectricBounce.Display.Fullscreen";

        private static bool loaded;
        private static int selectedResolutionIndex;
        private static bool fullscreen;
        private static IReadOnlyList<Vector2Int> resolutions;
        private static bool dirty;
        private const float NativeSettleSeconds = 0.15f;
        private const float ApplyTimeoutSeconds = 3f;
        private static bool pendingApply;
        private static int requestedFrame;
        private static float applyDeadline;
        private static bool hasObservedScreen;
        private static Vector2Int observedSize;
        private static bool observedFullscreen;
        private static bool hasCandidateScreen;
        private static Vector2Int candidateSize;
        private static bool candidateFullscreen;
        private static float candidateSince;

        public static event Action Changed;
        public static IReadOnlyList<Vector2Int> Resolutions { get { EnsureLoaded(); return resolutions; } }
        public static int SelectedResolutionIndex { get { EnsureLoaded(); return selectedResolutionIndex; } }
        public static bool IsFullscreen { get { EnsureLoaded(); return fullscreen; } }
        public static int Width => Resolutions[SelectedResolutionIndex].x;
        public static int Height => Resolutions[SelectedResolutionIndex].y;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            loaded = false;
            resolutions = null;
            selectedResolutionIndex = 0;
            fullscreen = false;
            dirty = false;
            pendingApply = false;
            hasObservedScreen = false;
            hasCandidateScreen = false;
            DisplayPreferencesMonitor.ResetInstance();
            Changed = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RestoreAtStartup()
        {
            EnsureLoaded();
            Apply();
            if (!Application.isEditor) DisplayPreferencesMonitor.EnsureCreated();
        }

        private static void EnsureLoaded()
        {
            if (loaded) return;
            Resolution desktop = Screen.currentResolution;
            int monitorWidth = desktop.width >= 640 ? desktop.width : 1920;
            int monitorHeight = desktop.height >= 360 ? desktop.height : 1080;
            Vector2Int initial = new Vector2Int(Screen.width, Screen.height);
            if (!Usable(initial) || initial.x > monitorWidth || initial.y > monitorHeight)
                initial = new Vector2Int(Mathf.Min(1280, monitorWidth), Mathf.Min(720, monitorHeight));

            var sizes = new HashSet<Vector2Int>();
            foreach (Resolution mode in Screen.resolutions)
            {
                var size = new Vector2Int(mode.width, mode.height);
                if (Usable(size)) sizes.Add(size);
            }
            sizes.Add(initial);
            foreach (Vector2Int common in new[] { new Vector2Int(1280, 720), new Vector2Int(1600, 900), new Vector2Int(1920, 1080) })
                if (common.x <= monitorWidth && common.y <= monitorHeight) sizes.Add(common);
            Vector2Int requested = initial;
            if (PlayerPrefs.HasKey(WidthKey) && PlayerPrefs.HasKey(HeightKey))
            {
                var saved = new Vector2Int(PlayerPrefs.GetInt(WidthKey), PlayerPrefs.GetInt(HeightKey));
                if (Usable(saved)) requested = saved;
            }
                                                   
            if (Usable(requested) && requested.x <= monitorWidth && requested.y <= monitorHeight)
                sizes.Add(requested);
            var sorted = new List<Vector2Int>(sizes);
            SortSizes(sorted);
            resolutions = sorted.AsReadOnly();
            double closestDistance = double.PositiveInfinity;
            for (int i = 0; i < sorted.Count; i++)
            {
                double dx = (double)sorted[i].x - requested.x;
                double dy = (double)sorted[i].y - requested.y;
                double distance = dx * dx + dy * dy;
                if (distance >= closestDistance) continue;
                closestDistance = distance;
                selectedResolutionIndex = i;
            }
            fullscreen = PlayerPrefs.GetInt(FullscreenKey, Screen.fullScreen ? 1 : 0) != 0;
            loaded = true;
        }

        private static bool Usable(Vector2Int size) => size.x >= 640 && size.y >= 360;

        public static void SetFullscreen(bool value)
        {
            EnsureLoaded();
            if (fullscreen == value)
            {
                if (!Application.isEditor && !pendingApply && Screen.fullScreen != value) Apply();
                return;
            }
            fullscreen = value;
            Commit();
        }

        public static void SetResolutionIndex(int index)
        {
            EnsureLoaded();
            if (index < 0 || index >= resolutions.Count) return;
            if (selectedResolutionIndex == index)
            {
                if (!Application.isEditor && !pendingApply && (Screen.width != Width || Screen.height != Height)) Apply();
                return;
            }
            selectedResolutionIndex = index;
            Commit();
        }

        private static void Commit()
        {
            Persist();
            Apply();
            Changed?.Invoke();
        }

        private static void Persist()
        {
            PlayerPrefs.SetInt(WidthKey, Width);
            PlayerPrefs.SetInt(HeightKey, Height);
            PlayerPrefs.SetInt(FullscreenKey, fullscreen ? 1 : 0);
            dirty = true;
            Save();
        }

        private static void Apply()
        {
                                                          
            if (Application.isEditor) return;
            if (!hasObservedScreen)
            {
                observedSize = new Vector2Int(Screen.width, Screen.height);
                observedFullscreen = Screen.fullScreen;
                hasObservedScreen = true;
            }
                                                                       
            pendingApply = true;
            requestedFrame = Time.frameCount;
            applyDeadline = Time.realtimeSinceStartup + ApplyTimeoutSeconds;
            hasCandidateScreen = false;
            Screen.SetResolution(Width, Height, fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
        }

        internal static void ObserveNativeScreen()
        {
            if (Application.isEditor) return;
            EnsureLoaded();
            Vector2Int size = new Vector2Int(Screen.width, Screen.height);
            bool actualFullscreen = Screen.fullScreen;
            float now = Time.realtimeSinceStartup;
            if (!hasCandidateScreen || candidateSize != size || candidateFullscreen != actualFullscreen)
            {
                candidateSize = size;
                candidateFullscreen = actualFullscreen;
                candidateSince = now;
                hasCandidateScreen = true;
                return;
            }
                                                               
            if (now - candidateSince < NativeSettleSeconds) return;
            if (pendingApply)
            {
                if (Time.frameCount <= requestedFrame) return;
                if (size.x == Width && size.y == Height && actualFullscreen == fullscreen)
                {
                    pendingApply = false;
                    observedSize = size;
                    observedFullscreen = actualFullscreen;
                    hasObservedScreen = true;
                    return;
                }
                if (now < applyDeadline) return;
                pendingApply = false;
                hasObservedScreen = false;
            }
            if (hasObservedScreen && observedSize == size && observedFullscreen == actualFullscreen) return;
            observedSize = size;
            observedFullscreen = actualFullscreen;
            hasObservedScreen = true;

            bool changed = fullscreen != actualFullscreen;
            fullscreen = actualFullscreen;
            if (Usable(size))
            {
                changed |= Width != size.x || Height != size.y;
                var sizes = new List<Vector2Int>(resolutions);
                int index = sizes.IndexOf(size);
                if (index < 0)
                {
                    sizes.Add(size);
                    SortSizes(sizes);
                    index = sizes.IndexOf(size);
                    resolutions = sizes.AsReadOnly();
                    changed = true;
                }
                selectedResolutionIndex = index;
            }
            if (!changed) return;
                                                              
            Persist();
            Changed?.Invoke();
        }

        private static void SortSizes(List<Vector2Int> sizes) =>
            sizes.Sort((left, right) => left.x != right.x ? left.x.CompareTo(right.x) : left.y.CompareTo(right.y));

        public static void Save()
        {
            if (!dirty) return;
            PlayerPrefs.Save();
            dirty = false;
        }
    }
}
