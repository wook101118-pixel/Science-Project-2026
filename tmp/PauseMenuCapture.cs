// Temporary capture harness. Copy only to an isolated Unity project's Assets/Editor.
// Unity.exe -batchmode -force-d3d11 -projectPath <isolated project>
//   -executeMethod PauseMenuCapture.RunBatch -logFile <temporary log>
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
public static class PauseMenuCapture
{
    private const string Prefix = "Codex.PauseMenuCapture.";
    private const string ScenePath = "Assets/@Project/@sung_wook/@Game/00_Scenes/Stage/Stage_03.unity";
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
        public bool pauseOpen;
        public bool pauseRootVisible;
        public bool transitionBusy;
        public bool physicsFrozen;
        public string[] activeLabels;
        public string method = "Actual Play Mode camera render with the scene's existing Canvas UI.";
    }

    [Serializable]
    private sealed class Report
    {
        public string status = "running";
        public string mode = "Saved Stage_03 scene initialized naturally in Play Mode; initial fade finished, then PauseMenuController.Pause opened the saved UI. No asset or scene saves.";
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

    static PauseMenuCapture()
    {
        if (Application.isBatchMode && SessionState.GetBool(Prefix + "Active", false)) Resume();
    }

    private static string OutputFolder
    {
        get
        {
            string configured = Environment.GetEnvironmentVariable("CODEX_PAUSE_CAPTURE_OUTPUT");
            return string.IsNullOrWhiteSpace(configured) ? DefaultOutput : Path.GetFullPath(configured);
        }
    }

    private static string ReportPath => Path.Combine(OutputFolder, "PauseMenuCaptureReport.json");

    public static void RunBatch()
    {
        try
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use a separate isolated Unity batch editor.");
            if (Application.dataPath.Replace('\\', '/').IndexOf("/Temp/CodexUnityVisualBuild/", StringComparison.OrdinalIgnoreCase) < 0)
                throw new InvalidOperationException("Use the isolated CodexUnityVisualBuild clone only.");
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
            if ((DateTime.UtcNow - start).TotalSeconds > 180d) throw new TimeoutException("Pause menu capture exceeded 180 seconds.");
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
            PauseMenuController menu = Object.FindFirstObjectByType<PauseMenuController>();
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
                menu.Pause();
                if (!menu.IsPaused || !Mathf.Approximately(Time.timeScale, 0f))
                    throw new InvalidOperationException("Pause did not open the saved pause menu and stop physics.");
                Physics2D.SyncTransforms();
                SessionState.SetInt(Prefix + "PauseFrame", Time.frameCount);
                SessionState.SetInt(Prefix + "Phase", 2);
                return;
            }

            if (phase == 2)
            {
                if (Time.frameCount < SessionState.GetInt(Prefix + "PauseFrame", 0) + 2) return;
                if (SceneTransitionController.IsBusy) throw new InvalidOperationException("Opening pause unexpectedly started a scene transition.");
                if (!menu.IsPaused) throw new InvalidOperationException("The saved pause window closed before capture.");
                AddCapture(RenderScene(menu));
                Finish();
            }
        }
        catch (Exception exception) { Fail(exception); }
    }

    private static Capture RenderScene(PauseMenuController menu)
    {
        Camera camera = Camera.main;
        if (camera == null) throw new InvalidOperationException("No active MainCamera in Stage_03.");
        GameObject pauseRoot = new SerializedObject(menu).FindProperty("menuRoot").objectReferenceValue as GameObject;
        if (pauseRoot == null || !pauseRoot.activeInHierarchy)
            throw new InvalidOperationException("The saved pause root is not visible.");
        var labels = new List<string>();
        foreach (Text label in Object.FindObjectsByType<Text>(FindObjectsSortMode.None))
            if (label.isActiveAndEnabled) labels.Add(label.text);
        foreach (TMPro.TMP_Text label in Object.FindObjectsByType<TMPro.TMP_Text>(FindObjectsSortMode.None))
            if (label.isActiveAndEnabled) labels.Add(label.text);
        if (!labels.Contains("계속하기") || !labels.Contains("다시 시작") || !labels.Contains("메인 메뉴로 돌아가기"))
            throw new InvalidOperationException("The three requested pause menu labels are not active.");

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
                name = "Temporary Saved Pause Menu Capture",
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
            string path = Path.Combine(OutputFolder, "PauseMenu.png");
            File.WriteAllBytes(path, png);
            return new Capture
            {
                view = "paused", scene = ScenePath, path = path, width = Width, height = Height,
                canvasCount = states.Count, pauseOpen = menu.IsPaused, pauseRootVisible = pauseRoot.activeInHierarchy,
                transitionBusy = SceneTransitionController.IsBusy,
                physicsFrozen = Mathf.Approximately(Time.timeScale, 0f), activeLabels = labels.ToArray()
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
        Debug.Log("Captured saved pause menu: " + capture.path);
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
        report.status = report.errors.Count == 0 && report.captures.Count == 1 ? "passed" : "failed";
        Save(report);
        EditorApplication.Exit(report.status == "passed" ? 0 : 1);
    }
}
