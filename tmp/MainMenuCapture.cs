// Temporary capture harness. Copy only to an isolated Unity project's Assets/Editor.
// Unity.exe -batchmode -force-d3d11 -projectPath <isolated project>
//   -executeMethod MainMenuCapture.RunBatch -logFile <temporary log>
// Keep graphics enabled and omit -quit. No source asset or scene is saved.
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
public static class MainMenuCapture
{
    private const string Prefix = "Codex.MainMenuCapture.";
    private const string ScenePath = "Assets/@Project/@sung_wook/@Game/00_Scenes/MainMenu.unity";
    private const string DefaultOutput = "C:/Users/wook1/GitBub-Unity/Science-Project-2026/Logs";
    private const int Width = 1600;
    private const int Height = 900;

    [Serializable]
    private sealed class Capture
    {
        public string view;
        public string scene;
        public string path;
        public int width;
        public int height;
        public int canvasCount;
        public bool settingsOpen;
        public bool transitionBusy;
        public bool physicsFrozen;
        public bool settingsContentEmpty;
        public string[] activeLabels;
        public string method = "Actual Play Mode camera render with the scene's existing Canvas UI.";
    }

    [Serializable]
    private sealed class Report
    {
        public string status = "running";
        public string mode = "Saved MainMenu scene initialized naturally in Play Mode; initial fade finished before capture. No asset or scene saves.";
        public List<Capture> captures = new List<Capture>();
        public List<string> errors = new List<string>();
    }

    private sealed class CanvasState
    {
        public Canvas canvas;
        public RenderMode mode;
        public Camera camera;
        public float planeDistance;
    }

    static MainMenuCapture()
    {
        if (Application.isBatchMode && SessionState.GetBool(Prefix + "Active", false)) Resume();
    }

    private static string OutputFolder
    {
        get
        {
            string configured = Environment.GetEnvironmentVariable("CODEX_MENU_CAPTURE_OUTPUT");
            return string.IsNullOrWhiteSpace(configured) ? DefaultOutput : Path.GetFullPath(configured);
        }
    }

    private static string ReportPath => Path.Combine(OutputFolder, "MainMenuCaptureReport.json");

    public static void RunBatch()
    {
        try
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use a separate isolated Unity batch editor.");
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("Screenshot capture requires graphics; omit -nographics.");
            Directory.CreateDirectory(OutputFolder);
            Save(new Report());
            SessionState.SetBool(Prefix + "Active", true);
            SessionState.SetInt(Prefix + "Phase", 0);
            SessionState.SetInt(Prefix + "ReadyFrame", -1);
            SessionState.SetString(Prefix + "StartedUtc", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Resume();
        }
        catch (Exception exception) { Fail(exception); }
    }

    private static void Resume()
    {
        // update survives the unrelated Search delayCall startup issue in this clone.
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
            DateTime start = DateTime.Parse(SessionState.GetString(Prefix + "StartedUtc", ""), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            if ((DateTime.UtcNow - start).TotalSeconds > 180d) throw new TimeoutException("Main menu capture exceeded 180 seconds.");
            int phase = SessionState.GetInt(Prefix + "Phase", 0);
            if (phase == 0)
            {
                if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
                SessionState.SetInt(Prefix + "Phase", 1);
                EditorApplication.EnterPlaymode();
                return;
            }
            if (!EditorApplication.isPlaying || EditorApplication.isPaused) return;
            if (SceneManager.GetActiveScene().path != ScenePath) return;
            MainMenuController menu = Object.FindFirstObjectByType<MainMenuController>();
            if (menu == null || !SceneTransitionController.IsAvailable) return;

            if (phase == 1)
            {
                if (SceneTransitionController.IsBusy)
                {
                    SessionState.SetInt(Prefix + "ReadyFrame", -1);
                    return;
                }
                int readyFrame = SessionState.GetInt(Prefix + "ReadyFrame", -1);
                if (readyFrame < 0)
                {
                    SessionState.SetInt(Prefix + "ReadyFrame", Time.frameCount);
                    return;
                }
                if (Time.frameCount < readyFrame + 4) return;
                Time.timeScale = 0f;
                Physics2D.SyncTransforms();
                AddCapture(RenderScene(menu, "main", "MainMenu.png"));
                menu.OpenSettings();
                if (!menu.IsSettingsOpen) throw new InvalidOperationException("OpenSettings did not open the authored settings window.");
                SessionState.SetInt(Prefix + "SettingsFrame", Time.frameCount);
                SessionState.SetInt(Prefix + "Phase", 2);
                return;
            }

            if (phase == 2)
            {
                // UI layout and Update continue on native frames at timeScale zero.
                if (Time.frameCount < SessionState.GetInt(Prefix + "SettingsFrame", 0) + 2) return;
                if (SceneTransitionController.IsBusy) throw new InvalidOperationException("Opening settings unexpectedly started a scene transition.");
                if (!menu.IsSettingsOpen) throw new InvalidOperationException("The authored settings window closed before capture.");
                AddCapture(RenderScene(menu, "settings", "MainMenuSettings.png"));
                Finish();
            }
        }
        catch (Exception exception) { Fail(exception); }
    }

