// Clone-only temporary Editor tool. Do not ship this file in the source project.
// Unity -batchmode -force-d3d11 -executeMethod ScienceCampaignCapture.RunBatch (omit -quit/-nographics).
// Two genuine render passes preserve the authored partial game viewport and full-screen saved UI.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using TMPro;
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
public static class ScienceCampaignCapture
{
    private const string Prefix = "Codex.ScienceCampaignCapture.";
    private const string Output = "C:/Users/wook1/GitBub-Unity/Science-Project-2026/Logs/ScienceCampaign";
    private const string ReportPath = Output + "/PresentationCapture.json";
    private const int Width = 1600, Height = 900;

    [Serializable] private sealed class Report
    {
        public string status = "running";
        public string startedUtc, finishedUtc;
        public bool progressUnchanged;
        public List<Capture> captures = new List<Capture>();
        public List<string> errors = new List<string>();
        public List<string> warnings = new List<string>();
    }
    [Serializable] private sealed class Capture
    {
        public int stage, width = Width, height = Height;
        public string scene, path;
        public string method = "Actual saved scene in Play Mode. The original camera projection renders its viewport at native pixel size; saved overlay canvases render full frame. A temporary RawImage composites that world render at the original camera rectangle. No authored object, text, physics or asset is changed.";
        public Rect authoredCameraRect;
        public Vector3 cameraPosition, playerPosition, playerViewportPosition;
        public float orthographicSize;
        public bool playerInsideFrame, textFits, presentationLabelsDoNotOverlap;
        public int overlayCanvasCount;
        public List<LabelLayout> labels = new List<LabelLayout>();
        public List<string> overlaps = new List<string>();
    }
    [Serializable] private sealed class LabelLayout
    {
        public string name, path, text;
        public float width, height, preferredHeight, fontSize;
        public Rect screenRect;
        public bool fits, insideFrame, generatedGlyphsFit;
        public int lineCount, characterCount;
    }
    private sealed class CanvasState
    {
        public Canvas canvas;
        public RenderMode mode;
        public Camera camera;
        public float planeDistance;
        public float scaleFactor;
    }

    static ScienceCampaignCapture()
    {
        if (!Application.isBatchMode) return;
        if (SessionState.GetBool(Prefix + "Active", false)) Resume();
        if (SessionState.GetBool(Prefix + "ExitPending", false)) EditorApplication.update += ExitWhenReady;
    }

