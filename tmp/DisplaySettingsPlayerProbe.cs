// Temporary standalone-player probe. Copy only to the isolated build project's Assets.
// No Editor execution and no activity unless --display-probe=write|read|window-read|restore|diagnose is present.
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

public sealed class DisplaySettingsPlayerProbe : MonoBehaviour
{
    [Serializable] private sealed class Check { public string name; public bool passed; public string details; }
    [Serializable] private sealed class Backup
    {
        public bool hasWidth; public int width; public bool hasHeight; public int height;
        public bool hasFullscreen; public int fullscreen;
        public bool hasSfxVolume; public float sfxVolume; public bool hasSfxMute; public int sfxMute;
        public string company; public string product; public string createdUtc; public bool restored;
        public int expectedWidth; public int expectedHeight; public bool expectedFullscreen;
        public int aWidth; public int aHeight; public int bWidth; public int bHeight;
        public int writerProcessId; public int readerProcessId;
    }
    [Serializable] private sealed class NativeWindow
    {
        public bool found; public long handle; public string className; public string title;
        public int left; public int top; public int width; public int height;
        public int clientWidth; public int clientHeight;
        public int monitorLeft; public int monitorTop; public int monitorWidth; public int monitorHeight;
        public uint style; public uint extendedStyle; public uint dpi;
        public bool hasCaption; public bool popup; public bool visible; public bool iconic;
    }
    [Serializable] private sealed class Observation
    {
        public string name; public int frame; public float elapsed; public string scene; public float timeScale;
        public int requestedWidth; public int requestedHeight; public bool requestedFullscreen;
        public int actualWidth; public int actualHeight; public bool actualFullscreen; public string actualMode;
        public int desktopWidth; public int desktopHeight; public bool focused;
        public bool splashFinished; public int preferenceWidth; public int preferenceHeight; public bool preferenceFullscreen;
        public bool hasRawWidth; public int rawWidth; public bool hasRawHeight; public int rawHeight;
        public bool hasRawFullscreen; public int rawFullscreen;
        public string[] startupObjects;
        public NativeWindow window; public string screenshot;
    }
    [Serializable] private sealed class Report
    {
        public string status = "running"; public string mode; public string startedUtc;
        public int processId; public string unity; public string executable; public string graphics;
        public string company; public string product;
        public List<string> supportedResolutions = new List<string>();
        public List<Check> checks = new List<Check>();
        public List<Observation> observations = new List<Observation>();
        public List<string> errors = new List<string>();
        public string scope = "Windows standalone player: actual saved Dropdown events, native client/window rectangles and styles, Unity rendering size/mode, paused changes, asynchronous menu/stage transitions and separate-process persistence. Probe is absent from production source assets.";
    }

