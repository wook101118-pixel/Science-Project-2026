// Clone-only, read-only overview capture and layout inspection.
// Unity.exe -batchmode -force-d3d11 -projectPath <clone> -executeMethod TilemapLayoutCapture.RunBatch
// Omit -quit and -nographics. No scene or asset saves are performed.
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
using UnityEngine.Tilemaps;
using sung_wook;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class TilemapLayoutCapture
{
    private const string Prefix = "Codex.TilemapLayoutCapture.";
    private const string Clone = "C:/Users/wook1/AppData/Local/Temp/CodexUnityVisualBuild";
    private const string DefaultOutput = "C:/Users/wook1/GitBub-Unity/Science-Project-2026/Logs/TilemapLayout";
    private const int Width = 1600, Height = 900;

    [Serializable] private sealed class Capture
    {
        public int stage, width = Width, height = Height, canvasCount;
        public string scene, path, layoutPath;
        public Vector3 cameraPosition;
        public float orthographicSize, panelAnimationWaitSeconds;
        public bool objectivePanelCollapsed, physicsFrozen;
        public string method = "Actual initialized Play Mode camera and existing Canvas UI. Camera position/size unchanged. OnGUI excluded.";
    }
    [Serializable] private sealed class CaptureReport
    {
        public string status = "running", startedUtc = DateTime.UtcNow.ToString("O"), finishedUtc;
        public string mode = "Saved Stage 01-05 scenes initialized naturally; existing objective panel collapsed and physics frozen before camera render. No scene or asset saves.";
        public List<Capture> captures = new List<Capture>();
        public List<string> errors = new List<string>();
    }
    [Serializable] private sealed class Cell
    {
        public Vector3Int cell;
        public string tileAsset, tileGuid, colliderType;
        public Vector3 worldCenter, worldMin, worldMax;
    }
    [Serializable] private sealed class SolidTilemap
    {
        public string name, path, prefabAsset, tag, compositeOperation;
        public Vector3 localPosition, localScale, worldPosition, worldScale, cellSize;
        public Vector3Int origin, size;
        public Vector3 worldMin, worldMax, colliderBoundsCenter, colliderBoundsSize;
        public Vector3 rendererBoundsCenter, rendererBoundsSize;
        public bool tileColliderEnabled, compositeEnabled, hasPendingTilemapChanges, collisionReady;
        public int occupiedCellCount, gridColliderCellCount, colliderShapeCount, compositePathCount, compositeShapeCount;
        public List<Cell> cells = new List<Cell>();
    }
    [Serializable] private sealed class Point
    {
        public string kind, name, path, tag, prefabAsset;
        public bool active, hasBox;
        public Vector3 localPosition, localScale, worldPosition, worldScale;
        public Vector3 boxWorldMin, boxWorldMax;
        public List<ReferencePoint> references = new List<ReferencePoint>();
    }
    [Serializable] private sealed class ReferencePoint
    {
        public string field, name, path;
        public Vector3 worldPosition, localPosition;
    }
    [Serializable] private sealed class StageLayout
    {
        public int stage;
        public string scene;
        public Vector3 cameraPosition, playerPosition;
        public Vector2 savedSpawn;
        public bool hasSavedSpawn;
        public float orthographicSize;
        public List<SolidTilemap> solidTilemaps = new List<SolidTilemap>();
        public List<Point> legacyRouteAnchors = new List<Point>();
        public List<Point> mechanismsStarsAndGoals = new List<Point>();
    }
    [Serializable] private sealed class LayoutReport
    {
        public string status = "running", startedUtc = DateTime.UtcNow.ToString("O"), finishedUtc;
        public string method = "Read-only inspection of naturally initialized active scenes in Play Mode. Cell world bounds use Grid.CellToWorld corners; legacy BoxCollider bounds derive from saved transform/offset/size even when disabled.";
        public List<StageLayout> stages = new List<StageLayout>();
    }
    private sealed class CanvasState
    {
        public Canvas canvas;
        public RenderMode mode;
        public Camera camera;
        public float planeDistance;
        public bool enabled;
    }

    static TilemapLayoutCapture()
    {
        if (!Application.isBatchMode) return;
        if (SessionState.GetBool(Prefix + "Active", false)) Subscribe();
        if (SessionState.GetBool(Prefix + "ExitPending", false)) EditorApplication.update += ExitWhenReady;
    }
    private static string Output => SessionState.GetString(Prefix + "Output", DefaultOutput);
    private static string CaptureReportPath => Path.Combine(Output, "CaptureReport.json");
    private static string LayoutReportPath => Path.Combine(Output, "LayoutInspection.json");
    private static string ScenePath(int stage) => ElectricStageProgression.StageScenePath(stage);
    private static string CapturePath(int stage, bool includeUi = true) => Path.Combine(Output, "Stage" + stage.ToString("00") + (includeUi ? "" : "-Layout") + ".png");

    public static void RunBatch()
    {
        if (!Application.isBatchMode || Path.GetFullPath(Directory.GetParent(Application.dataPath).FullName).Replace('\\', '/') != Clone)
            throw new InvalidOperationException("Use only the isolated CodexUnityVisualBuild batch project.");
        string configured = Environment.GetEnvironmentVariable("CODEX_TILEMAP_CAPTURE_OUTPUT");
        SessionState.SetString(Prefix + "Output", string.IsNullOrWhiteSpace(configured) ? DefaultOutput : Path.GetFullPath(configured));
        Directory.CreateDirectory(Output);
        Save(new CaptureReport());
        SaveLayout(new LayoutReport());
        try
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("Screenshots require graphics. Omit -nographics.");
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Begin with the isolated editor outside Play Mode.");
            SessionState.SetBool(Prefix + "Active", true);
            SessionState.SetBool(Prefix + "ExitPending", false);
            SessionState.SetBool(Prefix + "EnterRequested", false);
            SessionState.SetInt(Prefix + "Stage", 1);
            SessionState.SetString(Prefix + "ReadyScene", "");
            SessionState.SetString(Prefix + "StartedUtc", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            EditorSceneManager.OpenScene(ScenePath(1), OpenSceneMode.Single);
            Subscribe();
        }
        catch (Exception error) { Fail(error); }
    }
    private static void Subscribe()
    {
        EditorApplication.update -= Tick; EditorApplication.update += Tick;
        Application.logMessageReceived -= OnLog; Application.logMessageReceived += OnLog;
    }
    private static void Tick()
    {
        if (!Application.isBatchMode || !SessionState.GetBool(Prefix + "Active", false)) return;
        try
        {
            DateTime start = DateTime.Parse(SessionState.GetString(Prefix + "StartedUtc", ""), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            if ((DateTime.UtcNow - start).TotalSeconds > 240d) throw new TimeoutException("Five-stage layout capture exceeded 240 seconds.");
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
            string scenePath = ScenePath(stage);
            if (SceneManager.GetActiveScene().path != scenePath) return;
            ElectricStageSelector selector = Object.FindFirstObjectByType<ElectricStageSelector>();
            if (selector == null || selector.SelectedStage != stage || selector.GetComponent<ElectricStageSession2D>()?.IsInitialized != true) return;
            if (SessionState.GetString(Prefix + "ReadyScene", "") != scenePath)
            {
                SessionState.SetString(Prefix + "ReadyScene", scenePath);
                SessionState.SetInt(Prefix + "ReadyFrame", Time.frameCount);
                SessionState.SetFloat(Prefix + "ReadyUnscaledTime", Time.unscaledTime);
                CollapsiblePanel panel = Object.FindFirstObjectByType<CollapsiblePanel>();
                if (panel == null) throw new InvalidOperationException("Saved scene has no active objective panel.");
                panel.SetOpen(false);
                float duration = new SerializedObject(panel).FindProperty("duration").floatValue;
                SessionState.SetFloat(Prefix + "PanelWaitSeconds", Mathf.Max(0.4f, duration + 0.1f));
                Time.timeScale = 0f;
                return;
            }
            if (Time.frameCount < SessionState.GetInt(Prefix + "ReadyFrame", 0) + 4) return;
            if (Time.unscaledTime - SessionState.GetFloat(Prefix + "ReadyUnscaledTime", 0f) < SessionState.GetFloat(Prefix + "PanelWaitSeconds", .4f)) return;
            Time.timeScale = 0f;
            Physics2D.SyncTransforms();
            LayoutReport layout = LoadLayout();
            layout.stages.Add(Inspect(stage, selector));
            SaveLayout(layout);
            Capture result = RenderScene(stage, true);
            Capture cameraOnly = RenderScene(stage, false);
            result.layoutPath = cameraOnly.path;
            CaptureReport report = Load(); report.captures.Add(result); Save(report);
            Debug.Log("Captured and inspected existing Stage " + stage.ToString("00") + ": " + result.path);
            if (stage >= 5) { Finish(); return; }
            SessionState.SetInt(Prefix + "Stage", stage + 1);
            SessionState.SetString(Prefix + "ReadyScene", "");
            Time.timeScale = 1f;
            SceneManager.LoadScene(ScenePath(stage + 1), LoadSceneMode.Single);
        }
        catch (Exception error) { Fail(error); }
    }

    private static StageLayout Inspect(int stage, ElectricStageSelector selector)
    {
        var result = new StageLayout { stage = stage, scene = ScenePath(stage), playerPosition = selector.Ball.transform.position };
        result.hasSavedSpawn = selector.TryGetSpawnPosition(out result.savedSpawn);
        if (Camera.main != null) { result.cameraPosition = Camera.main.transform.position; result.orthographicSize = Camera.main.orthographicSize; }
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (Tilemap map in root.GetComponentsInChildren<Tilemap>(true))
            {
                TilemapCollider2D collider = map.GetComponent<TilemapCollider2D>();
                if (!map.gameObject.activeInHierarchy || !map.enabled || collider == null || !collider.enabled || collider.isTrigger) continue;
                var item = new SolidTilemap {
                    name = map.name, path = HierarchyPath(map.transform), prefabAsset = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(map.gameObject),
                    tag = map.tag, localPosition = map.transform.localPosition, localScale = map.transform.localScale,
                    worldPosition = map.transform.position, worldScale = map.transform.lossyScale,
                    cellSize = map.layoutGrid.cellSize, origin = map.origin, size = map.size,
                    tileColliderEnabled = collider.enabled, compositeOperation = collider.compositeOperation.ToString(),
                    colliderShapeCount = collider.shapeCount, colliderBoundsCenter = collider.bounds.center, colliderBoundsSize = collider.bounds.size,
                    hasPendingTilemapChanges = collider.hasTilemapChanges
                };
                CompositeCollider2D composite = map.GetComponent<CompositeCollider2D>();
                item.compositeEnabled = composite != null && composite.enabled;
                if (composite != null) { item.compositePathCount = composite.pathCount; item.compositeShapeCount = composite.shapeCount; }
                item.collisionReady = !item.hasPendingTilemapChanges && (item.compositeEnabled ? item.compositeShapeCount > 0 : item.colliderShapeCount > 0);
                TilemapRenderer renderer = map.GetComponent<TilemapRenderer>();
                if (renderer != null) { item.rendererBoundsCenter = renderer.bounds.center; item.rendererBoundsSize = renderer.bounds.size; }
                bool first = true;
                Bounds worldBounds = default;
                foreach (Vector3Int position in map.cellBounds.allPositionsWithin)
                {
                    TileBase tile = map.GetTile(position); if (tile == null) continue;
                    Bounds cellBounds = CellBounds(map, position);
                    string tilePath = AssetDatabase.GetAssetPath(tile);
                    item.cells.Add(new Cell { cell = position, tileAsset = tilePath, tileGuid = AssetDatabase.AssetPathToGUID(tilePath),
                        colliderType = map.GetColliderType(position).ToString(), worldCenter = map.GetCellCenterWorld(position), worldMin = cellBounds.min, worldMax = cellBounds.max });
                    if (map.GetColliderType(position) == Tile.ColliderType.Grid) item.gridColliderCellCount++;
                    if (first) { worldBounds = cellBounds; first = false; } else worldBounds.Encapsulate(cellBounds);
                }
                item.occupiedCellCount = item.cells.Count;
                item.worldMin = worldBounds.min; item.worldMax = worldBounds.max;
                result.solidTilemaps.Add(item);
            }
            foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
            {
                if (candidate.GetComponent<Tilemap>() == null && (candidate.name.StartsWith("Route ", StringComparison.Ordinal)
                    || candidate.name == "Goal Platform" || candidate.name.StartsWith("Spawn", StringComparison.Ordinal)))
                    result.legacyRouteAnchors.Add(InspectPoint(candidate, "Legacy route anchor"));
            }
            foreach (MonoBehaviour component in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component == null || !component.gameObject.activeInHierarchy) continue;
                if (!(component is ContactCharger2D) && !(component is DistanceLauncher2D) && !(component is UniformField2D)
                    && !(component is VoltageLauncher2D) && !(component is ElectricStar2D) && !(component is ElectricGoal2D) && !(component is StageLever2D)) continue;
                Point point = InspectPoint(component.transform, component.GetType().Name);
                SerializedObject data = new SerializedObject(component);
                foreach (string field in new[] { "source", "restPoint", "contactPoint", "nearPoint", "farPoint", "lowerPlate", "upperPlate", "launchZone" })
                {
                    SerializedProperty property = data.FindProperty(field);
                    if (property == null || property.propertyType != SerializedPropertyType.ObjectReference) continue;
                    Component referenced = property.objectReferenceValue as Component;
                    if (referenced == null) continue;
                    point.references.Add(new ReferencePoint { field = field, name = referenced.name, path = HierarchyPath(referenced.transform),
                        worldPosition = referenced.transform.position, localPosition = referenced.transform.localPosition });
                }
                result.mechanismsStarsAndGoals.Add(point);
            }
        }
        return result;
    }
    private static Point InspectPoint(Transform transform, string kind)
    {
        var point = new Point { kind = kind, name = transform.name, path = HierarchyPath(transform), active = transform.gameObject.activeInHierarchy,
            tag = transform.tag, prefabAsset = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(transform.gameObject),
            localPosition = transform.localPosition, localScale = transform.localScale, worldPosition = transform.position, worldScale = transform.lossyScale };
        BoxCollider2D box = transform.GetComponent<BoxCollider2D>();
        if (box != null)
        {
            point.hasBox = true;
            Vector2 half = box.size * .5f;
            Bounds bounds = new Bounds(transform.TransformPoint(box.offset - half), Vector3.zero);
            bounds.Encapsulate(transform.TransformPoint(box.offset + new Vector2(half.x, -half.y)));
            bounds.Encapsulate(transform.TransformPoint(box.offset + new Vector2(-half.x, half.y)));
            bounds.Encapsulate(transform.TransformPoint(box.offset + half));
            point.boxWorldMin = bounds.min; point.boxWorldMax = bounds.max;
        }
        return point;
    }
    private static Bounds CellBounds(Tilemap map, Vector3Int cell)
    {
        Bounds bounds = new Bounds(map.CellToWorld(cell), Vector3.zero);
        bounds.Encapsulate(map.CellToWorld(cell + Vector3Int.right));
        bounds.Encapsulate(map.CellToWorld(cell + Vector3Int.up));
        bounds.Encapsulate(map.CellToWorld(cell + Vector3Int.right + Vector3Int.up));
        return bounds;
    }
    private static string HierarchyPath(Transform value)
    {
        string path = value.name;
        for (Transform parent = value.parent; parent != null; parent = parent.parent) path = parent.name + "/" + path;
        return path;
    }

    private static Capture RenderScene(int stage, bool includeUi)
    {
        Camera camera = Camera.main;
        if (camera == null) throw new InvalidOperationException("No active MainCamera in " + ScenePath(stage));
        CollapsiblePanel panel = Object.FindFirstObjectByType<CollapsiblePanel>();
        var content = panel != null ? new SerializedObject(panel).FindProperty("content").objectReferenceValue as UnityEngine.UI.LayoutElement : null;
        bool panelCollapsed = panel != null && !panel.IsOpen && content != null && Mathf.Abs(content.preferredHeight) < .01f;
        if (!panelCollapsed) throw new InvalidOperationException("Objective panel did not finish collapsing before capture.");
        RenderTexture previousTarget = camera.targetTexture, previousActive = RenderTexture.active;
        float previousAspect = camera.aspect;
        Rect previousRect = camera.rect;
        var states = new List<CanvasState>();
        RenderTexture target = null; Texture2D pixels = null;
        try
        {
            target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { name = "Temporary Saved Stage Capture", antiAliasing = 1, hideFlags = HideFlags.HideAndDontSave };
            target.Create(); camera.targetTexture = target; camera.aspect = Width / (float)Height; camera.rect = new Rect(0, 0, 1, 1);
            foreach (Canvas canvas in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (!canvas.isActiveAndEnabled || (includeUi && canvas.renderMode != RenderMode.ScreenSpaceOverlay)) continue;
                states.Add(new CanvasState { canvas = canvas, mode = canvas.renderMode, camera = canvas.worldCamera, planeDistance = canvas.planeDistance, enabled = canvas.enabled });
                if (includeUi) { canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = camera.nearClipPlane + .5f; }
                else canvas.enabled = false;
            }
            Canvas.ForceUpdateCanvases();
            if (GraphicsSettings.currentRenderPipeline == null) camera.Render();
            else if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset)
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            else throw new InvalidOperationException("Expected the existing URP or built-in render pipeline.");
            RenderTexture.active = target;
            pixels = new Texture2D(Width, Height, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave };
            pixels.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false); pixels.Apply(false, false);
            byte[] png = pixels.EncodeToPNG();
            if (png == null || png.Length < 1000) throw new InvalidOperationException("Camera render produced no usable PNG.");
            File.WriteAllBytes(CapturePath(stage, includeUi), png);
            return new Capture { stage = stage, scene = ScenePath(stage), path = CapturePath(stage, includeUi), canvasCount = states.Count,
                cameraPosition = camera.transform.position, orthographicSize = camera.orthographicSize, objectivePanelCollapsed = panelCollapsed,
                panelAnimationWaitSeconds = Time.unscaledTime - SessionState.GetFloat(Prefix + "ReadyUnscaledTime", 0), physicsFrozen = Mathf.Approximately(Time.timeScale, 0) };
        }
        finally
        {
            foreach (CanvasState state in states)
            {
                if (state.canvas == null) continue;
                state.canvas.renderMode = state.mode; state.canvas.worldCamera = state.camera; state.canvas.planeDistance = state.planeDistance; state.canvas.enabled = state.enabled;
            }
            camera.targetTexture = previousTarget; camera.aspect = previousAspect; camera.rect = previousRect; RenderTexture.active = previousActive;
            Canvas.ForceUpdateCanvases();
            if (pixels != null) Object.DestroyImmediate(pixels);
            if (target != null) { target.Release(); Object.DestroyImmediate(target); }
        }
    }
    private static CaptureReport Load() => File.Exists(CaptureReportPath) ? JsonUtility.FromJson<CaptureReport>(File.ReadAllText(CaptureReportPath)) : new CaptureReport();
    private static LayoutReport LoadLayout() => File.Exists(LayoutReportPath) ? JsonUtility.FromJson<LayoutReport>(File.ReadAllText(LayoutReportPath)) : new LayoutReport();
    private static void Save(CaptureReport report) => File.WriteAllText(CaptureReportPath, JsonUtility.ToJson(report, true));
    private static void SaveLayout(LayoutReport report) => File.WriteAllText(LayoutReportPath, JsonUtility.ToJson(report, true));
    private static void OnLog(string message, string stack, LogType type)
    {
        if (!SessionState.GetBool(Prefix + "Active", false) || (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)) return;
        CaptureReport report = Load(); report.errors.Add(message + "\n" + stack); Save(report);
    }
    private static void Fail(Exception error)
    {
        CaptureReport report = Load(); report.errors.Add(error.ToString()); Save(report); Finish();
    }
    private static void Finish()
    {
        SessionState.SetBool(Prefix + "Active", false);
        EditorApplication.update -= Tick; Application.logMessageReceived -= OnLog;
        Time.timeScale = 1;
        CaptureReport report = Load(); LayoutReport layout = LoadLayout();
        report.status = report.errors.Count == 0 && report.captures.Count == 5 && layout.stages.Count == 5 ? "passed" : "failed";
        layout.status = report.status; report.finishedUtc = layout.finishedUtc = DateTime.UtcNow.ToString("O");
        Save(report); SaveLayout(layout);
        SessionState.SetInt(Prefix + "ExitCode", report.status == "passed" ? 0 : 1);
        SessionState.SetBool(Prefix + "ExitPending", true);
        EditorApplication.update -= ExitWhenReady; EditorApplication.update += ExitWhenReady;
        if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
    }
    private static void ExitWhenReady()
    {
        if (!SessionState.GetBool(Prefix + "ExitPending", false)) { EditorApplication.update -= ExitWhenReady; return; }
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        SessionState.SetBool(Prefix + "ExitPending", false);
        EditorApplication.update -= ExitWhenReady;
        EditorApplication.Exit(SessionState.GetInt(Prefix + "ExitCode", 1));
    }
}
