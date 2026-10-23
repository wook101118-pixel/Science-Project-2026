// Temporary validation harness. Copy only to an isolated Unity project's Assets/Editor.
// Run with graphics enabled and without -quit:
// Unity.exe -batchmode -projectPath <isolated project> -executeMethod SpaceMapCapture.RunBatch
// This renders existing saved scenes. It never creates or saves source assets/scenes.
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
using sung_wook;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class SpaceMapCapture
{
    private const string Prefix = "Codex.SpaceMapCapture.";
    private const string SceneFolder = "Assets/@Project/@sung_wook/@Game/00_Scenes/Stage/";
    private const string DefaultOutput = "C:/Users/wook1/GitBub-Unity/Science-Project-2026/Logs";
    private const int Width = 1600;
    private const int Height = 900;

    [Serializable]
    private sealed class Capture
    {
        public int stage;
        public string scene;
        public string path;
        public int width;
        public int height;
        public int canvasCount;
        public int activeSpriteCount;
        public int electricVectorGraphicsCount;
        public bool objectivePanelCollapsed;
        public float panelAnimationWaitSeconds;
        public string method = "Actual Play Mode camera render with existing Canvas UI; OnGUI excluded.";
        public bool physicsFrozen;
    }

    [Serializable]
    private sealed class Report
    {
        public string status = "running";
        public string mode = "Saved Stage 02-05 scenes initialized naturally in Play Mode, then captured at 1600x900. No scene or asset saves.";
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

    static SpaceMapCapture()
    {
        if (Application.isBatchMode && SessionState.GetBool(Prefix + "Active", false))
            Resume();
    }

    private static string OutputFolder
    {
        get
        {
            string configured = Environment.GetEnvironmentVariable("CODEX_SPACE_CAPTURE_OUTPUT");
            return string.IsNullOrWhiteSpace(configured) ? DefaultOutput : Path.GetFullPath(configured);
        }
    }

    private static string ReportPath => Path.Combine(OutputFolder, "SpaceMapCaptureReport.json");
    private static string ScenePath(int stage) => SceneFolder + "Stage_" + stage.ToString("00") + ".unity";
    private static string CapturePath(int stage) => Path.Combine(OutputFolder, "SpaceMapStage" + stage.ToString("00") + ".png");

    public static void RunBatch()
    {
        try
        {
            if (!Application.isBatchMode)
                throw new InvalidOperationException("Use a separate isolated Unity batch editor.");
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("Screenshot capture requires graphics; omit -nographics.");
            Directory.CreateDirectory(OutputFolder);
            Save(new Report());
            SessionState.SetBool(Prefix + "Active", true);
            SessionState.SetInt(Prefix + "Stage", 2);
            SessionState.SetString(Prefix + "StartedUtc", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            SessionState.SetString(Prefix + "ReadyScene", "");
            EditorSceneManager.OpenScene(ScenePath(2), OpenSceneMode.Single);
            Resume();
            EditorApplication.delayCall += WaitForStartup;
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    private static void Resume()
    {
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
    }

    private static void WaitForStartup() => EditorApplication.delayCall += EnterWhenIdle;

    private static void EnterWhenIdle()
    {
        if (!SessionState.GetBool(Prefix + "Active", false) || EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += EnterWhenIdle;
            return;
        }
        EditorApplication.EnterPlaymode();
    }

    private static void Tick()
    {
        if (!Application.isBatchMode || !SessionState.GetBool(Prefix + "Active", false)) return;
        try
        {
            DateTime start = DateTime.Parse(SessionState.GetString(Prefix + "StartedUtc", ""), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            if ((DateTime.UtcNow - start).TotalSeconds > 180d)
                throw new TimeoutException("Space map capture exceeded 180 seconds.");
            if (!EditorApplication.isPlaying || EditorApplication.isPaused) return;
            int stage = SessionState.GetInt(Prefix + "Stage", 2);
            string scenePath = ScenePath(stage);
            if (SceneManager.GetActiveScene().path != scenePath) return;
            var selector = Object.FindFirstObjectByType<ElectricStageSelector>();
            if (selector == null || selector.GetComponent<ElectricStageSession2D>()?.IsInitialized != true) return;

            if (SessionState.GetString(Prefix + "ReadyScene", "") != scenePath)
            {
                SessionState.SetString(Prefix + "ReadyScene", scenePath);
                SessionState.SetInt(Prefix + "ReadyFrame", Time.frameCount);
                SessionState.SetFloat(Prefix + "ReadyUnscaledTime", Time.unscaledTime);
                Time.timeScale = 1f;
                CollapsiblePanel panel = Object.FindFirstObjectByType<CollapsiblePanel>();
                if (panel == null)
                    throw new InvalidOperationException("The saved scene has no active objective panel.");
                float duration = new SerializedObject(panel).FindProperty("duration").floatValue;
                SessionState.SetFloat(Prefix + "PanelWaitSeconds", Mathf.Max(0.4f, duration + 0.1f));
                panel.Toggle();
                return;
            }
            // Let the existing panel finish its unscaled collapse animation and give
            // Start, LateUpdate and Canvas layout natural runtime frames before capture.
            if (Time.frameCount < SessionState.GetInt(Prefix + "ReadyFrame", 0) + 4) return;
            if (Time.unscaledTime - SessionState.GetFloat(Prefix + "ReadyUnscaledTime", 0f)
                < SessionState.GetFloat(Prefix + "PanelWaitSeconds", 0.4f)) return;
            Time.timeScale = 0f;
            Physics2D.SyncTransforms();
            Capture result = RenderScene(stage);
            Report report = Load();
            report.captures.Add(result);
            Save(report);
            Debug.Log("Captured existing Stage " + stage.ToString("00") + ": " + result.path);
            if (stage >= 5)
            {
                Finish();
                return;
            }
            Time.timeScale = 1f;
            SessionState.SetInt(Prefix + "Stage", stage + 1);
            SessionState.SetString(Prefix + "ReadyScene", "");
            SceneManager.LoadScene(ScenePath(stage + 1), LoadSceneMode.Single);
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    private static Capture RenderScene(int stage)
    {
        Camera camera = Camera.main;
        if (camera == null) throw new InvalidOperationException("No active MainCamera in " + ScenePath(stage));
        int vectorGraphicsCount = 0;
        foreach (Transform candidate in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (candidate.gameObject.scene.IsValid() && candidate.name == "Electric Vector Graphics")
                vectorGraphicsCount++;
        if (vectorGraphicsCount != 0)
            throw new InvalidOperationException("Runtime-created Electric Vector Graphics objects remain: " + vectorGraphicsCount);
        CollapsiblePanel panel = Object.FindFirstObjectByType<CollapsiblePanel>();
        var content = panel != null
            ? new SerializedObject(panel).FindProperty("content").objectReferenceValue as UnityEngine.UI.LayoutElement
            : null;
        bool panelCollapsed = content != null && Mathf.Abs(content.preferredHeight) < 0.01f;
        if (!panelCollapsed)
            throw new InvalidOperationException("Objective panel did not finish collapsing before capture.");
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
                name = "Temporary Saved Stage Capture",
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
                states.Add(new CanvasState
                {
                    canvas = canvas,
                    mode = canvas.renderMode,
                    camera = canvas.worldCamera,
                    planeDistance = canvas.planeDistance
                });
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = camera.nearClipPlane + 0.5f;
            }
            Canvas.ForceUpdateCanvases();
            if (GraphicsSettings.currentRenderPipeline == null)
                camera.Render();
            else if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset)
            {
                var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
                RenderPipeline.SubmitRenderRequest(camera, request);
            }
            else
                throw new InvalidOperationException("Expected the existing URP or built-in render pipeline.");
            RenderTexture.active = target;
            pixels = new Texture2D(Width, Height, TextureFormat.RGB24, false)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            pixels.ReadPixels(new Rect(0f, 0f, Width, Height), 0, 0, false);
            pixels.Apply(false, false);
            byte[] png = pixels.EncodeToPNG();
            if (png == null || png.Length < 1000)
                throw new InvalidOperationException("Camera render produced no usable PNG.");
            string path = CapturePath(stage);
            File.WriteAllBytes(path, png);
            return new Capture
            {
                stage = stage,
                scene = ScenePath(stage),
                path = path,
                width = Width,
                height = Height,
                canvasCount = states.Count,
                activeSpriteCount = Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None).Length,
                electricVectorGraphicsCount = vectorGraphicsCount,
                objectivePanelCollapsed = panelCollapsed,
                panelAnimationWaitSeconds = Time.unscaledTime - SessionState.GetFloat(Prefix + "ReadyUnscaledTime", 0f),
                physicsFrozen = Mathf.Approximately(Time.timeScale, 0f)
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
            if (target != null)
            {
                target.Release();
                Object.DestroyImmediate(target);
            }
        }
    }

    private static void OnLog(string message, string stack, LogType type)
    {
        if (!SessionState.GetBool(Prefix + "Active", false)) return;
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        Report report = Load();
        report.errors.Add(message + "\n" + stack);
        Save(report);
    }

    private static Report Load() => File.Exists(ReportPath)
        ? JsonUtility.FromJson<Report>(File.ReadAllText(ReportPath)) : new Report();

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
        report.status = report.errors.Count == 0 && report.captures.Count == 4 ? "passed" : "failed";
        Save(report);
        EditorApplication.Exit(report.status == "passed" ? 0 : 1);
    }
}