    private string mode, reportPath, backupPath;
    private Report report;
    private Backup backup;
    private float started;
    private bool ending;
    private int changes;
    private readonly List<string> runtimeErrors = new List<string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (Application.isEditor) return;
        string selectedMode = Argument("--display-probe=");
        if (string.IsNullOrEmpty(selectedMode)) return;
        var root = new GameObject("Temporary Standalone Display Validation");
        DontDestroyOnLoad(root);
        var probe = root.AddComponent<DisplaySettingsPlayerProbe>();
        probe.mode = selectedMode;
        probe.reportPath = Argument("--display-report=");
        probe.backupPath = Argument("--display-backup=");
        probe.Initialize();
    }

    private static string Argument(string prefix)
    {
        foreach (string value in Environment.GetCommandLineArgs())
            if (value.StartsWith(prefix, StringComparison.Ordinal)) return value.Substring(prefix.Length);
        return null;
    }

    private void Initialize()
    {
        Application.runInBackground = true;
        started = Time.realtimeSinceStartup;
        report = new Report {
            mode = mode, startedUtc = DateTime.UtcNow.ToString("O"), processId = Process.GetCurrentProcess().Id,
            unity = Application.unityVersion, executable = Environment.GetCommandLineArgs()[0],
            graphics = SystemInfo.graphicsDeviceName + " / " + SystemInfo.graphicsDeviceType,
            company = Application.companyName, product = Application.productName
        };
        Application.logMessageReceived += OnLog;
        DisplayPreferences.Changed += OnChanged;
        try
        {
            Require(Application.platform == RuntimePlatform.WindowsPlayer, "This probe requires the Windows standalone player.");
            Require(!string.IsNullOrEmpty(reportPath) && Path.IsPathRooted(reportPath), "An absolute --display-report path is required.");
            Require(!string.IsNullOrEmpty(backupPath) && Path.IsPathRooted(backupPath), "An absolute --display-backup path is required.");
            Require(mode == "write" || mode == "read" || mode == "window-read" || mode == "restore" || mode == "diagnose", "Unknown probe mode: " + mode);
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
            foreach (Vector2Int size in DisplayPreferences.Resolutions) report.supportedResolutions.Add(size.x + "x" + size.y);
            if (mode == "write") MakeBackup();
            else
            {
                Require(File.Exists(backupPath), "A writer backup is required.");
                backup = JsonUtility.FromJson<Backup>(File.ReadAllText(backupPath));
                Require(backup != null && backup.company == Application.companyName && backup.product == Application.productName, "PlayerPrefs identity does not match backup.");
                Require(!backup.restored || mode == "restore", "The test preferences were already restored.");
            }
            if (mode == "diagnose") RecordDiagnostic("diagnostic-immediate-boot-frame", backup.expectedFullscreen, backup.expectedWidth, backup.expectedHeight);
            Flush();
            StartCoroutine(Guarded(Run()));
        }
        catch (Exception exception) { Fail(exception); }
    }

    private void MakeBackup()
    {
        if (File.Exists(backupPath))
        {
            Backup previous = JsonUtility.FromJson<Backup>(File.ReadAllText(backupPath));
            Require(previous == null || previous.restored, "An unrestored standalone preference backup exists. Run --display-probe=restore or restore it before replacing.");
        }
        backup = new Backup {
            hasWidth = PlayerPrefs.HasKey(DisplayPreferences.WidthKey), width = PlayerPrefs.GetInt(DisplayPreferences.WidthKey, 0),
            hasHeight = PlayerPrefs.HasKey(DisplayPreferences.HeightKey), height = PlayerPrefs.GetInt(DisplayPreferences.HeightKey, 0),
            hasFullscreen = PlayerPrefs.HasKey(DisplayPreferences.FullscreenKey), fullscreen = PlayerPrefs.GetInt(DisplayPreferences.FullscreenKey, 0),
            hasSfxVolume = PlayerPrefs.HasKey(SfxPreferences.VolumeKey), sfxVolume = PlayerPrefs.GetFloat(SfxPreferences.VolumeKey, 1f),
            hasSfxMute = PlayerPrefs.HasKey(SfxPreferences.MuteKey), sfxMute = PlayerPrefs.GetInt(SfxPreferences.MuteKey, 0),
            company = Application.companyName, product = Application.productName, createdUtc = report.startedUtc,
            writerProcessId = report.processId
        };
        int a = IndexOf(1280, 720);
        if (a < 0) a = 0;
        int b = IndexOf(1600, 900);
        if (b < 0 || b == a) b = DisplayPreferences.Resolutions.Count - 1;
        Require(a != b, "At least two distinct supported sizes are required.");
        Vector2Int sizeA = DisplayPreferences.Resolutions[a], sizeB = DisplayPreferences.Resolutions[b];
        backup.aWidth = sizeA.x; backup.aHeight = sizeA.y; backup.bWidth = sizeB.x; backup.bHeight = sizeB.y;
        WriteBackup();
        CheckResult("backup-preserves-three-display-keys-and-observes-sfx", true, "No preference reset or DeleteAll. OS Screenmanager registry values must be backed up/restored by the parent process after the last player exits.");
    }

    private IEnumerator Run()
    {
        if (mode == "restore")
        {
            RestoreOriginal();
            Complete();
            yield break;
        }
        if (mode == "diagnose")
        {
            for (int sample = 0; sample < 5; sample++)
            {
                RecordDiagnostic("diagnostic-before-menu-frame-" + sample, backup.expectedFullscreen, backup.expectedWidth, backup.expectedHeight);
                yield return null;
            }
        }
        yield return WaitForMenu();
        CheckResult("standalone-graphics-and-player-window-available", !Application.isEditor && Screen.width > 0 && ObserveWindow().found,
            "The probe runs in a graphical WindowsPlayer and identifies its UnityWndClass window by the current process ID.");
        CheckSfx("startup-preserves-sfx");
        if (mode == "diagnose") yield return DiagnoseFlow();
        else if (mode == "write") yield return WriteFlow();
        else yield return ReadFlow();
        CheckSfx("completion-preserves-sfx");
        CheckResult("no-runtime-errors", runtimeErrors.Count == 0, string.Join("\n", runtimeErrors));
        Complete();
    }

    private IEnumerator DiagnoseFlow()
    {
        bool expectedFullscreen = backup.expectedFullscreen;
        int width = backup.expectedWidth, height = backup.expectedHeight;
        Require(width > 0 && height > 0, "Diagnostic mode requires a saved expected writer selection.");
        RecordDiagnostic("diagnostic-menu-ready-before-splash-wait", expectedFullscreen, width, height);
        float splashDeadline = Time.realtimeSinceStartup + 10f;
        while (!UnityEngine.Rendering.SplashScreen.isFinished && Time.realtimeSinceStartup < splashDeadline) yield return null;
        RecordDiagnostic("diagnostic-after-splash-wait-before-direct-apply", expectedFullscreen, width, height);
        Screen.SetResolution(width, height, FullScreenMode.FullScreenWindow);
        yield return Delay(3f);
        RecordDiagnostic("diagnostic-direct-fullscreen-apply-after-three-seconds", true, width, height);
        yield return Capture("diagnostic-direct-fullscreen");
        Screen.SetResolution(width, height, FullScreenMode.Windowed);
        yield return Delay(1.2f);
        RecordDiagnostic("diagnostic-forced-windowed-after-one-second", false, width, height);
        Screen.SetResolution(width, height, FullScreenMode.FullScreenWindow);
        yield return Delay(1.2f);
        RecordDiagnostic("diagnostic-windowed-then-fullscreen-after-one-second", true, width, height);
        yield return Capture("diagnostic-windowed-then-fullscreen");
        CheckResult("diagnostic-preserves-three-display-keys", SavedMatches(expectedFullscreen, width, height),
            "Diagnostic calls only Screen.SetResolution; no display preference setters, dropdown callbacks, restore or expected selection mutation.");
    }

    private void RecordDiagnostic(string name, bool fullscreen, int width, int height)
    {
        Observation state = Observe(name, fullscreen, width, height);
        report.observations.Add(state);
        CheckResult(name + "-observed-without-requiring-match", true, JsonUtility.ToJson(state));
    }

    private IEnumerator WriteFlow()
    {
        MainMenuController menu = FindActive<MainMenuController>();
        Invoke(menu, "settingsButton");
        DisplaySettingsView view = ActiveSettings();
        CheckResult("main-menu-opens-authored-display-settings", menu.IsSettingsOpen && view.isActiveAndEnabled, "Opening uses the saved settings button event.");
        yield return SetUsingDropdowns(view, false, backup.aWidth, backup.aHeight, "main-windowed-A");
        yield return SetUsingDropdowns(view, false, backup.bWidth, backup.bHeight, "main-windowed-B");
        yield return Capture("main-windowed-settings");
        if (IndexOf(1920, 1080) >= 0 && Screen.currentResolution.width >= 1920 && Screen.currentResolution.height >= 1080)
            yield return SetUsingDropdowns(view, false, 1920, 1080, "main-windowed-1920x1080-workarea-edge");
        yield return SetUsingDropdowns(view, true, backup.bWidth, backup.bHeight, "main-fullscreen-B");
        yield return SetUsingDropdowns(view, true, backup.aWidth, backup.aHeight, "main-fullscreen-A");
        yield return Capture("main-fullscreen-settings");
        Invoke(menu, "closeSettingsButton");
        Invoke(menu, "startButton");
        yield return WaitForStage();
        PauseMenuController pause = FindActive<PauseMenuController>();
        pause.Pause();
        Invoke(pause, "settingsButton");
        view = ActiveSettings();
        CheckResult("pause-settings-share-main-selection", pause.IsPaused && pause.IsSettingsOpen && Mathf.Approximately(Time.timeScale, 0f)
            && UiMatches(view, true, backup.aWidth, backup.aHeight), "The real stage and pause settings reflect the main-menu selection.");
        yield return SetUsingDropdowns(view, false, backup.bWidth, backup.bHeight, "paused-windowed-B");
        CheckResult("native-window-change-maintains-pause", pause.IsPaused && pause.IsSettingsOpen && Mathf.Approximately(Time.timeScale, 0f), "The native change completed while gameplay remained paused.");
        yield return Capture("pause-windowed-settings");
        yield return SetUsingDropdowns(view, false, backup.aWidth, backup.aHeight, "paused-windowed-A");
        yield return SetUsingDropdowns(view, true, backup.bWidth, backup.bHeight, "paused-fullscreen-B");
        CheckResult("native-fullscreen-change-maintains-pause", pause.IsPaused && Mathf.Approximately(Time.timeScale, 0f), "Fullscreen and size updates use realtime/end-of-frame progress despite timeScale zero.");
        yield return Capture("pause-fullscreen-settings");
        Invoke(pause, "closeSettingsButton");
        Invoke(pause, "mainMenuButton");
        yield return WaitForMenu();
        menu = FindActive<MainMenuController>();
        Invoke(menu, "settingsButton");
        CheckResult("return-to-main-shares-pause-selection", UiMatches(ActiveSettings(), true, backup.bWidth, backup.bHeight), "The asynchronous fade returns to the same shared choice.");
        yield return AwaitApplied(true, backup.bWidth, backup.bHeight, "writer-final-fullscreen");
        SetExpected(true, backup.bWidth, backup.bHeight);
        CheckResult("writer-persists-fullscreen-choice-before-exit", SavedMatches(backup.expectedFullscreen, backup.expectedWidth, backup.expectedHeight), "A different executable process must now read and apply these exact values.");
    }

    private IEnumerator ReadFlow()
    {
        CheckResult("different-process-reloads-durable-keys", report.processId != backup.writerProcessId
            && (mode != "window-read" || report.processId != backup.readerProcessId)
            && SavedMatches(backup.expectedFullscreen, backup.expectedWidth, backup.expectedHeight),
            "Stored keys read by a separately launched executable match the preceding run.");
        yield return AwaitApplied(backup.expectedFullscreen, backup.expectedWidth, backup.expectedHeight, "startup-restores-" + mode);
        MainMenuController menu = FindActive<MainMenuController>();
        Invoke(menu, "settingsButton");
        DisplaySettingsView view = ActiveSettings();
        CheckResult("restart-ui-agrees-with-native-state", UiMatches(view, backup.expectedFullscreen, backup.expectedWidth, backup.expectedHeight), "Saved main-menu controls agree with restored actual rendering mode and size.");
        yield return Capture(mode + "-restored-settings");
        if (mode == "read")
        {
            CheckResult("reader-restores-fullscreen-choice", backup.expectedFullscreen, "The second process starts with the writer's borderless fullscreen selection.");
            yield return SetUsingDropdowns(view, false, backup.aWidth, backup.aHeight, "reader-saves-windowed-A");
            backup.readerProcessId = report.processId;
            SetExpected(false, backup.aWidth, backup.aHeight);
            CheckResult("reader-persists-windowed-choice-before-exit", SavedMatches(false, backup.aWidth, backup.aHeight), "The third process must restore the windowed selection.");
        }
        else
        {
            CheckResult("third-process-restores-windowed-choice", !backup.expectedFullscreen, "The third process starts in the prior run's windowed size.");
            Invoke(menu, "closeSettingsButton");
            Invoke(menu, "startButton");
            yield return WaitForStage();
            PauseMenuController pause = FindActive<PauseMenuController>();
            pause.Pause(); Invoke(pause, "settingsButton");
            CheckResult("restart-pause-ui-shares-persisted-windowed-choice", pause.IsPaused && Mathf.Approximately(Time.timeScale, 0f)
                && UiMatches(ActiveSettings(), false, backup.expectedWidth, backup.expectedHeight), "Shared settings survive an executable restart and actual stage loading.");
            Invoke(pause, "closeSettingsButton");
            RestoreOriginal();
        }
    }

    private IEnumerator SetUsingDropdowns(DisplaySettingsView view, bool fullscreen, int width, int height, string name)
    {
        Dropdown modeControl = Reference<Dropdown>(view, "screenModeDropdown");
        Dropdown sizeControl = Reference<Dropdown>(view, "resolutionDropdown");
        int resolutionIndex = IndexOf(width, height);
        Require(resolutionIndex >= 0, name + " resolution is absent.");
        int modeIndex = fullscreen ? 0 : 1;
        bool modeChanged = modeControl.value != modeIndex;
        int before = changes;
        modeControl.value = modeIndex;
        yield return Delay(0.45f);
        bool sizeChanged = sizeControl.value != resolutionIndex;
        sizeControl.value = resolutionIndex;
        CheckResult(name + "-real-dropdown-callbacks", changes - before == (modeChanged ? 1 : 0) + (sizeChanged ? 1 : 0), "Saved Dropdown.value invokes the actual DisplaySettingsView listeners exactly once per changed field.");
        yield return AwaitApplied(fullscreen, width, height, name);
        CheckResult(name + "-ui-and-prefs-agree", UiMatches(view, fullscreen, width, height) && SavedMatches(fullscreen, width, height), "UI, shared preferences and all three durable PlayerPrefs values agree with the requested size/mode.");
        CheckSfx(name + "-preserves-sfx");
    }

    private IEnumerator AwaitApplied(bool fullscreen, int width, int height, string name)
    {
        float deadline = Time.realtimeSinceStartup + 10f;
        int consecutive = 0;
        while (Time.realtimeSinceStartup < deadline)
        {
            NativeWindow window = ObserveWindow();
            bool unityMatches = Screen.width == width && Screen.height == height && Screen.fullScreen == fullscreen
                && Screen.fullScreenMode == (fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
            bool nativeMatches = NativeMatches(window, fullscreen, width, height);
            consecutive = unityMatches && nativeMatches ? consecutive + 1 : 0;
            if (consecutive >= 8) break;
            yield return null;
        }
        Observation observation = Observe(name, fullscreen, width, height);
        report.observations.Add(observation);
        bool applied = observation.actualWidth == width && observation.actualHeight == height && observation.actualFullscreen == fullscreen
            && observation.actualMode == (fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed).ToString();
        CheckResult(name + "-actual-unity-size-and-mode", applied, JsonUtility.ToJson(observation));
        CheckResult(name + "-actual-native-window-and-client", NativeMatches(observation.window, fullscreen, width, height),
            "Windowed: decorated window, client area equals the requested render size. Borderless: no caption, client and window cover the current monitor; render size may be smaller. " + JsonUtility.ToJson(observation.window));
    }

    private static bool NativeMatches(NativeWindow window, bool fullscreen, int width, int height)
    {
        if (!window.found || window.iconic || window.clientWidth <= 0 || window.clientHeight <= 0) return false;
        if (!fullscreen) return window.hasCaption && window.clientWidth == width && window.clientHeight == height;
        return !window.hasCaption && window.clientWidth == window.monitorWidth && window.clientHeight == window.monitorHeight
            && window.left == window.monitorLeft && window.top == window.monitorTop
            && window.width == window.monitorWidth && window.height == window.monitorHeight;
    }

    private IEnumerator Capture(string name)
    {
        yield return Delay(0.3f);
        yield return new WaitForEndOfFrame();
        string path = Path.Combine(Path.GetDirectoryName(reportPath), Path.GetFileNameWithoutExtension(reportPath) + "-" + name + ".png");
        Texture2D image = ScreenCapture.CaptureScreenshotAsTexture();
        Require(image != null && image.width == Screen.width && image.height == Screen.height, "Screenshot dimensions must match the real player framebuffer.");
        File.WriteAllBytes(path, image.EncodeToPNG());
        Object.Destroy(image);
        Observation observation = Observe(name, DisplayPreferences.IsFullscreen, DisplayPreferences.Width, DisplayPreferences.Height);
        observation.screenshot = path;
        report.observations.Add(observation);
        CheckResult(name + "-captures-actual-player-frame", File.Exists(path), path);
    }

    private Observation Observe(string name, bool fullscreen, int width, int height) => new Observation {
        name = name, frame = Time.frameCount, elapsed = Time.realtimeSinceStartup - started,
        scene = SceneManager.GetActiveScene().path, timeScale = Time.timeScale,
        requestedWidth = width, requestedHeight = height, requestedFullscreen = fullscreen,
        actualWidth = Screen.width, actualHeight = Screen.height, actualFullscreen = Screen.fullScreen,
        actualMode = Screen.fullScreenMode.ToString(), desktopWidth = Screen.currentResolution.width,
        desktopHeight = Screen.currentResolution.height, focused = Application.isFocused, window = ObserveWindow(),
        splashFinished = UnityEngine.Rendering.SplashScreen.isFinished,
        preferenceWidth = DisplayPreferences.Width, preferenceHeight = DisplayPreferences.Height, preferenceFullscreen = DisplayPreferences.IsFullscreen,
        hasRawWidth = PlayerPrefs.HasKey(DisplayPreferences.WidthKey), rawWidth = PlayerPrefs.GetInt(DisplayPreferences.WidthKey, -1),
        hasRawHeight = PlayerPrefs.HasKey(DisplayPreferences.HeightKey), rawHeight = PlayerPrefs.GetInt(DisplayPreferences.HeightKey, -1),
        hasRawFullscreen = PlayerPrefs.HasKey(DisplayPreferences.FullscreenKey), rawFullscreen = PlayerPrefs.GetInt(DisplayPreferences.FullscreenKey, -1),
        startupObjects = StartupObjectDescriptions()
    };

    private static string[] StartupObjectDescriptions()
    {
        var descriptions = new List<string>();
        foreach (MonoBehaviour component in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (component == null) continue;
            string type = component.GetType().FullName;
            if (type.IndexOf("Display", StringComparison.OrdinalIgnoreCase) < 0 && component.name.IndexOf("Display Settings Startup", StringComparison.OrdinalIgnoreCase) < 0) continue;
            descriptions.Add(type + " | " + component.name + " | active=" + component.gameObject.activeInHierarchy + " | enabled=" + component.enabled);
        }
        return descriptions.ToArray();
    }

    private IEnumerator WaitForMenu()
    {
        float deadline = Time.realtimeSinceStartup + 25f;
        while ((FindActive<MainMenuController>() == null || SceneTransitionController.IsBusy || !SceneTransitionController.IsAvailable) && Time.realtimeSinceStartup < deadline) yield return null;
        Require(FindActive<MainMenuController>() != null && !SceneTransitionController.IsBusy, "Main menu async load/fade did not complete.");
        yield return Delay(0.3f);
    }
    private IEnumerator WaitForStage()
    {
        float deadline = Time.realtimeSinceStartup + 25f;
        while ((FindActive<PauseMenuController>() == null || SceneTransitionController.IsBusy) && Time.realtimeSinceStartup < deadline) yield return null;
        Require(FindActive<PauseMenuController>() != null && !SceneTransitionController.IsBusy, "Stage async load/fade did not complete.");
        CheckResult("actual-stage-load-completes-" + report.observations.Count, Mathf.Approximately(Time.timeScale, 1f), SceneManager.GetActiveScene().path);
        yield return null;
    }
    private static IEnumerator Delay(float seconds)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < deadline) yield return null;
    }

    private void RestoreOriginal()
    {
        RestoreInt(DisplayPreferences.WidthKey, backup.hasWidth, backup.width);
        RestoreInt(DisplayPreferences.HeightKey, backup.hasHeight, backup.height);
        RestoreInt(DisplayPreferences.FullscreenKey, backup.hasFullscreen, backup.fullscreen);
        PlayerPrefs.Save();
        CheckResult("restore-original-display-key-presence-and-values", OriginalInt(DisplayPreferences.WidthKey, backup.hasWidth, backup.width)
            && OriginalInt(DisplayPreferences.HeightKey, backup.hasHeight, backup.height)
            && OriginalInt(DisplayPreferences.FullscreenKey, backup.hasFullscreen, backup.fullscreen), "Only the three owned display keys are restored; parent restores Unity's OS Screenmanager keys after all players exit.");
        CheckSfx("restore-preserves-sfx");
        backup.restored = true;
        WriteBackup();
    }
    private static void RestoreInt(string key, bool present, int value) { if (present) PlayerPrefs.SetInt(key, value); else PlayerPrefs.DeleteKey(key); }
    private static bool OriginalInt(string key, bool present, int value) => PlayerPrefs.HasKey(key) == present && (!present || PlayerPrefs.GetInt(key) == value);
    private bool SavedMatches(bool fullscreen, int width, int height) => PlayerPrefs.HasKey(DisplayPreferences.WidthKey)
        && PlayerPrefs.HasKey(DisplayPreferences.HeightKey) && PlayerPrefs.HasKey(DisplayPreferences.FullscreenKey)
        && PlayerPrefs.GetInt(DisplayPreferences.WidthKey) == width && PlayerPrefs.GetInt(DisplayPreferences.HeightKey) == height
        && PlayerPrefs.GetInt(DisplayPreferences.FullscreenKey) == (fullscreen ? 1 : 0);
    private void SetExpected(bool fullscreen, int width, int height)
    {
        backup.expectedFullscreen = fullscreen; backup.expectedWidth = width; backup.expectedHeight = height;
        DisplayPreferences.Save(); PlayerPrefs.Save(); WriteBackup();
    }
    private void WriteBackup() { Directory.CreateDirectory(Path.GetDirectoryName(backupPath)); File.WriteAllText(backupPath, JsonUtility.ToJson(backup, true)); }
    private void CheckSfx(string name) => CheckResult(name, PlayerPrefs.HasKey(SfxPreferences.VolumeKey) == backup.hasSfxVolume
        && PlayerPrefs.HasKey(SfxPreferences.MuteKey) == backup.hasSfxMute
        && (!backup.hasSfxVolume || PlayerPrefs.GetFloat(SfxPreferences.VolumeKey) == backup.sfxVolume)
        && (!backup.hasSfxMute || PlayerPrefs.GetInt(SfxPreferences.MuteKey) == backup.sfxMute), "SFX preference presence and exact values remain unchanged.");
    private void OnChanged() => changes++;

    private static T FindActive<T>() where T : Object => Object.FindFirstObjectByType<T>(FindObjectsInactive.Exclude);
    private static DisplaySettingsView ActiveSettings()
    {
        DisplaySettingsView view = FindActive<DisplaySettingsView>();
        Require(view != null && view.isActiveAndEnabled, "No active authored DisplaySettingsView."); return view;
    }
    private static T Reference<T>(object target, string name) where T : class
    {
        FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        T value = field == null ? null : field.GetValue(target) as T;
        Require(value != null, target.GetType().Name + "." + name + " is not wired."); return value;
    }
    private static void Invoke(object target, string field) => Reference<Button>(target, field).onClick.Invoke();
    private static bool UiMatches(DisplaySettingsView view, bool fullscreen, int width, int height) =>
        DisplayPreferences.IsFullscreen == fullscreen && DisplayPreferences.Width == width && DisplayPreferences.Height == height
        && Reference<Dropdown>(view, "screenModeDropdown").value == (fullscreen ? 0 : 1)
        && Reference<Dropdown>(view, "resolutionDropdown").value == IndexOf(width, height);
    private static int IndexOf(int width, int height)
    {
        for (int i = 0; i < DisplayPreferences.Resolutions.Count; i++)
            if (DisplayPreferences.Resolutions[i].x == width && DisplayPreferences.Resolutions[i].y == height) return i;
        return -1;
    }

    private IEnumerator Guarded(IEnumerator root)
    {
        var stack = new Stack<IEnumerator>(); stack.Push(root);
        while (stack.Count > 0 && !ending)
        {
            object yielded = null; bool next = false; Exception failure = null;
            try { next = stack.Peek().MoveNext(); if (next) yielded = stack.Peek().Current; }
            catch (Exception exception) { failure = exception; }
            if (failure != null) { Fail(failure); yield break; }
            if (!next) { stack.Pop(); continue; }
            if (yielded is IEnumerator nested) stack.Push(nested);
            else yield return yielded;
        }
    }
    private void Update()
    {
        if (!ending && report != null && Time.realtimeSinceStartup - started > 120f) Fail(new TimeoutException("Standalone display probe exceeded 120 realtime seconds."));
    }
    private void OnLog(string message, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) runtimeErrors.Add(message + "\n" + stack);
    }
    private void CheckResult(string name, bool passed, string details)
    {
        report.checks.Add(new Check { name = name, passed = passed, details = details });
        Flush();
        if (!passed) throw new InvalidOperationException("Check failed: " + name + ". " + details);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private void Flush() { if (!string.IsNullOrEmpty(reportPath)) File.WriteAllText(reportPath, JsonUtility.ToJson(report, true)); }
    private void Complete()
    {
        ending = true; report.status = "passed"; Flush(); Application.logMessageReceived -= OnLog;
        DisplayPreferences.Changed -= OnChanged; Application.Quit(0);
    }
    private void Fail(Exception exception)
    {
        if (ending) return;
        ending = true;
        if (report != null) { report.status = "failed"; report.errors.Add(exception.ToString()); report.errors.AddRange(runtimeErrors); Flush(); }
        Application.logMessageReceived -= OnLog; DisplayPreferences.Changed -= OnChanged;
        UnityEngine.Debug.LogError("Standalone display probe failed: " + exception);
        Application.Quit(1);
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
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, System.Text.StringBuilder name, int max);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
    private static NativeWindow ObserveWindow()
    {
        var result = new NativeWindow();
        uint wanted = (uint)Process.GetCurrentProcess().Id;
        EnumWindows((window, parameter) => {
            GetWindowThreadProcessId(window, out uint process);
            if (process != wanted) return true;
            var name = new System.Text.StringBuilder(256); GetClassName(window, name, name.Capacity);
            if (!name.ToString().StartsWith("UnityWndClass", StringComparison.Ordinal)) return true;
            result.found = true; result.handle = window.ToInt64(); result.className = name.ToString();
            var title = new System.Text.StringBuilder(512); GetWindowText(window, title, title.Capacity); result.title = title.ToString();
            GetWindowRect(window, out Rect outer); GetClientRect(window, out Rect client);
            result.left = outer.left; result.top = outer.top; result.width = outer.right - outer.left; result.height = outer.bottom - outer.top;
            result.clientWidth = client.right - client.left; result.clientHeight = client.bottom - client.top;
            result.style = unchecked((uint)GetWindowLong(window, -16)); result.extendedStyle = unchecked((uint)GetWindowLong(window, -20));
            result.hasCaption = (result.style & 0x00C00000u) == 0x00C00000u; result.popup = (result.style & 0x80000000u) != 0;
            result.visible = IsWindowVisible(window); result.iconic = IsIconic(window);
            var monitor = new MonitorInfo { size = Marshal.SizeOf(typeof(MonitorInfo)) };
            if (GetMonitorInfo(MonitorFromWindow(window, 2), ref monitor)) {
                result.monitorLeft = monitor.monitor.left; result.monitorTop = monitor.monitor.top;
                result.monitorWidth = monitor.monitor.right - monitor.monitor.left; result.monitorHeight = monitor.monitor.bottom - monitor.monitor.top;
            }
            result.dpi = GetDpiForWindow(window);
            return false;
        }, IntPtr.Zero);
        return result;
    }
}
