// Temporary capture harness; use only the isolated clone, with graphics and without -quit.
// -batchmode -force-d3d11 -executeMethod BgmSettingsCapture.RunBatch
// RunBatchAndBuild also builds after leaving Play Mode in the same editor process.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using sung_wook;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class BgmSettingsCapture
{
    private const string Prefix = "Codex.BgmSettingsCapture.";
    private const string Root = "Assets/@Project/@sung_wook/@Game/00_Scenes/@InGame/";
    private const string MenuPath = Root + "MainMenu.unity";
    private const string StagePath = Root + "Stage/Stage_03.unity";
    private const string Output = "C:/Users/wook1/GitBub-Unity/Science-Project-2026/Logs/BgmSettings";
    private const string ReportPath = Output + "/Capture.json";
    private const int Width = 1600;
    private const int Height = 900;

    [Serializable] private sealed class Capture
    {
        public string scene;
        public string path;
        public int width = Width;
        public int height = Height;
        public int canvasCount;
        public bool settingsOpen;
        public bool paused;
        public bool transitionBusy;
        public float timeScale;
        public bool fullscreen;
        public string screenMode;
        public string resolution;
        public int resolutionOptions;
        public float sfxVolume;
        public bool sfxMuted;
        public float bgmVolume;
        public bool bgmMuted;
        public bool independentAudioControls;
        public List<LabelLayout> labelLayouts;
        public string[] activeLabels;
        public string method = "Actual Play Mode camera render with saved Canvas and current shared settings. No preference changes or asset saves.";
    }
    [Serializable] private sealed class Report
    {
        public string status = "running";
        public bool preferencesUnchanged;
        public List<Capture> captures = new List<Capture>();
        public List<string> errors = new List<string>();
    }
    [Serializable] private sealed class LabelLayout
    {
        public string text;
        public float width, height, preferredWidth, preferredHeight;
        public bool fits;
    }
    [Serializable] private sealed class Preference
    {
        public string key, type;
        public bool exists;
        public float floatValue;
        public int intValue;
    }
    [Serializable] private sealed class PreferenceSnapshot { public List<Preference> keys = new List<Preference>(); }
    private sealed class CanvasState
    {
        public Canvas canvas;
        public RenderMode mode;
        public Camera camera;
        public float planeDistance;
    }

    static BgmSettingsCapture()
    {
        if (Application.isBatchMode && SessionState.GetBool(Prefix + "Active", false)) Resume();
        if (Application.isBatchMode && SessionState.GetBool(Prefix + "FinishPending", false))
            EditorApplication.update += ExitWhenReady;
    }

    public static void RunBatch() => Start(false);
    public static void RunBatchAndBuild() => Start(true);

    private static void Start(bool build)
    {
        try
        {
            if (!Application.isBatchMode || Application.dataPath.Replace('\\', '/').IndexOf("/Temp/CodexUnityVisualBuild/", StringComparison.OrdinalIgnoreCase) < 0)
                throw new InvalidOperationException("Use the isolated CodexUnityVisualBuild project only.");
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) throw new InvalidOperationException("Graphics are required; omit -nographics.");
            Save(new Report());
            SessionState.SetBool(Prefix + "Build", build);
            SessionState.SetString(Prefix + "Preferences", JsonUtility.ToJson(SnapshotPreferences()));
            SessionState.SetBool(Prefix + "Active", true);
            SessionState.SetInt(Prefix + "Phase", 0);
            SessionState.SetInt(Prefix + "ReadyFrame", -1);
            SessionState.SetString(Prefix + "StartedUtc", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            EditorSceneManager.OpenScene(MenuPath, OpenSceneMode.Single);
            Resume();
        }
        catch (Exception exception) { Fail(exception); }
    }

    private static void Resume()
    {
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
    }

    private static void Tick()
    {
        if (!Application.isBatchMode || !SessionState.GetBool(Prefix + "Active", false)) return;
        try
        {
            DateTime started = DateTime.Parse(SessionState.GetString(Prefix + "StartedUtc", ""), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            if ((DateTime.UtcNow - started).TotalSeconds > 180d) throw new TimeoutException("BGM settings capture exceeded 180 seconds.");
            int phase = SessionState.GetInt(Prefix + "Phase", 0);
            if (phase == 0)
            {
                if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
                SessionState.SetInt(Prefix + "Phase", 1);
                EditorApplication.EnterPlaymode();
                return;
            }
            if (!EditorApplication.isPlaying || EditorApplication.isPaused || !SceneTransitionController.IsAvailable) return;
            if (phase == 1)
            {
                if (SceneManager.GetActiveScene().path != MenuPath || !ReadyAfterFade()) return;
                MainMenuController menu = Object.FindFirstObjectByType<MainMenuController>();
                if (menu == null) throw new InvalidOperationException("Saved main menu controller is missing.");
                Button button = new SerializedObject(menu).FindProperty("settingsButton").objectReferenceValue as Button;
                if (button == null || !button.isActiveAndEnabled || !button.interactable) throw new InvalidOperationException("Main-menu settings button is unavailable.");
                button.onClick.Invoke();
                if (!menu.IsSettingsOpen) throw new InvalidOperationException("Main-menu settings button did not open settings.");
                SessionState.SetInt(Prefix + "SettingsFrame", Time.frameCount);
                SessionState.SetInt(Prefix + "Phase", 2);
                return;
            }
            if (phase == 2)
            {
                if (Time.frameCount < SessionState.GetInt(Prefix + "SettingsFrame", 0) + 2) return;
                AddCapture(Render(false));
                MainMenuController menu = Object.FindFirstObjectByType<MainMenuController>();
                menu.CloseSettings();
                if (!SceneTransitionController.LoadScene(StagePath)) throw new InvalidOperationException("Could not load a saved stage through the existing transition service.");
                SessionState.SetInt(Prefix + "ReadyFrame", -1);
                SessionState.SetInt(Prefix + "Phase", 3);
                return;
            }
            if (phase == 3)
            {
                if (SceneManager.GetActiveScene().path != StagePath || !ReadyAfterFade()) return;
                PauseMenuController pause = Object.FindFirstObjectByType<PauseMenuController>();
                if (pause == null) throw new InvalidOperationException("Saved pause controller is missing.");
                pause.Pause();
                Button button = new SerializedObject(pause).FindProperty("settingsButton").objectReferenceValue as Button;
                if (button == null || !button.isActiveAndEnabled || !button.interactable) throw new InvalidOperationException("Pause settings button is unavailable.");
                button.onClick.Invoke();
                if (!pause.IsPaused || !pause.IsSettingsOpen || !Mathf.Approximately(Time.timeScale, 0f))
                    throw new InvalidOperationException("Pause settings did not preserve the stopped game state.");
                Physics2D.SyncTransforms();
                SessionState.SetInt(Prefix + "SettingsFrame", Time.frameCount);
                SessionState.SetInt(Prefix + "Phase", 4);
                return;
            }
            if (phase == 4)
            {
                if (Time.frameCount < SessionState.GetInt(Prefix + "SettingsFrame", 0) + 2) return;
                AddCapture(Render(true));
                Finish();
            }
        }
        catch (Exception exception) { Fail(exception); }
    }

    private static bool ReadyAfterFade()
    {
        if (SceneTransitionController.IsBusy)
        {
            SessionState.SetInt(Prefix + "ReadyFrame", -1);
            return false;
        }
        int ready = SessionState.GetInt(Prefix + "ReadyFrame", -1);
        if (ready < 0)
        {
            SessionState.SetInt(Prefix + "ReadyFrame", Time.frameCount);
            return false;
        }
        return Time.frameCount >= ready + 4;
    }

    private static Capture Render(bool paused)
    {
        if (SceneTransitionController.IsBusy) throw new InvalidOperationException("Capture began before fade completed.");
        Camera camera = Camera.main;
        DisplaySettingsView display = Object.FindFirstObjectByType<DisplaySettingsView>();
        SfxSettingsView sfx = Object.FindFirstObjectByType<SfxSettingsView>();
        BGMSettingView bgm = Object.FindFirstObjectByType<BGMSettingView>();
        if (camera == null || display == null || sfx == null || bgm == null) throw new InvalidOperationException("The saved display/BGM/SFX controls are not active.");
        var bgmFields = new SerializedObject(bgm);
        var sfxFields = new SerializedObject(sfx);
        Slider bgmSlider = bgmFields.FindProperty("volumeSlider").objectReferenceValue as Slider;
        Toggle bgmMute = bgmFields.FindProperty("muteToggle").objectReferenceValue as Toggle;
        Slider sfxSlider = sfxFields.FindProperty("volumeSlider").objectReferenceValue as Slider;
        Toggle sfxMute = sfxFields.FindProperty("muteToggle").objectReferenceValue as Toggle;
        if (bgmSlider == null || bgmMute == null || sfxSlider == null || sfxMute == null || bgmSlider == sfxSlider || bgmMute == sfxMute)
            throw new InvalidOperationException("BGM/SFX controls are missing or are not independent.");
        var fields = new SerializedObject(display);
        Dropdown mode = fields.FindProperty("screenModeDropdown").objectReferenceValue as Dropdown;
        Dropdown resolution = fields.FindProperty("resolutionDropdown").objectReferenceValue as Dropdown;
        if (mode == null || resolution == null || !mode.isActiveAndEnabled || !resolution.isActiveAndEnabled)
            throw new InvalidOperationException("The authored dropdown bindings are missing or hidden.");
        if (mode.value != (DisplayPreferences.IsFullscreen ? 0 : 1) || resolution.value != DisplayPreferences.SelectedResolutionIndex)
            throw new InvalidOperationException("The display controls do not match the shared persisted settings.");
        PauseMenuController pause = Object.FindFirstObjectByType<PauseMenuController>();
        MainMenuController menu = Object.FindFirstObjectByType<MainMenuController>();
        bool settingsOpen = paused ? pause != null && pause.IsSettingsOpen && pause.IsPaused : menu != null && menu.IsSettingsOpen;
        if (!settingsOpen || (paused && !Mathf.Approximately(Time.timeScale, 0f))) throw new InvalidOperationException("The requested settings view is not open.");
        var labels = new List<string>();
        foreach (Text label in Object.FindObjectsByType<Text>(FindObjectsSortMode.None))
            if (label.isActiveAndEnabled) labels.Add(label.text);
        if (!labels.Contains("BGM 볼륨") || !labels.Contains("BGM 음소거") || !labels.Contains("SFX 볼륨") || !labels.Contains("효과음 음소거") || !labels.Contains("화면 모드") || !labels.Contains("해상도"))
            throw new InvalidOperationException("The existing SFX or new display labels are not visible.");

        RenderTexture oldTarget = camera.targetTexture;
        RenderTexture oldActive = RenderTexture.active;
        float oldAspect = camera.aspect;
        Rect oldRect = camera.rect;
        var states = new List<CanvasState>();
        RenderTexture target = null;
        Texture2D pixels = null;
        try
        {
            target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32)
            {
                name = "Temporary Saved BGM Settings Capture", antiAliasing = 1, hideFlags = HideFlags.HideAndDontSave
            };
            target.Create();
            camera.targetTexture = target;
            camera.aspect = Width / (float)Height;
            camera.rect = new Rect(0f, 0f, 1f, 1f);
            foreach (Canvas canvas in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (!canvas.isActiveAndEnabled || canvas.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                states.Add(new CanvasState { canvas = canvas, mode = canvas.renderMode, camera = canvas.worldCamera, planeDistance = canvas.planeDistance });
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = camera.nearClipPlane + 0.5f;
            }
            Canvas.ForceUpdateCanvases();
            List<LabelLayout> labelLayouts = MeasureAudioLabels(bgmMute, sfxMute);
            foreach (LabelLayout label in labelLayouts)
                if (!label.fits) throw new InvalidOperationException("Audio mute label clips or wraps: " + JsonUtility.ToJson(label));
            if (GraphicsSettings.currentRenderPipeline == null) camera.Render();
            else if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset)
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            else throw new InvalidOperationException("Expected the existing URP or built-in pipeline.");
            RenderTexture.active = target;
            pixels = new Texture2D(Width, Height, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave };
            pixels.ReadPixels(new Rect(0f, 0f, Width, Height), 0, 0, false);
            pixels.Apply(false, false);
            byte[] png = pixels.EncodeToPNG();
            if (png == null || png.Length < 1000) throw new InvalidOperationException("Camera render produced no usable PNG.");
            string path = Path.Combine(Output, paused ? "PausedSettings.png" : "MenuSettings.png");
            File.WriteAllBytes(path, png);
            return new Capture
            {
                scene = SceneManager.GetActiveScene().path, path = path, canvasCount = states.Count,
                settingsOpen = settingsOpen, paused = paused, transitionBusy = SceneTransitionController.IsBusy,
                timeScale = Time.timeScale, fullscreen = DisplayPreferences.IsFullscreen,
                screenMode = mode.captionText.text, resolution = resolution.captionText.text,
                resolutionOptions = resolution.options.Count, sfxVolume = SfxPreferences.Volume,
                sfxMuted = SfxPreferences.IsMuted, bgmVolume = BGMPreferences.Volume, bgmMuted = BGMPreferences.IsMuted,
                independentAudioControls = true, labelLayouts = labelLayouts, activeLabels = labels.ToArray()
            };
        }
        finally
        {
            foreach (CanvasState state in states)
            {
                if (state.canvas == null) continue;
                state.canvas.renderMode = state.mode;
                state.canvas.worldCamera = state.camera;
                state.canvas.planeDistance = state.planeDistance;
            }
            camera.targetTexture = oldTarget;
            camera.aspect = oldAspect;
            camera.rect = oldRect;
            RenderTexture.active = oldActive;
            Canvas.ForceUpdateCanvases();
            if (pixels != null) Object.DestroyImmediate(pixels);
            if (target != null) { target.Release(); Object.DestroyImmediate(target); }
        }
    }

    private static void AddCapture(Capture capture)
    {
        Report report = Load();
        report.captures.Add(capture);
        Save(report);
        Debug.Log("Captured saved shared BGM settings: " + capture.path);
    }
    private static void OnLog(string message, string stack, LogType type)
    {
        if (!SessionState.GetBool(Prefix + "Active", false) || (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)) return;
        Report report = Load();
        report.errors.Add(message + "\n" + stack);
        Save(report);
    }
    private static Report Load() => File.Exists(ReportPath) ? JsonUtility.FromJson<Report>(File.ReadAllText(ReportPath)) : new Report();
    private static void Save(Report report)
    {
        Directory.CreateDirectory(Output);
        File.WriteAllText(ReportPath, JsonUtility.ToJson(report, true));
    }
    private static void Fail(Exception exception)
    {
        Report report = Load();
        report.errors.Add(exception.ToString());
        Save(report);
        Finish();
    }
    private static void Finish()
    {
        SessionState.SetBool(Prefix + "Active", false);
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= OnLog;
        Time.timeScale = 1f;
        Report report = Load();
        report.status = report.errors.Count == 0 && report.captures.Count == 2 ? "passed" : "failed";
        Save(report);
        SessionState.SetBool(Prefix + "FinishPending", true);
        EditorApplication.update -= ExitWhenReady;
        EditorApplication.update += ExitWhenReady;
        if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
    }

    private static void ExitWhenReady()
    {
        if (!SessionState.GetBool(Prefix + "FinishPending", false)) { EditorApplication.update -= ExitWhenReady; return; }
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        SessionState.SetBool(Prefix + "FinishPending", false);
        EditorApplication.update -= ExitWhenReady;
        Report report = Load();
        report.preferencesUnchanged = SessionState.GetString(Prefix + "Preferences", "") == JsonUtility.ToJson(SnapshotPreferences());
        if (!report.preferencesUnchanged)
        {
            report.errors.Add("The capture changed a BGM/SFX/display/unlock preference.");
            report.status = "failed";
        }
        Save(report);
        if (report.status == "passed" && SessionState.GetBool(Prefix + "Build", false))
        {
            try { BgmReleaseBuild.Build(); }
            catch (Exception error)
            {
                report.errors.Add(error.ToString());
                report.status = "failed";
                Save(report);
            }
        }
        EditorApplication.Exit(report.status == "passed" ? 0 : 1);
    }

    private static List<LabelLayout> MeasureAudioLabels(params Toggle[] toggles)
    {
        var result = new List<LabelLayout>();
        foreach (Toggle toggle in toggles)
        {
            Text text = toggle.transform.Find("Label").GetComponent<Text>();
            Rect rect = text.rectTransform.rect;
            result.Add(new LabelLayout
            {
                text = text.text, width = rect.width, height = rect.height,
                preferredWidth = text.preferredWidth, preferredHeight = text.preferredHeight,
                fits = text.preferredWidth <= rect.width + 1f && text.preferredHeight <= rect.height + 1f
            });
        }
        return result;
    }

    private static PreferenceSnapshot SnapshotPreferences()
    {
        var result = new PreferenceSnapshot();
        foreach (string key in new[] { BGMPreferences.VolumeKey, SfxPreferences.VolumeKey })
            result.keys.Add(new Preference { key = key, type = "float", exists = PlayerPrefs.HasKey(key), floatValue = PlayerPrefs.GetFloat(key) });
        foreach (string key in new[] { BGMPreferences.MuteKey, SfxPreferences.MuteKey, DisplayPreferences.WidthKey,
            DisplayPreferences.HeightKey, DisplayPreferences.FullscreenKey, ElectricStageProgression.UnlockKey })
            result.keys.Add(new Preference { key = key, type = "int", exists = PlayerPrefs.HasKey(key), intValue = PlayerPrefs.GetInt(key) });
        return result;
    }
}