    private static Capture RenderScene(MainMenuController menu, string view, string filename)
    {
        Camera camera = Camera.main;
        if (camera == null) throw new InvalidOperationException("No active MainCamera in MainMenu.");
        GameObject settings = new SerializedObject(menu).FindProperty("settingsWindow").objectReferenceValue as GameObject;
        Transform settingsContent = settings != null ? settings.transform.Find("Content") : null;
        bool empty = settingsContent != null && settingsContent.childCount == 0;
        if (!empty) throw new InvalidOperationException("The settings Content must exist and remain empty.");
        var labels = new List<string>();
        foreach (Text label in Object.FindObjectsByType<Text>(FindObjectsSortMode.None))
            if (label.isActiveAndEnabled) labels.Add(label.text);
        foreach (TMPro.TMP_Text label in Object.FindObjectsByType<TMPro.TMP_Text>(FindObjectsSortMode.None))
            if (label.isActiveAndEnabled) labels.Add(label.text);
        if (view == "main" && (!labels.Contains("시작") || !labels.Contains("설정") || !labels.Contains("나가기")))
            throw new InvalidOperationException("The three requested menu labels are not active.");

        RenderTexture previousTarget = camera.targetTexture;
        RenderTexture previousActive = RenderTexture.active;
        float previousAspect = camera.aspect;
        Rect previousRect = camera.rect;
        var states = new List<CanvasState>();
        RenderTexture target = null;
        Texture2D pixels = null;
        try
        {
            target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32)
            {
                name = "Temporary Saved Main Menu Capture",
                antiAliasing = 1,
                hideFlags = HideFlags.HideAndDontSave
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
            if (GraphicsSettings.currentRenderPipeline == null) camera.Render();
            else if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset)
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            else throw new InvalidOperationException("Expected the existing URP or built-in render pipeline.");
            RenderTexture.active = target;
            pixels = new Texture2D(Width, Height, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave };
            pixels.ReadPixels(new Rect(0f, 0f, Width, Height), 0, 0, false);
            pixels.Apply(false, false);
            byte[] png = pixels.EncodeToPNG();
            if (png == null || png.Length < 1000) throw new InvalidOperationException("Camera render produced no usable PNG.");
            string path = Path.Combine(OutputFolder, filename);
            File.WriteAllBytes(path, png);
            return new Capture
            {
                view = view, scene = ScenePath, path = path, width = Width, height = Height,
                canvasCount = states.Count, settingsOpen = menu.IsSettingsOpen,
                transitionBusy = SceneTransitionController.IsBusy,
                physicsFrozen = Mathf.Approximately(Time.timeScale, 0f),
                settingsContentEmpty = empty, activeLabels = labels.ToArray()
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
            camera.targetTexture = previousTarget;
            camera.aspect = previousAspect;
            camera.rect = previousRect;
            RenderTexture.active = previousActive;
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
        Debug.Log("Captured saved MainMenu " + capture.view + ": " + capture.path);
    }

    private static void OnLog(string message, string stack, LogType type)
    {
        if (!SessionState.GetBool(Prefix + "Active", false)) return;
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        Report report = Load();
        report.errors.Add(message + "\n" + stack);
        Save(report);
    }

    private static Report Load() => File.Exists(ReportPath) ? JsonUtility.FromJson<Report>(File.ReadAllText(ReportPath)) : new Report();
    private static void Save(Report report)
    {
        Directory.CreateDirectory(OutputFolder);
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
        EditorApplication.Exit(report.status == "passed" ? 0 : 1);
    }
}