    public static void RunBatch()
    {
        try
        {
            if (!Application.isBatchMode || Path.GetFullPath(Application.dataPath).Replace('\\', '/') != "C:/Users/wook1/AppData/Local/Temp/CodexUnityVisualBuild/Assets")
                throw new InvalidOperationException("Use only the isolated CodexUnityVisualBuild clone.");
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("Capture requires graphics. Omit -nographics.");
            Directory.CreateDirectory(Output);
            Save(new Report { startedUtc = DateTime.UtcNow.ToString("O") });
            SessionState.SetBool(Prefix + "UnlockExists", PlayerPrefs.HasKey(ElectricStageProgression.UnlockKey));
            SessionState.SetInt(Prefix + "UnlockValue", PlayerPrefs.GetInt(ElectricStageProgression.UnlockKey, 1));
            SessionState.SetBool(Prefix + "Active", true);
            SessionState.SetInt(Prefix + "Stage", 1);
            SessionState.SetBool(Prefix + "EnterRequested", false);
            SessionState.SetString(Prefix + "ReadyScene", "");
            SessionState.SetString(Prefix + "StartedUtc", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            EditorSceneManager.OpenScene(ElectricStageProgression.StageScenePath(1), OpenSceneMode.Single);
            Resume();
        }
        catch (Exception exception) { Fail(exception); }
    }

    public static void RunBatchThenInteraction()
    {
        SessionState.SetBool(Prefix + "ThenInteraction", true);
        RunBatch();
    }

    private static void Resume()
    {
        EditorApplication.update -= Tick; EditorApplication.update += Tick;
        Application.logMessageReceived -= OnLog; Application.logMessageReceived += OnLog;
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(Prefix + "Active", false)) return;
        try
        {
            DateTime start = DateTime.Parse(SessionState.GetString(Prefix + "StartedUtc", ""), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            if ((DateTime.UtcNow - start).TotalSeconds > 240) throw new TimeoutException("Presentation capture exceeded 240 seconds.");
            if (!EditorApplication.isPlaying)
            {
                if (!SessionState.GetBool(Prefix + "EnterRequested", false) && !EditorApplication.isCompiling && !EditorApplication.isUpdating && !EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    SessionState.SetBool(Prefix + "EnterRequested", true);
                    EditorApplication.EnterPlaymode();
                }
                return;
            }
            if (EditorApplication.isPaused) return;
            int stage = SessionState.GetInt(Prefix + "Stage", 1);
            string scenePath = ElectricStageProgression.StageScenePath(stage);
            if (SceneManager.GetActiveScene().path != scenePath) return;
            ElectricStageSelector selector = Object.FindFirstObjectByType<ElectricStageSelector>();
            SciencePresentationView view = Object.FindFirstObjectByType<SciencePresentationView>();
            if (selector == null || selector.SelectedStage != stage || selector.Ball == null || view == null
                || selector.GetComponent<ElectricStageSession2D>()?.IsInitialized != true) return;
            if (SessionState.GetString(Prefix + "ReadyScene", "") != scenePath)
            {
                SessionState.SetString(Prefix + "ReadyScene", scenePath);
                SessionState.SetInt(Prefix + "ReadyFrame", Time.frameCount);
                // Freeze initial physical state, but allow Start/LateUpdate and unscaled fade to finish.
                Time.timeScale = 0;
                return;
            }
            if (Time.frameCount < SessionState.GetInt(Prefix + "ReadyFrame", 0) + 5 || SceneTransitionController.IsBusy) return;
            Physics2D.SyncTransforms();
            Capture capture = Render(stage, selector, view);
            Report report = Load(); report.captures.Add(capture);
            if (!capture.playerInsideFrame) report.errors.Add("Stage " + stage + ": player is outside the authored game viewport.");
            if (!capture.textFits) report.errors.Add("Stage " + stage + ": one or more presentation text blocks overflow or leave frame; inspect labels.");
            if (!capture.presentationLabelsDoNotOverlap) report.errors.Add("Stage " + stage + ": presentation text rectangles overlap; inspect overlaps.");
            Save(report);
            Debug.Log("Captured actual science presentation Stage " + stage + ": " + capture.path);
            if (stage == 5) { Finish(); return; }
            SessionState.SetInt(Prefix + "Stage", stage + 1);
            SessionState.SetString(Prefix + "ReadyScene", "");
            Time.timeScale = 1;
            SceneManager.LoadScene(ElectricStageProgression.StageScenePath(stage + 1), LoadSceneMode.Single);
        }
        catch (Exception exception) { Fail(exception); }
    }

    private static Capture Render(int stage, ElectricStageSelector selector, SciencePresentationView view)
    {
        Camera camera = Camera.main;
        if (camera == null) throw new InvalidOperationException("Saved MainCamera is missing.");
        Rect savedRect = camera.rect;
        if (savedRect.x < 0 || savedRect.y < 0 || savedRect.xMax > 1.001f || savedRect.yMax > 1.001f || savedRect.width <= 0 || savedRect.height <= 0)
            throw new InvalidOperationException("Invalid authored camera viewport: " + savedRect);
        RenderTexture savedTarget = camera.targetTexture, savedActive = RenderTexture.active;
        float savedAspect = camera.aspect;
        var states = new List<CanvasState>();
        RenderTexture board = null, final = null;
        GameObject uiCameraObject = null, boardCanvasObject = null;
        Texture2D pixels = null;
        try
        {
            final = Target(Width, Height, "Temporary actual presentation frame");
            uiCameraObject = new GameObject("Temporary UI capture camera") { hideFlags = HideFlags.HideAndDontSave };
            Camera uiCamera = uiCameraObject.AddComponent<Camera>();
            uiCamera.enabled = false;
            uiCamera.transform.position = new Vector3(0, 0, -10000);
            uiCamera.orthographic = true; uiCamera.orthographicSize = 5;
            uiCamera.nearClipPlane = .01f; uiCamera.farClipPlane = 2;
            uiCamera.clearFlags = CameraClearFlags.SolidColor;
            uiCamera.backgroundColor = new Color(245f / 255f, 246f / 255f, 242f / 255f, 1);
            uiCamera.targetTexture = final; uiCamera.aspect = Width / (float)Height;
            uiCamera.allowHDR = false; uiCamera.allowMSAA = false;
            foreach (Canvas canvas in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (!canvas.isActiveAndEnabled || !canvas.isRootCanvas || canvas.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                states.Add(new CanvasState { canvas = canvas, mode = canvas.renderMode, camera = canvas.worldCamera, planeDistance = canvas.planeDistance, scaleFactor = canvas.scaleFactor });
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = uiCamera; canvas.planeDistance = .5f;
                // Match the CanvasScaler's authored policy at the capture resolution, not the batch Editor's GameView.
                CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
                if (scaler != null && scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize)
                {
                    float sx = Width / scaler.referenceResolution.x, sy = Height / scaler.referenceResolution.y;
                    canvas.scaleFactor = scaler.screenMatchMode == CanvasScaler.ScreenMatchMode.Expand ? Mathf.Min(sx, sy)
                        : scaler.screenMatchMode == CanvasScaler.ScreenMatchMode.Shrink ? Mathf.Max(sx, sy)
                        : Mathf.Pow(2, Mathf.Lerp(Mathf.Log(sx, 2), Mathf.Log(sy, 2), scaler.matchWidthOrHeight));
                }
            }
            Canvas.ForceUpdateCanvases();
            // Invoke the product's own responsive layout for its now-correct rendering surface.
            view.SendMessage("LateUpdate", SendMessageOptions.RequireReceiver);
            Canvas.ForceUpdateCanvases();
            Rect captureRect = camera.rect;
            int boardWidth = Mathf.RoundToInt(Width * captureRect.width), boardHeight = Mathf.RoundToInt(Height * captureRect.height);
            board = Target(boardWidth, boardHeight, "Temporary actual world viewport");
            camera.targetTexture = board; camera.rect = new Rect(0, 0, 1, 1); camera.aspect = boardWidth / (float)boardHeight;
            RenderCamera(camera, board);
            Vector3 viewport = camera.WorldToViewportPoint(selector.Ball.Body.worldCenterOfMass);
            var capture = new Capture {
                stage = stage, scene = SceneManager.GetActiveScene().path,
                path = Output + "/Stage" + stage.ToString("00") + "-Presentation.png",
                authoredCameraRect = captureRect, cameraPosition = camera.transform.position,
                orthographicSize = camera.orthographicSize, playerPosition = selector.Ball.transform.position,
                playerViewportPosition = viewport,
                playerInsideFrame = viewport.z > 0 && viewport.x > .025f && viewport.x < .975f && viewport.y > .025f && viewport.y < .975f
            };
            capture.overlayCanvasCount = states.Count;
            boardCanvasObject = new GameObject("Temporary actual viewport composite", typeof(RectTransform), typeof(Canvas)) { hideFlags = HideFlags.HideAndDontSave };
            Canvas backdrop = boardCanvasObject.GetComponent<Canvas>();
            backdrop.renderMode = RenderMode.ScreenSpaceCamera; backdrop.worldCamera = uiCamera; backdrop.planeDistance = 1;
            backdrop.sortingOrder = -30000;
            var imageObject = new GameObject("Actual world pixels", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage)) { hideFlags = HideFlags.HideAndDontSave };
            imageObject.transform.SetParent(boardCanvasObject.transform, false);
            RectTransform imageRect = imageObject.GetComponent<RectTransform>();
            imageRect.anchorMin = captureRect.min; imageRect.anchorMax = captureRect.max;
            imageRect.offsetMin = Vector2.zero; imageRect.offsetMax = Vector2.zero;
            RawImage raw = imageObject.GetComponent<RawImage>(); raw.texture = board; raw.raycastTarget = false;
            Canvas.ForceUpdateCanvases();
            capture.labels = InspectLabels(view, uiCamera);
            capture.textFits = capture.labels.Count >= 10;
            foreach (LabelLayout label in capture.labels) capture.textFits &= label.fits;
            for (int a = 0; a < capture.labels.Count; a++)
                for (int b = a + 1; b < capture.labels.Count; b++)
                {
                    Rect first = capture.labels[a].screenRect, second = capture.labels[b].screenRect;
                    first.xMin += .5f; first.xMax -= .5f; first.yMin += .5f; first.yMax -= .5f;
                    if (first.Overlaps(second)) capture.overlaps.Add(capture.labels[a].name + " overlaps " + capture.labels[b].name);
                }
            capture.presentationLabelsDoNotOverlap = capture.overlaps.Count == 0;
            RenderCamera(uiCamera, final);
            RenderTexture.active = final;
            pixels = new Texture2D(Width, Height, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave };
            pixels.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false); pixels.Apply(false, false);
            byte[] png = pixels.EncodeToPNG();
            if (png == null || png.Length < 10000) throw new InvalidOperationException("No usable presentation PNG was rendered.");
            File.WriteAllBytes(capture.path, png);
            return capture;
        }
        finally
        {
            foreach (CanvasState state in states)
                if (state.canvas != null) { state.canvas.renderMode = state.mode; state.canvas.worldCamera = state.camera; state.canvas.planeDistance = state.planeDistance; state.canvas.scaleFactor = state.scaleFactor; }
            camera.targetTexture = savedTarget; camera.rect = savedRect; camera.aspect = savedAspect;
            RenderTexture.active = savedActive;
            if (boardCanvasObject != null) Object.DestroyImmediate(boardCanvasObject);
            if (uiCameraObject != null) Object.DestroyImmediate(uiCameraObject);
            Canvas.ForceUpdateCanvases();
            view.SendMessage("LateUpdate", SendMessageOptions.RequireReceiver);
            Canvas.ForceUpdateCanvases();
            if (pixels != null) Object.DestroyImmediate(pixels);
            if (board != null) { board.Release(); Object.DestroyImmediate(board); }
            if (final != null) { final.Release(); Object.DestroyImmediate(final); }
        }
    }

