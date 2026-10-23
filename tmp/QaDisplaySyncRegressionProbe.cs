// Temporary opt-in Windows standalone test. Copy only to the isolated clone's Assets.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using sung_wook;
using Object = UnityEngine.Object;

public sealed class QaDisplaySyncRegressionProbe : MonoBehaviour
{
    [Serializable] private sealed class Check { public string name; public bool passed; public string details; }
    [Serializable] private sealed class Backup {
        public string company, product; public bool hasWidth, hasHeight, hasFullscreen, hasVolume, hasMute, restored;
        public int width, height, fullscreen, mute, expectedWidth, expectedHeight, writerPid; public float volume; public bool expectedFullscreen;
    }
    [Serializable] private sealed class NativeWindow {
        public bool found, visible, iconic, hasCaption; public int left, top, width, height, clientWidth, clientHeight;
        public int monitorLeft, monitorTop, monitorWidth, monitorHeight;
    }
    [Serializable] private sealed class Snapshot {
        public string name, scene, actualMode; public int frame, actualWidth, actualHeight, savedWidth, savedHeight, cachedWidth, cachedHeight;
        public bool actualFullscreen, cachedFullscreen; public int savedFullscreen, modeDropdown, resolutionDropdown, monitorCount;
        public float timeScale; public NativeWindow window;
    }
    [Serializable] private sealed class Report {
        public string status = "running", startedUtc, finishedUtc, mode; public int pid; public bool preferencesRestored;
        public string scope = "Windows standalone, real Screen API external changes, authored Dropdown callbacks, realtime paused updates, native window/client geometry, scene continuity and a fresh executable process. Direct API simulation does not prove physical Alt+Enter input.";
        public List<Check> checks = new List<Check>(); public List<Snapshot> snapshots = new List<Snapshot>(); public List<string> errors = new List<string>();
    }
    private Report report; private Backup backup; private string mode, reportPath, backupPath;
    private bool ending; private float started; private int changes; private readonly List<string> errors = new List<string>();
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot() {
        string mode = Arg("--qa-display-sync="); if (Application.isEditor || mode == null) return;
        var root = new GameObject("Temporary Display Sync Regression"); DontDestroyOnLoad(root);
        var probe = root.AddComponent<QaDisplaySyncRegressionProbe>(); probe.mode = mode; probe.Initialize();
    }
    private static string Arg(string prefix) { foreach (string arg in Environment.GetCommandLineArgs()) if (arg.StartsWith(prefix, StringComparison.Ordinal)) return arg.Substring(prefix.Length); return null; }
    private void Initialize() {
        reportPath = Arg("--qa-report="); backupPath = Arg("--qa-backup="); started = Time.realtimeSinceStartup;
        report = new Report { startedUtc = DateTime.UtcNow.ToString("O"), mode = mode, pid = Process.GetCurrentProcess().Id };
        try {
            Require(Application.platform == RuntimePlatform.WindowsPlayer, "Windows standalone is required.");
            Require(!string.IsNullOrEmpty(reportPath) && Path.IsPathRooted(reportPath) && !string.IsNullOrEmpty(backupPath) && Path.IsPathRooted(backupPath), "Absolute report and backup paths required.");
            Require(mode == "write" || mode == "read" || mode == "restore", "Unknown mode.");
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)); Directory.CreateDirectory(Path.GetDirectoryName(backupPath));
            if (mode == "write") {
                if (File.Exists(backupPath)) Require(JsonUtility.FromJson<Backup>(File.ReadAllText(backupPath)).restored, "Unrestored backup exists.");
                backup = new Backup { company = Application.companyName, product = Application.productName,
                    hasWidth = PlayerPrefs.HasKey(DisplayPreferences.WidthKey), width = PlayerPrefs.GetInt(DisplayPreferences.WidthKey),
                    hasHeight = PlayerPrefs.HasKey(DisplayPreferences.HeightKey), height = PlayerPrefs.GetInt(DisplayPreferences.HeightKey),
                    hasFullscreen = PlayerPrefs.HasKey(DisplayPreferences.FullscreenKey), fullscreen = PlayerPrefs.GetInt(DisplayPreferences.FullscreenKey),
                    hasVolume = PlayerPrefs.HasKey(SfxPreferences.VolumeKey), volume = PlayerPrefs.GetFloat(SfxPreferences.VolumeKey),
                    hasMute = PlayerPrefs.HasKey(SfxPreferences.MuteKey), mute = PlayerPrefs.GetInt(SfxPreferences.MuteKey), writerPid = report.pid };
                WriteBackup();
            } else { backup = JsonUtility.FromJson<Backup>(File.ReadAllText(backupPath)); Require(backup != null && !backup.restored, "Writer backup required."); }
            Require(backup.company == Application.companyName && backup.product == Application.productName, "Preference identity mismatch.");
            Application.runInBackground = true; Application.logMessageReceived += OnLog; DisplayPreferences.Changed += OnChanged;
            StartCoroutine(Guarded(Run()));
        } catch (Exception error) { Finish(error); }
    }
    private IEnumerator Run() {
        if (mode == "restore") { Restore(); Finish(null); yield break; }
        yield return Ready("MainMenu");
        float deadline = Time.realtimeSinceStartup + 12f; while (!UnityEngine.Rendering.SplashScreen.isFinished && Time.realtimeSinceStartup < deadline) yield return null;
        yield return Delay(.5f);
        CheckResult("one-persistent-monitor-at-startup", CountMonitors() == 1, "Exactly one runtime observer is present.");
        CheckSfx("startup-preserves-sfx");
        MainMenuController menu = Find<MainMenuController>(); Click(menu, "settingsButton");
        DisplaySettingsView view = Find<DisplaySettingsView>();
        if (mode == "read") {
            CheckResult("fresh-process-reads-externally-observed-values", report.pid != backup.writerPid && Saved(backup.expectedFullscreen, backup.expectedWidth, backup.expectedHeight), "New process reads the preceding external window change.");
            yield return Await(view, backup.expectedFullscreen, backup.expectedWidth, backup.expectedHeight, "restart-restores-external-window-size");
            Click(menu, "closeSettingsButton"); Click(menu, "startButton"); yield return Ready("Stage_02");
            PauseMenuController pause = Find<PauseMenuController>(); pause.Pause(); Click(pause, "settingsButton");
            view = Find<DisplaySettingsView>(); yield return Await(view, backup.expectedFullscreen, backup.expectedWidth, backup.expectedHeight, "restart-pause-shares-restored-values");
            CheckResult("one-monitor-after-restart-scene-transition", CountMonitors() == 1 && pause.IsPaused && Time.timeScale == 0f, "One observer survives scene loading and pause.");
            Restore(); Finish(null); yield break;
        }
        Require(IndexOf(1280, 720) >= 0 && IndexOf(1600, 900) >= 0, "1280x720 and 1600x900 required on this QA display.");
        DisplayPreferences.SetFullscreen(true);
        Ref<Dropdown>(view, "screenModeDropdown").value = 1;
        Ref<Dropdown>(view, "resolutionDropdown").value = IndexOf(1280, 720);
        yield return Await(view, false, 1280, 720, "authored-windowed-baseline");

        Screen.fullScreenMode = FullScreenMode.FullScreenWindow;
        yield return AwaitExternalMode(view, true, "external-main-fullscreen");
        Screen.fullScreenMode = FullScreenMode.Windowed;
        yield return AwaitExternalMode(view, false, "external-main-windowed");

        Dropdown modeControl = Ref<Dropdown>(view, "screenModeDropdown"), sizes = Ref<Dropdown>(view, "resolutionDropdown");
        int before = changes;
        modeControl.value = 0; sizes.value = IndexOf(1600, 900); modeControl.value = 1;
        sizes.value = IndexOf(1280, 720); modeControl.value = 0; sizes.value = IndexOf(1600, 900);
        CheckResult("rapid-own-requests-preserve-final-selection-immediately", DisplayPreferences.IsFullscreen && DisplayPreferences.Width == 1600 && DisplayPreferences.Height == 900 && changes == before + 6, "Six saved Dropdown callbacks synchronously retain the last requested state.");
        yield return null;
        CheckResult("end-of-frame-does-not-rollback-pending-choice", DisplayPreferences.IsFullscreen && DisplayPreferences.Width == 1600 && DisplayPreferences.Height == 900, "The previous native snapshot does not overwrite the pending latest selection.");
        yield return Await(view, true, 1600, 900, "rapid-own-requests-settle-to-final-choice");
        CheckResult("settled-own-requests-have-no-spurious-change", changes == before + 6, "Observing successful application does not emit extra change events.");

        Click(menu, "closeSettingsButton"); Click(menu, "startButton"); yield return Ready("Stage_02");
        PauseMenuController paused = Find<PauseMenuController>(); paused.Pause(); Click(paused, "settingsButton");
        view = Find<DisplaySettingsView>();
        ElectricBall2D ball = Find<ElectricBall2D>(); Vector2 position = ball.Body.position, velocity = ball.Body.linearVelocity; float fixedTime = Time.fixedTime;
        Screen.fullScreenMode = FullScreenMode.Windowed;
        yield return AwaitExternalMode(view, false, "external-paused-windowed");
        Dropdown expandedSizes = Ref<Dropdown>(view, "resolutionDropdown");
        Vector2Int insertedSize = Vector2Int.zero;
        foreach (Vector2Int candidate in new[] { new Vector2Int(1376, 774), new Vector2Int(1392, 783), new Vector2Int(1408, 792) })
            if (IndexOf(candidate.x, candidate.y) < 0) { insertedSize = candidate; break; }
        Require(insertedSize != Vector2Int.zero, "A new non-preset resolution is required for the expanded list regression.");
        expandedSizes.Show(); yield return Delay(.25f);
        GameObject oldPopup = Popup(expandedSizes);
        CheckResult("resolution-popup-is-open-before-external-list-insertion", oldPopup != null && oldPopup.activeInHierarchy,
            "Real legacy Dropdown.Show creates the saved template popup while gameplay remains paused.");
        Screen.SetResolution(insertedSize.x, insertedSize.y, FullScreenMode.Windowed);
        yield return Await(view, false, insertedSize.x, insertedSize.y, "external-resize-during-expanded-resolution-popup");
        yield return null;
        CheckResult("external-list-insertion-closes-stale-popup-immediately", Popup(expandedSizes) == null && oldPopup == null && expandedSizes.enabled,
            "Changing the options removes the old popup and restores the authored Dropdown enabled state.");
        expandedSizes.Show(); yield return Delay(.25f);
        int targetIndex = IndexOf(1600, 900);
        Toggle freshItem = PopupToggle(expandedSizes, targetIndex);
        CheckResult("reopened-popup-label-matches-new-resolution-index", freshItem.GetComponentInChildren<Text>().text == "1600 × 900",
            "The rebuilt popup's target row displays the resolution represented by its current index.");
        freshItem.isOn = true;
        yield return Await(view, false, 1600, 900, "reopened-popup-selection-applies-displayed-resolution");
        Screen.SetResolution(1440, 810, FullScreenMode.Windowed);
        yield return Await(view, false, 1440, 810, "external-paused-custom-resolution");
        CheckResult("custom-native-size-added-to-resolution-dropdown", IndexOf(1440, 810) >= 0 && Ref<Dropdown>(view, "resolutionDropdown").options[IndexOf(1440, 810)].text == "1440 × 810", "Actual non-preset window dimensions receive an exact selectable label.");
        Screen.fullScreenMode = FullScreenMode.FullScreenWindow;
        yield return AwaitExternalMode(view, true, "external-paused-fullscreen");
        Screen.fullScreenMode = FullScreenMode.Windowed;
        yield return Await(view, false, 1440, 810, "external-paused-windowed-final");
        CheckResult("external-updates-maintain-pause-and-physics", paused.IsPaused && paused.IsSettingsOpen && Time.timeScale == 0f && Time.fixedTime == fixedTime && ball.Body.position == position && ball.Body.linearVelocity == velocity, "Realtime display synchronization neither resumes the game nor advances physics.");
        CheckResult("one-monitor-after-scene-load", CountMonitors() == 1, "Native observation is not duplicated by a second scene.");
        Click(paused, "closeSettingsButton"); Click(paused, "mainMenuButton"); yield return Ready("MainMenu");
        menu = Find<MainMenuController>(); Click(menu, "settingsButton"); view = Find<DisplaySettingsView>();
        yield return Await(view, false, 1440, 810, "return-main-shares-external-pause-change");
        backup.expectedFullscreen = false; backup.expectedWidth = 1440; backup.expectedHeight = 810; WriteBackup();
        CheckSfx("writer-preserves-sfx"); Finish(null);
    }
    private IEnumerator AwaitExternalMode(DisplaySettingsView view, bool fullscreen, string name) {
        float deadline = Time.realtimeSinceStartup + 8f;
        while (Screen.fullScreen != fullscreen && Time.realtimeSinceStartup < deadline) yield return null;
        yield return Delay(.4f);
        yield return Await(view, fullscreen, Screen.width, Screen.height, name);
    }
    private IEnumerator Await(DisplaySettingsView view, bool fullscreen, int width, int height, string name) {
        float deadline = Time.realtimeSinceStartup + 8f; int stable = 0;
        while (Time.realtimeSinceStartup < deadline) {
            bool match = Screen.fullScreen == fullscreen && Screen.width == width && Screen.height == height &&
                DisplayPreferences.IsFullscreen == fullscreen && DisplayPreferences.Width == width && DisplayPreferences.Height == height &&
                Saved(fullscreen, width, height) && Ref<Dropdown>(view, "screenModeDropdown").value == (fullscreen ? 0 : 1) &&
                Ref<Dropdown>(view, "resolutionDropdown").value == IndexOf(width, height) && NativeMatches(ObserveWindow(), fullscreen, width, height);
            stable = match ? stable + 1 : 0; if (stable >= 12) break; yield return null;
        }
        Snapshot snapshot = Observe(name, view); report.snapshots.Add(snapshot);
        CheckResult(name + "-actual-cache-ui-and-durable-keys", stable >= 12, JsonUtility.ToJson(snapshot));
        CheckResult(name + "-native-window-geometry", NativeMatches(snapshot.window, fullscreen, width, height), JsonUtility.ToJson(snapshot.window));
    }
    private Snapshot Observe(string name, DisplaySettingsView view) => new Snapshot { name = name, scene = SceneManager.GetActiveScene().path, frame = Time.frameCount, timeScale = Time.timeScale,
        actualWidth = Screen.width, actualHeight = Screen.height, actualFullscreen = Screen.fullScreen, actualMode = Screen.fullScreenMode.ToString(),
        cachedWidth = DisplayPreferences.Width, cachedHeight = DisplayPreferences.Height, cachedFullscreen = DisplayPreferences.IsFullscreen,
        savedWidth = PlayerPrefs.GetInt(DisplayPreferences.WidthKey, -1), savedHeight = PlayerPrefs.GetInt(DisplayPreferences.HeightKey, -1), savedFullscreen = PlayerPrefs.GetInt(DisplayPreferences.FullscreenKey, -1),
        modeDropdown = Ref<Dropdown>(view, "screenModeDropdown").value, resolutionDropdown = Ref<Dropdown>(view, "resolutionDropdown").value,
        monitorCount = CountMonitors(), window = ObserveWindow() };
    private static bool Saved(bool fullscreen, int width, int height) => PlayerPrefs.GetInt(DisplayPreferences.FullscreenKey, -1) == (fullscreen ? 1 : 0) && PlayerPrefs.GetInt(DisplayPreferences.WidthKey, -1) == width && PlayerPrefs.GetInt(DisplayPreferences.HeightKey, -1) == height;
    private static int IndexOf(int width, int height) { for (int i = 0; i < DisplayPreferences.Resolutions.Count; i++) if (DisplayPreferences.Resolutions[i] == new Vector2Int(width, height)) return i; return -1; }
    private static GameObject Popup(Dropdown dropdown) => typeof(Dropdown).GetField("m_Dropdown", Fields).GetValue(dropdown) as GameObject;
    private static Toggle PopupToggle(Dropdown dropdown, int index) {
        var items = typeof(Dropdown).GetField("m_Items", Fields).GetValue(dropdown) as IList;
        Require(items != null && index >= 0 && index < items.Count, "The expanded Dropdown has no target option.");
        object item = items[index];
        Toggle toggle = item.GetType().GetProperty("toggle", Fields).GetValue(item) as Toggle;
        Require(toggle != null && toggle.gameObject.activeInHierarchy, "The rebuilt option Toggle is missing or inactive.");
        return toggle;
    }
    private static int CountMonitors() => Object.FindObjectsByType<DisplayPreferencesMonitor>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
    private static T Find<T>() where T : Object => Object.FindFirstObjectByType<T>();
    private static T Ref<T>(object target, string field) where T : class { T value = target.GetType().GetField(field, Fields).GetValue(target) as T; Require(value != null, "Missing " + field); return value; }
    private static void Click(object target, string field) => Ref<Button>(target, field).onClick.Invoke();
    private static IEnumerator Delay(float seconds) { float deadline = Time.realtimeSinceStartup + seconds; while (Time.realtimeSinceStartup < deadline) yield return null; }
    private static IEnumerator Ready(string scene) { float deadline = Time.realtimeSinceStartup + 25f; while ((SceneManager.GetActiveScene().name != scene || SceneTransitionController.IsBusy || !SceneTransitionController.IsAvailable) && Time.realtimeSinceStartup < deadline) yield return null; Require(SceneManager.GetActiveScene().name == scene && !SceneTransitionController.IsBusy, "Scene failed: " + scene); yield return null; }
    private void OnChanged() => changes++;
    private void CheckSfx(string name) => CheckResult(name, PlayerPrefs.HasKey(SfxPreferences.VolumeKey) == backup.hasVolume && PlayerPrefs.HasKey(SfxPreferences.MuteKey) == backup.hasMute && (!backup.hasVolume || PlayerPrefs.GetFloat(SfxPreferences.VolumeKey) == backup.volume) && (!backup.hasMute || PlayerPrefs.GetInt(SfxPreferences.MuteKey) == backup.mute), "Unrelated SFX preference presence/values remain exact.");
    private void Restore() {
        DisplayPreferences.Save(); RestoreInt(DisplayPreferences.WidthKey, backup.hasWidth, backup.width); RestoreInt(DisplayPreferences.HeightKey, backup.hasHeight, backup.height); RestoreInt(DisplayPreferences.FullscreenKey, backup.hasFullscreen, backup.fullscreen); PlayerPrefs.Save();
        CheckSfx("restore-preserves-sfx"); backup.restored = true; report.preferencesRestored = true; WriteBackup();
    }
    private static void RestoreInt(string key, bool present, int value) { if (present) PlayerPrefs.SetInt(key, value); else PlayerPrefs.DeleteKey(key); }
    private void WriteBackup() => File.WriteAllText(backupPath, JsonUtility.ToJson(backup, true));
    private void OnLog(string message, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message + "\n" + stack); }
    private void CheckResult(string name, bool passed, string details) { report.checks.Add(new Check { name = name, passed = passed, details = details }); Flush(); Require(passed, name + ": " + details); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private void Flush() { if (!string.IsNullOrEmpty(reportPath)) File.WriteAllText(reportPath, JsonUtility.ToJson(report, true)); }
    private IEnumerator Guarded(IEnumerator sequence) { var stack = new Stack<IEnumerator>(); stack.Push(sequence); while (stack.Count > 0 && !ending) { bool next = false; object item = null; Exception error = null; try { next = stack.Peek().MoveNext(); if (next) item = stack.Peek().Current; } catch (Exception caught) { error = caught; } if (error != null) { Finish(error); yield break; } if (!next) stack.Pop(); else if (item is IEnumerator child) stack.Push(child); else yield return item; } }
    private void Update() { if (!ending && report != null && Time.realtimeSinceStartup - started > 150f) Finish(new TimeoutException("Display sync regression timed out.")); }
    private void Finish(Exception error) {
        if (ending) return; ending = true;
        if (error != null) { report.errors.Add(error.ToString()); try { if (backup != null && !backup.restored) Restore(); } catch (Exception restoreError) { report.errors.Add(restoreError.ToString()); } }
        report.errors.AddRange(errors); bool failed = report.errors.Count > 0 || report.checks.Exists(check => !check.passed);
        report.status = failed ? "failed" : "passed"; report.finishedUtc = DateTime.UtcNow.ToString("O"); Flush();
        DisplayPreferences.Changed -= OnChanged; Application.logMessageReceived -= OnLog; Application.Quit(failed ? 1 : 0);
    }

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int left, top, right, bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int size; public Rect monitor; public Rect work; public uint flags; }
    private delegate bool EnumWindow(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindow callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, System.Text.StringBuilder name, int max);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    private static NativeWindow ObserveWindow() {
        var result = new NativeWindow(); uint wanted = (uint)Process.GetCurrentProcess().Id;
        EnumWindows((window, parameter) => {
            GetWindowThreadProcessId(window, out uint pid); if (pid != wanted) return true;
            var className = new System.Text.StringBuilder(256); GetClassName(window, className, className.Capacity); if (!className.ToString().StartsWith("UnityWndClass", StringComparison.Ordinal)) return true;
            result.found = true; GetWindowRect(window, out Rect outer); GetClientRect(window, out Rect client);
            result.left = outer.left; result.top = outer.top; result.width = outer.right - outer.left; result.height = outer.bottom - outer.top;
            result.clientWidth = client.right - client.left; result.clientHeight = client.bottom - client.top;
            result.hasCaption = (unchecked((uint)GetWindowLong(window, -16)) & 0x00C00000u) == 0x00C00000u;
            result.visible = IsWindowVisible(window); result.iconic = IsIconic(window);
            var monitor = new MonitorInfo { size = Marshal.SizeOf(typeof(MonitorInfo)) };
            if (GetMonitorInfo(MonitorFromWindow(window, 2), ref monitor)) { result.monitorLeft = monitor.monitor.left; result.monitorTop = monitor.monitor.top; result.monitorWidth = monitor.monitor.right - monitor.monitor.left; result.monitorHeight = monitor.monitor.bottom - monitor.monitor.top; }
            return false;
        }, IntPtr.Zero); return result;
    }
    private static bool NativeMatches(NativeWindow window, bool fullscreen, int width, int height) {
        if (!window.found || !window.visible || window.iconic) return false;
        if (!fullscreen) return window.hasCaption && window.clientWidth == width && window.clientHeight == height;
        return !window.hasCaption && window.clientWidth == window.monitorWidth && window.clientHeight == window.monitorHeight && window.left == window.monitorLeft && window.top == window.monitorTop && window.width == window.monitorWidth && window.height == window.monitorHeight;
    }
}
