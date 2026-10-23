// Temporary opt-in standalone QA. Copy ONLY to isolated clone Assets; never production Assets.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using sung_wook;
using Object = UnityEngine.Object;

public sealed class QaUiAudioProbe : MonoBehaviour
{
    [Serializable] private sealed class Check { public string name; public bool passed; public string details; }
    [Serializable] private sealed class Layout { public string scene, path, text; public float x, y, width, height, preferredWidth, preferredHeight; public bool outsideScreen, heightTruncated; }
    [Serializable] private sealed class Finding { public string id, priority, title, evidence, reproduction; }
    [Serializable] private sealed class Report {
        public string status = "running", startedUtc, finishedUtc, unity, graphics;
        public string scope = "Opt-in Windows standalone QA using authored callbacks and normal rendered frames. Not a full human playthrough. Direct Screen mode change simulates an external fullscreen toggle; it does not prove native Alt+Enter was pressed.";
        public List<Check> checks = new List<Check>(); public List<Finding> findings = new List<Finding>();
        public List<Layout> layouts = new List<Layout>(); public List<string> screenshots = new List<string>(), errors = new List<string>();
        public bool preferencesRestored; public float meanFrameMs, maxFrameMs; public long managedMemoryStart, managedMemoryEnd;
    }
    [Serializable] private sealed class Backup {
        public string company, product; public bool hasWidth, hasHeight, hasFullscreen, hasVolume, hasMute, restored;
        public int width, height, fullscreen, mute; public float volume;
    }
    private Report report; private Backup backup; private string reportPath, backupPath; private bool ending, expectedError;
    private Keyboard keyboard, oldKeyboard; private InputSettings.UpdateMode oldUpdate; private InputSettings.BackgroundBehavior oldBackground;
    private float started; private int displayChanges, audioChanges; private readonly List<string> errors = new List<string>();
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot() {
        if (Application.isEditor || Array.IndexOf(Environment.GetCommandLineArgs(), "--qa-ui-audio") < 0) return;
        var go = new GameObject("Temporary UI Audio QA"); DontDestroyOnLoad(go);
        go.AddComponent<QaUiAudioProbe>().Initialize();
    }
    private static string Arg(string prefix) { foreach (string arg in Environment.GetCommandLineArgs()) if (arg.StartsWith(prefix, StringComparison.Ordinal)) return arg.Substring(prefix.Length); return null; }
    private void Initialize() {
        reportPath = Arg("--qa-report="); backupPath = Arg("--qa-backup="); started = Time.realtimeSinceStartup;
        report = new Report { startedUtc = DateTime.UtcNow.ToString("O"), unity = Application.unityVersion, graphics = SystemInfo.graphicsDeviceName + " / " + SystemInfo.graphicsDeviceType };
        try {
            Require(!string.IsNullOrEmpty(reportPath) && Path.IsPathRooted(reportPath) && !string.IsNullOrEmpty(backupPath) && Path.IsPathRooted(backupPath), "Absolute report and backup paths required.");
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)); Directory.CreateDirectory(Path.GetDirectoryName(backupPath));
            if (File.Exists(backupPath)) Require(JsonUtility.FromJson<Backup>(File.ReadAllText(backupPath)).restored, "An unrestored backup exists. Restore it first.");
            backup = new Backup { company = Application.companyName, product = Application.productName,
                hasWidth = PlayerPrefs.HasKey(DisplayPreferences.WidthKey), width = PlayerPrefs.GetInt(DisplayPreferences.WidthKey),
                hasHeight = PlayerPrefs.HasKey(DisplayPreferences.HeightKey), height = PlayerPrefs.GetInt(DisplayPreferences.HeightKey),
                hasFullscreen = PlayerPrefs.HasKey(DisplayPreferences.FullscreenKey), fullscreen = PlayerPrefs.GetInt(DisplayPreferences.FullscreenKey),
                hasVolume = PlayerPrefs.HasKey(SfxPreferences.VolumeKey), volume = PlayerPrefs.GetFloat(SfxPreferences.VolumeKey),
                hasMute = PlayerPrefs.HasKey(SfxPreferences.MuteKey), mute = PlayerPrefs.GetInt(SfxPreferences.MuteKey) };
            File.WriteAllText(backupPath, JsonUtility.ToJson(backup, true));
            Application.runInBackground = true; Application.logMessageReceived += OnLog;
            DisplayPreferences.Changed += DisplayChanged; SfxPreferences.Changed += AudioChanged;
            oldKeyboard = Keyboard.current; oldUpdate = InputSystem.settings.updateMode; oldBackground = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            keyboard = InputSystem.AddDevice<Keyboard>(); keyboard.MakeCurrent();
            StartCoroutine(Guarded(Run()));
        } catch (Exception e) { Finish(e); }
    }
    private IEnumerator Run() {
        yield return Ready("MainMenu");
        float splashDeadline = Time.realtimeSinceStartup + 12f;
        while (!UnityEngine.Rendering.SplashScreen.isFinished && Time.realtimeSinceStartup < splashDeadline) yield return null;
        yield return Delay(.5f);
        report.managedMemoryStart = GC.GetTotalMemory(false);
        int transitionId = Find<SceneTransitionController>().GetInstanceID(), sfxId = Find<ElectricSfxPlayer>().GetInstanceID();
        CheckResult("services-and-listener-once", Count<SceneTransitionController>() == 1 && Count<ElectricSfxPlayer>() == 1 && Count<AudioListener>() == 1, "Exactly one persistent transition, SFX service and active listener.");
        CheckResult("no-playing-looped-bgm", !PlayingLoop(), "Existing active AudioSources have no playing looping music.");
        MainMenuController menu = Find<MainMenuController>();
        Click(menu, "settingsButton");
        SfxSettingsView sfxView = Find<SfxSettingsView>(); DisplaySettingsView display = Find<DisplaySettingsView>();
        CheckResult("main-settings-authored-wiring", menu.IsSettingsOpen && sfxView != null && display != null, "Saved settings button opens both saved settings components.");
        Slider volume = Ref<Slider>(sfxView, "volumeSlider"); Toggle mute = Ref<Toggle>(sfxView, "muteToggle");
        volume.value = .37f; mute.isOn = false;
        CheckResult("volume-callback-saves", Mathf.Abs(SfxPreferences.Volume - .37f) < .001f && Mathf.Abs(PlayerPrefs.GetFloat(SfxPreferences.VolumeKey, -1) - .37f) < .001f, "Saved Slider callback updates gain and durable key.");
        mute.isOn = true;
        CheckResult("mute-callback-all-sources", SfxPreferences.IsMuted && AllSourcesMuted(true), "All gameplay and UI sources become muted immediately.");
        CheckResult("muted-volume-remains-independent", Mathf.Abs(SfxPreferences.Volume - .37f) < .001f, "Mute preserves chosen volume.");
        mute.isOn = false; volume.value = .63f;
        yield return Delay(.15f);
        bool uiPlayed = ElectricSfxPlayer.Play(SfxCue.UiClick);
        AudioSource ui = Ref<AudioSource>(Find<ElectricSfxPlayer>(), "uiSource");
        CheckResult("ui-sfx-valid-gain-and-clip", uiPlayed && ui.clip != null && ui.volume > 0f && ui.volume <= .63f && !ui.loop && !ui.mute, "UI sound starts with authored clip, proportional volume and non-looping playback.");
        Dropdown mode = Ref<Dropdown>(display, "screenModeDropdown"), resolution = Ref<Dropdown>(display, "resolutionDropdown");
        mode.value = 1;
        int low = IndexOf(640, 480); if (low < 0) low = 0;
        resolution.value = low; yield return Delay(.8f);
        CheckResult("low-resolution-native-windowed", Screen.fullScreenMode == FullScreenMode.Windowed && Screen.width == DisplayPreferences.Width && Screen.height == DisplayPreferences.Height, "Lowest supported authored resolution applied to real player buffer: " + Screen.width + "x" + Screen.height);
        yield return Capture("main-settings-low"); RecordLayout("main-settings-low");
        int initialDisplayListeners = ListenerCount(typeof(DisplayPreferences), "Changed"), initialAudioListeners = ListenerCount(typeof(SfxPreferences), "Changed");
        for (int i = 0; i < 6; i++) { Click(menu, "closeSettingsButton"); Click(menu, "settingsButton"); }
        CheckResult("settings-repeat-no-listener-growth", initialDisplayListeners == ListenerCount(typeof(DisplayPreferences), "Changed") && initialAudioListeners == ListenerCount(typeof(SfxPreferences), "Changed"), "Six authored close/open cycles retain one view subscription each.");
        int before = audioChanges; volume.value = .51f;
        CheckResult("reopened-slider-single-callback", audioChanges == before + 1, "One edit produces exactly one shared audio change notification.");
        resolution.Show(); yield return Delay(.3f);
        CheckResult("main-dropdown-opens", RefOrNull<GameObject>(resolution, "m_Dropdown") != null, "Actual saved Dropdown.Show creates its popup.");
        DispatchEscape(menu); yield return null; yield return null;
        CheckResult("main-dropdown-escape-cleans-popup", !menu.IsSettingsOpen && RefOrNull<GameObject>(resolution, "m_Dropdown") == null && RefOrNull<GameObject>(resolution, "m_Blocker") == null, "Esc closes settings and removes popup/blocker in normal deferred destruction frames.");
        Click(menu, "startButton"); Click(menu, "startButton");
        CheckResult("double-start-one-transition", SceneTransitionController.IsBusy && Time.timeScale == 0f, "Immediate second saved button callback cannot create another transition.");
        yield return Ready("Stage_02");
        CheckResult("fresh-stage-time-and-services", Time.timeScale == 1f && Find<SceneTransitionController>().GetInstanceID() == transitionId && Find<ElectricSfxPlayer>().GetInstanceID() == sfxId && Count<SceneTransitionController>() == 1 && Count<ElectricSfxPlayer>() == 1, "Start async fade restores time and reuses original services.");
        yield return Capture("stage02-low"); RecordLayout("stage02-low");
        PauseMenuController pause = Find<PauseMenuController>(); DispatchEscape(pause);
        CheckResult("escape-pauses-inputs", pause.IsPaused && Time.timeScale == 0f && InputsDisabled(), "Actual pause Update receives an opt-in virtual Esc; all supported gameplay input components are disabled.");
        ElectricBall2D ball = Find<ElectricBall2D>(); Vector2 frozen = ball.Body.position, velocity = ball.Body.linearVelocity; float fixedTime = Time.fixedTime;
        Click(pause, "settingsButton"); sfxView = Find<SfxSettingsView>(); display = Find<DisplaySettingsView>();
        CheckResult("pause-shares-settings-values", pause.IsSettingsOpen && Mathf.Abs(Ref<Slider>(sfxView, "volumeSlider").value - .51f) < .001f && Ref<Dropdown>(display, "resolutionDropdown").value == low, "Pause settings reflect exact main-menu audio/display values.");
        int sceneHandle = SceneManager.GetActiveScene().handle;
        Click(pause, "restartButton"); Click(pause, "mainMenuButton"); pause.Resume();
        CheckResult("settings-hidden-navigation-guards", !SceneTransitionController.IsBusy && SceneManager.GetActiveScene().handle == sceneHandle && pause.IsPaused && pause.IsSettingsOpen, "Hidden restart/menu/resume events do not bypass open settings.");
        CheckResult("paused-gameplay-sfx-rejected", !ElectricSfxPlayer.Play(SfxCue.Bounce) && !ElectricSfxPlayer.Play(SfxCue.Charge), "Gameplay SFX do not play through pause.");
        yield return Delay(.15f); CheckResult("paused-ui-sfx-accepted", ElectricSfxPlayer.Play(SfxCue.UiClick), "Menu SFX remain available at timeScale zero.");
        Ref<Toggle>(sfxView, "muteToggle").isOn = true;
        CheckResult("paused-mute-all-sources", AllSourcesMuted(true), "Mute updates currently paused gameplay and playing UI sources.");
        Ref<Slider>(sfxView, "volumeSlider").value = .24f; Ref<Toggle>(sfxView, "muteToggle").isOn = false;
        yield return Delay(.4f);
        CheckResult("paused-physics-frozen", Vector2.Distance(ball.Body.position, frozen) < .0001f && Vector2.Distance(ball.Body.linearVelocity, velocity) < .0001f && Mathf.Approximately(Time.fixedTime, fixedTime), "Audio and settings edits across real frames preserve paused ball position, velocity and fixed time.");
        yield return Capture("pause-settings-low"); RecordLayout("pause-settings-low");
        resolution = Ref<Dropdown>(display, "resolutionDropdown"); resolution.Show(); yield return Delay(.25f);
        DispatchEscape(pause); yield return null; yield return null;
        CheckResult("pause-dropdown-escape-stays-paused", pause.IsPaused && !pause.IsSettingsOpen && Time.timeScale == 0f && RefOrNull<GameObject>(resolution, "m_Dropdown") == null && RefOrNull<GameObject>(resolution, "m_Blocker") == null, "Esc clears popup and returns to pause without resuming.");
        DispatchEscape(pause);
        CheckResult("second-escape-resumes", !pause.IsPaused && Time.timeScale == 1f && Find<PlayerInputReader>().isActiveAndEnabled, "Following Esc restores enabled input state and simulation time.");
        yield return Delay(.2f);
        CheckResult("fixed-time-resumed", Time.fixedTime > fixedTime, "Fixed time advances after resume.");
        expectedError = true; bool accepted = SceneTransitionController.LoadScene("QA_INTENTIONALLY_MISSING_SCENE"); expectedError = false;
        CheckResult("invalid-scene-no-stuck-fade", !accepted && !SceneTransitionController.IsBusy && Time.timeScale == 1f, "Known invalid build path rejects before claiming pause/fade ownership; expected error excluded.");
        pause.Pause(); Click(pause, "restartButton"); Click(pause, "restartButton");
        yield return Ready("Stage_02");
        CheckResult("restart-fresh-stage-state", SceneManager.GetActiveScene().handle != sceneHandle && Time.timeScale == 1f && !Find<PauseMenuController>().IsPaused && Count<ElectricSfxPlayer>() == 1, "Double restart produces one fresh scene and no stale pause owner.");
        for (int stage = 3; stage <= 5; stage++) {
            Require(SceneTransitionController.LoadScene(ElectricStageProgression.StageScenePath(stage)), "Stage load rejected."); yield return Ready("Stage_" + stage.ToString("00"));
            yield return Capture("stage" + stage + "-low"); RecordLayout("stage" + stage + "-low");
            CheckResult("stage-" + stage + "-single-listener-service", Count<AudioListener>() == 1 && Count<SceneTransitionController>() == 1 && Count<ElectricSfxPlayer>() == 1 && !PlayingLoop(), "Scene has one listener/service and no active looped BGM.");
        }
        pause = Find<PauseMenuController>(); pause.Pause(); Click(pause, "mainMenuButton"); yield return Ready("MainMenu");
        menu = Find<MainMenuController>(); Click(menu, "settingsButton"); sfxView = Find<SfxSettingsView>(); display = Find<DisplaySettingsView>();
        CheckResult("return-menu-audio-shared", Mathf.Abs(Ref<Slider>(sfxView, "volumeSlider").value - .24f) < .001f && !Ref<Toggle>(sfxView, "muteToggle").isOn, "Main menu reflects pause changes after several async scene loads.");
        bool cached = DisplayPreferences.IsFullscreen; Screen.fullScreenMode = cached ? FullScreenMode.Windowed : FullScreenMode.FullScreenWindow;
        yield return Delay(.8f);
        bool actual = Screen.fullScreen; bool store = DisplayPreferences.IsFullscreen; int selected = Ref<Dropdown>(display, "screenModeDropdown").value;
        CheckResult("external-mode-observation-recorded", true, "After direct external Screen change: actual=" + actual + ", cached=" + store + ", dropdown=" + selected + ". Direct API simulation; not physical Alt+Enter input.");
        if (actual != store) report.findings.Add(new Finding { id = "UI-EXT-MODE", priority = "P2", title = "외부 전체화면 전환 후 설정 UI와 저장 상태가 실제 창 모드를 따르지 않음", evidence = "Actual Screen.fullScreen=" + actual + "; DisplayPreferences.IsFullscreen=" + store + "; dropdown=" + selected + ". allowFullscreenSwitch=1. Confirmed runtime by equivalent direct Screen mode change, native Alt+Enter not physically dispatched.", reproduction = "창 모드를 설정한 뒤 Alt+Enter 등 설정 UI 밖에서 모드를 바꾸고 설정창을 다시 연다. 캐시값과 실제 모드 동기화 경로가 없다. 같은 값을 재선택하면 setter가 조기 반환한다." });
        Screen.SetResolution(DisplayPreferences.Width, DisplayPreferences.Height, store ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
        yield return Delay(.3f); Click(menu, "closeSettingsButton");
        float end = Time.realtimeSinceStartup + 1.5f, sum = 0f, max = 0f; int frames = 0;
        while (Time.realtimeSinceStartup < end) { yield return null; float ms = Time.unscaledDeltaTime * 1000f; sum += ms; max = Mathf.Max(max, ms); frames++; }
        report.meanFrameMs = sum / Mathf.Max(1, frames); report.maxFrameMs = max; report.managedMemoryEnd = GC.GetTotalMemory(false);
        CheckResult("runtime-no-unexpected-error", errors.Count == 0, string.Join("\n", errors));
        CheckResult("repeated-scene-final-service-identity", Find<SceneTransitionController>().GetInstanceID() == transitionId && Find<ElectricSfxPlayer>().GetInstanceID() == sfxId, "Persistent service identity stable after main→2→restart2→3→4→5→main.");
        Finish(null);
    }
    private static IEnumerator Delay(float seconds) { float until = Time.realtimeSinceStartup + seconds; while (Time.realtimeSinceStartup < until) yield return null; }
    private IEnumerator Ready(string scene) { float deadline = Time.realtimeSinceStartup + 25f; while ((SceneManager.GetActiveScene().name != scene || SceneTransitionController.IsBusy || !SceneTransitionController.IsAvailable) && Time.realtimeSinceStartup < deadline) yield return null; Require(SceneManager.GetActiveScene().name == scene && !SceneTransitionController.IsBusy, "Timed out loading " + scene); yield return null; }
    private IEnumerator Capture(string label) { yield return new WaitForEndOfFrame(); string path = Path.Combine(Path.GetDirectoryName(reportPath), Path.GetFileNameWithoutExtension(reportPath) + "-" + label + ".png"); Texture2D capture = ScreenCapture.CaptureScreenshotAsTexture(); Require(capture != null, "No screenshot"); File.WriteAllBytes(path, capture.EncodeToPNG()); Object.Destroy(capture); report.screenshots.Add(path); Flush(); }
    private void RecordLayout(string label) {
        Canvas.ForceUpdateCanvases(); foreach (Text text in Object.FindObjectsByType<Text>(FindObjectsSortMode.None)) {
            if (!text.isActiveAndEnabled || string.IsNullOrWhiteSpace(text.text) || text.canvas == null || !text.canvas.isActiveAndEnabled) continue;
            RectTransform rt = text.rectTransform; Vector3[] corners = new Vector3[4]; rt.GetWorldCorners(corners); Canvas canvas = text.canvas;
            Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            Vector2 min = RectTransformUtility.WorldToScreenPoint(cam, corners[0]), max = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);
            report.layouts.Add(new Layout { scene = label, path = Hierarchy(text.transform), text = text.text, x = min.x, y = min.y, width = max.x - min.x, height = max.y - min.y,
                preferredWidth = text.preferredWidth, preferredHeight = text.preferredHeight,
                outsideScreen = min.x < -1f || min.y < -1f || max.x > Screen.width + 1f || max.y > Screen.height + 1f,
                heightTruncated = text.verticalOverflow == VerticalWrapMode.Truncate && text.preferredHeight > rt.rect.height + 2f });
        }
        // The active StageHUD uses TMP; the disabled legacy learning canvas must not be mistaken for visible UI.
        foreach (MonoBehaviour component in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)) {
            Type type = component.GetType(); if (type.FullName != "TMPro.TextMeshProUGUI" || !component.isActiveAndEnabled) continue;
            Canvas canvas = component.GetComponentInParent<Canvas>(); if (canvas == null || !canvas.isActiveAndEnabled) continue;
            string content = type.GetProperty("text").GetValue(component, null) as string; if (string.IsNullOrWhiteSpace(content)) continue;
            RectTransform rt = component.transform as RectTransform; if (rt == null) continue;
            Vector3[] corners = new Vector3[4]; rt.GetWorldCorners(corners); Camera cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            Vector2 min = RectTransformUtility.WorldToScreenPoint(cam, corners[0]), max = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);
            float preferredWidth = Convert.ToSingle(type.GetProperty("preferredWidth").GetValue(component, null)), preferredHeight = Convert.ToSingle(type.GetProperty("preferredHeight").GetValue(component, null));
            report.layouts.Add(new Layout { scene = label, path = Hierarchy(component.transform), text = content, x = min.x, y = min.y, width = max.x - min.x, height = max.y - min.y,
                preferredWidth = preferredWidth, preferredHeight = preferredHeight,
                outsideScreen = min.x < -1f || min.y < -1f || max.x > Screen.width + 1f || max.y > Screen.height + 1f,
                heightTruncated = false });
        }
        Flush();
    }
    private static string Hierarchy(Transform t) { string path = t.name; while (t.parent != null) { t = t.parent; path = t.name + "/" + path; } return path; }
    private static T Find<T>() where T : Object => Object.FindFirstObjectByType<T>(FindObjectsInactive.Exclude);
    private static int Count<T>() where T : Object => Object.FindObjectsByType<T>(FindObjectsSortMode.None).Length;
    private static T RefOrNull<T>(object target, string name) where T : class { if (target == null) return null; Type type = target.GetType(); while (type != null) { FieldInfo field = type.GetField(name, Fields); if (field != null) return field.GetValue(target) as T; type = type.BaseType; } return null; }
    private static T Ref<T>(object target, string name) where T : class { T value = RefOrNull<T>(target, name); Require(value != null, "Missing saved reference " + name); return value; }
    private static void Click(object target, string name) => Ref<Button>(target, name).onClick.Invoke();
    private static int IndexOf(int w, int h) { for (int i = 0; i < DisplayPreferences.Resolutions.Count; i++) if (DisplayPreferences.Resolutions[i] == new Vector2Int(w, h)) return i; return -1; }
    private static bool PlayingLoop() { foreach (AudioSource source in Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None)) if (source.isPlaying && source.loop) return true; return false; }
    private static bool AllSourcesMuted(bool value) { ElectricSfxPlayer player = Find<ElectricSfxPlayer>(); foreach (AudioSource source in Ref<AudioSource[]>(player, "gameplaySources")) if (source.mute != value) return false; return Ref<AudioSource>(player, "uiSource").mute == value; }
    private static bool InputsDisabled() { foreach (MonoBehaviour c in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)) if ((c is PlayerController || c is PlayerInputReader || c is ElectricStageInput2D || c is ElectricLearningInput2D || c is GearLeverController || c is global::LeverReader || c is global::LeverMover || c is DistanceLauncher2D || c is VoltageLauncher2D) && c.enabled) return false; return true; }
    private static int ListenerCount(Type type, string name) { Delegate handler = type.GetField(name, BindingFlags.Static | BindingFlags.NonPublic).GetValue(null) as Delegate; return handler == null ? 0 : handler.GetInvocationList().Length; }
    private void DispatchEscape(MonoBehaviour target) { InputSystem.QueueStateEvent(keyboard, new KeyboardState()); Pump(); InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape)); Pump(); Require(keyboard.escapeKey.wasPressedThisFrame, "Virtual Esc missing."); target.SendMessage("Update", SendMessageOptions.RequireReceiver); InputSystem.QueueStateEvent(keyboard, new KeyboardState()); Pump(); }
    private static void Pump() { typeof(InputSystem).GetMethod("Update", BindingFlags.Static | BindingFlags.NonPublic, null, new[] { typeof(InputUpdateType) }, null).Invoke(null, new object[] { InputUpdateType.Dynamic }); }
    private void DisplayChanged() => displayChanges++;
    private void AudioChanged() => audioChanges++;
    private void CheckResult(string name, bool passed, string details) { report.checks.Add(new Check { name = name, passed = passed, details = details }); Flush(); }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private void OnLog(string message, string stack, LogType type) { if (!expectedError && (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)) errors.Add(message + "\n" + stack); }
    private void Flush() { if (!string.IsNullOrEmpty(reportPath)) File.WriteAllText(reportPath, JsonUtility.ToJson(report, true)); }
    private IEnumerator Guarded(IEnumerator sequence) { var stack = new Stack<IEnumerator>(); stack.Push(sequence); while (stack.Count > 0 && !ending) { object item = null; bool next = false; Exception error = null; try { next = stack.Peek().MoveNext(); if (next) item = stack.Peek().Current; } catch (Exception e) { error = e; } if (error != null) { Finish(error); yield break; } if (!next) stack.Pop(); else if (item is IEnumerator child) stack.Push(child); else yield return item; } }
    private void Update() { if (!ending && report != null && Time.realtimeSinceStartup - started > 150f) Finish(new TimeoutException("UI audio QA exceeded 150 seconds.")); }
    private static void RestoreInt(string key, bool exists, int value) { if (exists) PlayerPrefs.SetInt(key, value); else PlayerPrefs.DeleteKey(key); }
    private void Restore() {
        if (backup == null) return;
        Require(backup.company == Application.companyName && backup.product == Application.productName, "Preference identity mismatch.");
        SfxPreferences.Save(); DisplayPreferences.Save();
        RestoreInt(DisplayPreferences.WidthKey, backup.hasWidth, backup.width); RestoreInt(DisplayPreferences.HeightKey, backup.hasHeight, backup.height); RestoreInt(DisplayPreferences.FullscreenKey, backup.hasFullscreen, backup.fullscreen);
        RestoreInt(SfxPreferences.MuteKey, backup.hasMute, backup.mute); if (backup.hasVolume) PlayerPrefs.SetFloat(SfxPreferences.VolumeKey, backup.volume); else PlayerPrefs.DeleteKey(SfxPreferences.VolumeKey);
        PlayerPrefs.Save(); backup.restored = true; File.WriteAllText(backupPath, JsonUtility.ToJson(backup, true)); report.preferencesRestored = true;
    }
    private void Finish(Exception error) {
        if (ending) return; ending = true;
        try { Restore(); } catch (Exception restoreError) { report.errors.Add(restoreError.ToString()); }
        if (error != null) report.errors.Add(error.ToString()); report.errors.AddRange(errors);
        Application.logMessageReceived -= OnLog; DisplayPreferences.Changed -= DisplayChanged; SfxPreferences.Changed -= AudioChanged;
        if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard); if (oldKeyboard != null && oldKeyboard.added) oldKeyboard.MakeCurrent();
        InputSystem.settings.updateMode = oldUpdate; InputSystem.settings.backgroundBehavior = oldBackground;
        bool failed = report.errors.Count > 0 || report.checks.Exists(c => !c.passed); report.status = failed ? "issues-found" : "passed"; report.finishedUtc = DateTime.UtcNow.ToString("O"); Flush(); Application.Quit(failed ? 1 : 0);
    }
}