    private static RenderTexture Target(int width, int height, string name)
    {
        var result = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
        { name = name, antiAliasing = 1, hideFlags = HideFlags.HideAndDontSave };
        result.Create(); return result;
    }
    private static void RenderCamera(Camera camera, RenderTexture destination)
    {
        if (GraphicsSettings.currentRenderPipeline == null) camera.Render();
        else if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset)
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = destination });
        else throw new InvalidOperationException("Expected the project's existing URP renderer.");
    }

    private static List<LabelLayout> InspectLabels(SciencePresentationView view, Camera camera)
    {
        var result = new List<LabelLayout>();
        foreach (TMP_Text label in view.GetComponentsInChildren<TMP_Text>(false))
        {
            if (!label.isActiveAndEnabled) continue;
            label.ForceMeshUpdate();
            Rect local = label.rectTransform.rect;
            Vector2 preferred = label.GetPreferredValues(label.text, local.width, float.PositiveInfinity);
            var corners = new Vector3[4]; label.rectTransform.GetWorldCorners(corners);
            Vector2 min = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
            Vector2 max = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
            Rect screenRect = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            bool glyphsFit = true;
            foreach (TMP_CharacterInfo character in label.textInfo.characterInfo)
            {
                if (!character.isVisible) continue;
                if (character.bottomLeft.x < local.xMin - 1.5f || character.topRight.x > local.xMax + 1.5f
                    || character.bottomLeft.y < local.yMin - 1.5f || character.topRight.y > local.yMax + 1.5f) glyphsFit = false;
            }
            bool inFrame = min.x >= -.5f && min.y >= -.5f && max.x <= Width + .5f && max.y <= Height + .5f;
            result.Add(new LabelLayout {
                name = label.name, path = PathOf(label.transform), text = label.text,
                width = local.width, height = local.height, preferredHeight = preferred.y,
                fontSize = label.fontSize, screenRect = screenRect, insideFrame = inFrame,
                generatedGlyphsFit = glyphsFit, lineCount = label.textInfo.lineCount, characterCount = label.textInfo.characterCount,
                fits = inFrame && preferred.y <= local.height + 1.5f && glyphsFit
            });
        }
        return result;
    }
    private static string PathOf(Transform item)
    {
        string path = item.name;
        for (Transform parent = item.parent; parent != null; parent = parent.parent) path = parent.name + "/" + path;
        return path;
    }
    private static void OnLog(string message, string stack, LogType type)
    {
        if (!SessionState.GetBool(Prefix + "Active", false)) return;
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert && type != LogType.Warning) return;
        Report report = Load();
        if (type == LogType.Warning) report.warnings.Add(message); else report.errors.Add(message + "\n" + stack);
        Save(report);
    }
    private static Report Load() => File.Exists(ReportPath) ? JsonUtility.FromJson<Report>(File.ReadAllText(ReportPath)) : new Report();
    private static void Save(Report report) { Directory.CreateDirectory(Output); File.WriteAllText(ReportPath, JsonUtility.ToJson(report, true)); }
    private static void Fail(Exception exception) { Report report = Load(); report.errors.Add(exception.ToString()); Save(report); Finish(); }
    private static void Finish()
    {
        SessionState.SetBool(Prefix + "Active", false);
        EditorApplication.update -= Tick; Application.logMessageReceived -= OnLog;
        Time.timeScale = 1;
        Report report = Load();
        report.progressUnchanged = PlayerPrefs.HasKey(ElectricStageProgression.UnlockKey) == SessionState.GetBool(Prefix + "UnlockExists", false)
            && PlayerPrefs.GetInt(ElectricStageProgression.UnlockKey, 1) == SessionState.GetInt(Prefix + "UnlockValue", 1);
        if (!report.progressUnchanged) report.errors.Add("Unexpected progression preference change during read-only capture.");
        report.status = report.errors.Count == 0 && report.captures.Count == 5 ? "passed" : "failed";
        report.finishedUtc = DateTime.UtcNow.ToString("O"); Save(report);
        SessionState.SetInt(Prefix + "ExitCode", report.status == "passed" ? 0 : 1);
        SessionState.SetBool(Prefix + "ExitPending", true);
        EditorApplication.update -= ExitWhenReady; EditorApplication.update += ExitWhenReady;
        if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
    }
    private static void ExitWhenReady()
    {
        if (!SessionState.GetBool(Prefix + "ExitPending", false)) { EditorApplication.update -= ExitWhenReady; return; }
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        SessionState.SetBool(Prefix + "ExitPending", false); EditorApplication.update -= ExitWhenReady;
        if (SessionState.GetBool(Prefix + "ThenInteraction", false) && SessionState.GetInt(Prefix + "ExitCode", 1) == 0)
        {
            SessionState.SetBool(Prefix + "ThenInteraction", false);
            Type.GetType("ScienceCampaignInteractionValidation, Assembly-CSharp-Editor", true).GetMethod("RunBatch").Invoke(null, null);
            return;
        }
        EditorApplication.Exit(SessionState.GetInt(Prefix + "ExitCode", 1));
    }
}
