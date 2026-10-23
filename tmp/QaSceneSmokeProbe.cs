// Temporary Development-player QA probe. Copy into the isolated build only.
// Inert unless BOTH --qa-scene-smoke and --qa-report=<absolute json path> are supplied.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using sung_wook;

public sealed class QaSceneSmokeProbe : MonoBehaviour
{
    private const string Menu = "Assets/@Project/@sung_wook/@Game/00_Scenes/MainMenu.unity";
    private const string Stages = "Assets/@Project/@sung_wook/@Game/00_Scenes/Stage/";
    [Serializable] private sealed class LogItem
    {
        public string scope, activeScene, type, message, stack;
        public float seconds;
    }
    [Serializable] private sealed class SceneResult
    {
        public string path, scope, status = "running", screenshot;
        public bool acceptedTransition, loaded, screenshotExists, developerConsoleWasVisible;
        public float sampleSeconds, elapsedSeconds, averageFrameMs, p95FrameMs, maxFrameMs;
        public float timeScale, playerX, playerY;
        public bool hasPlayer;
        public int frameCount, missingScriptCount, errorCount, width, height;
        public long managedBytesBefore, managedBytesAfter;
        public List<string> missingScriptObjects = new List<string>();
        public int diagnosticErrorCount;
        public List<ColliderDiagnostic> colliders = new List<ColliderDiagnostic>();
        public List<SpriteDiagnostic> sprites = new List<SpriteDiagnostic>();
    }
    [Serializable] private sealed class ColliderDiagnostic
    {
        public string hierarchy, type, tag, rigidbody, rigidbodyType, compositeOperation, diagnosticException;
        public bool enabled, activeInHierarchy, isTrigger, bodySimulated;
        public int shapeCount = -1, polygonPathCount = -1, compositePathCount = -1, compositePointCount = -1;
        public Vector3 boundsCenter, boundsSize;
        public Vector2 offset;
    }
    [Serializable] private sealed class SpriteDiagnostic
    {
        public string hierarchy, spriteName, textureName, shaderName, diagnosticException;
        public bool enabled, activeInHierarchy, hasSprite, textureReadable;
        public int physicsShapeCount = -1, textureWidth, textureHeight;
        public Vector3 boundsCenter, boundsSize;
    }
    [Serializable] private sealed class PrefValue
    {
        public string key, type;
        public bool exists;
        public int intValue;
        public float floatValue;
    }
    [Serializable] private sealed class Report
    {
        public string status = "running", startedUtc, finishedUtc, unity, graphicsDevice, graphicsApi;
        public string platform, os, cpu;
        public int processId, targetFrameRate = 60, vSyncCount = 0;
        public bool developmentBuild, focusedAtStart, preferencesUnchanged;
        public float elapsedSeconds;
        public int campaignErrorCount, legacyErrorCount, diagnosticErrorCount, missingCampaignScriptCount, missingLegacyScriptCount;
        public string scope = "실행 빌드에서 기존 공유 비동기 페이드로 씬을 불러온 뒤 입력·위치·속도·힘 변경 없이 관찰하는 idle smoke QA.";
        public string limitation = "Development 빌드, 현재 PC의 단일 GPU, 목표 60 FPS/vSync 0에서 각 캠페인 씬 3초와 기존 씬 1초의 짧은 baseline이다. 플레이 완주·최악 부하·다른 하드웨어·체감 조작감·장시간 메모리 누수 검증이 아니다. 전환과 캡처 정지는 프레임 표본에서 제외하며 GC.GetTotalMemory(false)는 관리 힙 추정치이고 전체 프로세스/GPU 메모리가 아니다.";
        public string colliderDiagnosticLimit = "Collider2D.shapeCount/Polygon.pathCount/Composite.pathCount와 Sprite.GetPhysicsShapeCount를 idle 관찰 후 읽는다. activeInHierarchy와 enabled가 모두 참이고 isTrigger가 거짓인 충돌체를 우선 평가한다. CompositeOperation이 Merge/Intersect 등인 원본 TilemapCollider.shapeCount=0은 단독 결함 근거가 아니며 CompositeCollider의 실제 shape/path와 함께 평가한다. Sprite.GetPhysicsShapeCount 자체가 Unity의 lazy outline 생성을 시도할 수 있으므로 해당 진단 중 발생한 오류는 diagnosticErrorCount로 별도 집계하며 자연 로드/플레이 오류와 섞지 않는다. inactive 장식 스프라이트나 물리와 무관한 스프라이트의 0 physics shape는 지형 충돌 누락을 입증하지 않는다.";
        public string screenshotNote = "실제 ScreenCapture 직전 Debug.developerConsoleVisible=false로 Development 전용 오류 콘솔 overlay만 닫고 한 프레임 뒤 캡처한다. 오류 기록은 유지하며 저장된 게임 UI/에셋/색상은 변경하지 않는다. 이 probe는 잘못된 씬 경로를 의도적으로 요청하지 않는다.";
        public List<SceneResult> scenes = new List<SceneResult>();
        public List<LogItem> errors = new List<LogItem>();
        public List<string> failures = new List<string>();
        public List<PrefValue> preferencesBefore = new List<PrefValue>();
        public List<PrefValue> preferencesAfter = new List<PrefValue>();
    }

    private Report report;
    private string reportPath, currentScope = "campaign-startup";
    private SceneResult current;
    private float started;
    private bool sampling, finishing, collectingPhysicsDiagnostics;
    private int previousTargetFrameRate, previousVSync;
    private bool previousRunInBackground;
    private readonly List<float> frames = new List<float>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Boot()
    {
        if (Application.isEditor) return;
        bool optedIn = false;
        string path = null;
        foreach (string arg in Environment.GetCommandLineArgs())
        {
            if (arg == "--qa-scene-smoke") optedIn = true;
            if (arg.StartsWith("--qa-report=", StringComparison.Ordinal)) path = arg.Substring("--qa-report=".Length);
        }
        if (!optedIn || string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)) return;
        var root = new GameObject("Temporary Scene Smoke QA");
        DontDestroyOnLoad(root);
        root.AddComponent<QaSceneSmokeProbe>().Initialize(path);
    }

    private void Initialize(string path)
    {
        reportPath = path;
        started = Time.realtimeSinceStartup;
        report = new Report {
            startedUtc = DateTime.UtcNow.ToString("O"), unity = Application.unityVersion,
            graphicsDevice = SystemInfo.graphicsDeviceName, graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
            platform = Application.platform.ToString(), os = SystemInfo.operatingSystem, cpu = SystemInfo.processorType,
            processId = System.Diagnostics.Process.GetCurrentProcess().Id,
            developmentBuild = Debug.isDebugBuild, focusedAtStart = Application.isFocused
        };
        report.preferencesBefore = SnapshotPreferences();
        previousTargetFrameRate = Application.targetFrameRate;
        previousVSync = QualitySettings.vSyncCount;
        previousRunInBackground = Application.runInBackground;
        Application.targetFrameRate = 60;
        QualitySettings.vSyncCount = 0;
        Application.runInBackground = true;
        Application.logMessageReceived += OnLog;
        Save();
        StartCoroutine(Run());
    }

    private void Update()
    {
        if (finishing || report == null) return;
        if (sampling && Time.unscaledDeltaTime > 0f) frames.Add(Time.unscaledDeltaTime * 1000f);
        if (Time.realtimeSinceStartup - started > 120f)
        {
            report.failures.Add("120초 smoke watchdog 초과: " + currentScope);
            Finish();
        }
    }

    private IEnumerator Run()
    {
        // Observe saved first scene startup before making any transition request.
        yield return null;
        yield return Observe(Menu, false, 3f, 0);
        for (int stage = 2; stage <= 5; stage++)
            yield return Observe(Stages + "Stage_" + stage.ToString("00") + ".unity", false, 3f, stage - 1);
        yield return Observe(Stages + "Stage_01.unity", true, 1f, 5);
        yield return Observe("Assets/@Project/@apple_wine/@Game/Scenes/apple_wine.unity", true, 1f, 6);
        currentScope = "campaign-return-main-menu";
        current = null;
        float wait = Time.realtimeSinceStartup;
        while (SceneTransitionController.IsBusy && Time.realtimeSinceStartup - wait < 12f) yield return null;
        if (!SceneTransitionController.LoadScene(Menu)) report.failures.Add("공유 전환 관리자가 메인 메뉴 복귀를 거부함.");
        wait = Time.realtimeSinceStartup;
        while ((SceneManager.GetActiveScene().path != Menu || SceneTransitionController.IsBusy)
            && Time.realtimeSinceStartup - wait < 15f) yield return null;
        if (SceneManager.GetActiveScene().path != Menu || SceneTransitionController.IsBusy)
            report.failures.Add("레거시 씬 관찰 뒤 메인 메뉴 복귀 타임아웃.");
        yield return null;
        Finish();
    }

    private IEnumerator Observe(string path, bool legacy, float seconds, int number)
    {
        currentScope = legacy ? "legacy" : "campaign";
        current = new SceneResult { path = path, scope = currentScope, sampleSeconds = seconds };
        SceneResult result = current;
        report.scenes.Add(result);
        Save();
        float sceneStarted = Time.realtimeSinceStartup;
        float wait = sceneStarted;
        while (SceneTransitionController.IsBusy && Time.realtimeSinceStartup - wait < 12f) yield return null;
        if (SceneManager.GetActiveScene().path == path)
            result.acceptedTransition = true;
        else
            result.acceptedTransition = SceneTransitionController.LoadScene(path);
        if (!result.acceptedTransition)
        {
            result.status = "transition-rejected";
            if (!legacy) report.failures.Add("캠페인 씬 전환 거부: " + path);
            Save();
            yield break;
        }
        wait = Time.realtimeSinceStartup;
        while ((SceneManager.GetActiveScene().path != path || SceneTransitionController.IsBusy)
            && Time.realtimeSinceStartup - wait < 15f) yield return null;
        result.loaded = SceneManager.GetActiveScene().path == path && !SceneTransitionController.IsBusy;
        if (!result.loaded)
        {
            result.status = "load-timeout";
            if (!legacy) report.failures.Add("캠페인 씬 로드 타임아웃: " + path);
            Save();
            yield break;
        }
        // Let startup, component Start, shader setup and initial fade settle before sampling idle frames.
        wait = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - wait < 0.5f) yield return null;
        result.managedBytesBefore = GC.GetTotalMemory(false);
        frames.Clear();
        sampling = true;
        wait = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - wait < seconds) yield return null;
        sampling = false;
        result.managedBytesAfter = GC.GetTotalMemory(false);
        result.frameCount = frames.Count;
        if (frames.Count > 0)
        {
            float total = 0f;
            foreach (float value in frames) total += value;
            frames.Sort();
            result.averageFrameMs = total / frames.Count;
            result.p95FrameMs = frames[Mathf.Clamp(Mathf.CeilToInt(frames.Count * 0.95f) - 1, 0, frames.Count - 1)];
            result.maxFrameMs = frames[frames.Count - 1];
        }
        result.timeScale = Time.timeScale;
        result.width = Screen.width;
        result.height = Screen.height;
        Scene scene = SceneManager.GetActiveScene();
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                int missing = 0;
                foreach (Component component in child.GetComponents<Component>()) if (component == null) missing++;
                if (missing == 0) continue;
                result.missingScriptCount += missing;
                result.missingScriptObjects.Add(HierarchyPath(child) + " (" + missing + ")");
            }
        ElectricBall2D player = UnityEngine.Object.FindFirstObjectByType<ElectricBall2D>();
        result.hasPlayer = player != null;
        if (player != null) { result.playerX = player.transform.position.x; result.playerY = player.transform.position.y; }
        CapturePhysicsDiagnostics(scene, result);
        string file = "QaSceneSmoke_" + report.processId + "_" + number.ToString("00") + "_" + Path.GetFileNameWithoutExtension(path) + ".png";
        result.screenshot = Path.Combine(Path.GetDirectoryName(reportPath), file);
        result.developerConsoleWasVisible = Debug.developerConsoleVisible;
        Debug.developerConsoleVisible = false;
        yield return null;
        yield return new WaitForEndOfFrame();
        ScreenCapture.CaptureScreenshot(result.screenshot);
        // Wait for the asynchronous screenshot writer rather than accepting a file in the request frame.
        int captureFrame = Time.frameCount;
        while (Time.frameCount < captureFrame + 3) yield return null;
        wait = Time.realtimeSinceStartup;
        while (!File.Exists(result.screenshot) && Time.realtimeSinceStartup - wait < 3f) yield return null;
        result.screenshotExists = File.Exists(result.screenshot) && new FileInfo(result.screenshot).Length > 0;
        result.elapsedSeconds = Time.realtimeSinceStartup - sceneStarted;
        result.status = result.errorCount == 0 && result.missingScriptCount == 0 && result.screenshotExists && result.frameCount > 0 ? "passed" : "observed-issues";
        if (!legacy && !result.screenshotExists) report.failures.Add("실제 화면 캡처 누락: " + path);
        if (!legacy && result.frameCount == 0) report.failures.Add("idle 프레임 표본 없음: " + path);
        Save();
    }

    private static string HierarchyPath(Transform value)
    {
        string path = value.name;
        for (Transform parent = value.parent; parent != null; parent = parent.parent) path = parent.name + "/" + path;
        return path;
    }

    private void CapturePhysicsDiagnostics(Scene scene, SceneResult result)
    {
        collectingPhysicsDiagnostics = true;
        try
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Collider2D collider in root.GetComponentsInChildren<Collider2D>(true))
                {
                    var diagnostic = new ColliderDiagnostic {
                        hierarchy = HierarchyPath(collider.transform), type = collider.GetType().Name,
                        tag = collider.tag, enabled = collider.enabled, activeInHierarchy = collider.gameObject.activeInHierarchy,
                        isTrigger = collider.isTrigger, boundsCenter = collider.bounds.center, boundsSize = collider.bounds.size,
                        offset = collider.offset, compositeOperation = collider.compositeOperation.ToString()
                    };
                    try
                    {
                        diagnostic.shapeCount = collider.shapeCount;
                        if (collider is PolygonCollider2D polygon) diagnostic.polygonPathCount = polygon.pathCount;
                        if (collider is CompositeCollider2D composite) {
                            diagnostic.compositePathCount = composite.pathCount;
                            diagnostic.compositePointCount = composite.pointCount;
                        }
                        Rigidbody2D body = collider.attachedRigidbody;
                        if (body != null) {
                            diagnostic.rigidbody = body.name; diagnostic.rigidbodyType = body.bodyType.ToString();
                            diagnostic.bodySimulated = body.simulated;
                        }
                    }
                    catch (Exception exception) { diagnostic.diagnosticException = exception.ToString(); }
                    result.colliders.Add(diagnostic);
                }
                foreach (SpriteRenderer renderer in root.GetComponentsInChildren<SpriteRenderer>(true))
                {
                    var diagnostic = new SpriteDiagnostic {
                        hierarchy = HierarchyPath(renderer.transform), enabled = renderer.enabled,
                        activeInHierarchy = renderer.gameObject.activeInHierarchy,
                        boundsCenter = renderer.bounds.center, boundsSize = renderer.bounds.size,
                        hasSprite = renderer.sprite != null,
                        shaderName = renderer.sharedMaterial != null && renderer.sharedMaterial.shader != null
                            ? renderer.sharedMaterial.shader.name : null
                    };
                    try
                    {
                        Sprite sprite = renderer.sprite;
                        if (sprite != null) {
                            diagnostic.spriteName = sprite.name;
                            Texture2D texture = sprite.texture;
                            if (texture != null) {
                                diagnostic.textureName = texture.name; diagnostic.textureReadable = texture.isReadable;
                                diagnostic.textureWidth = texture.width; diagnostic.textureHeight = texture.height;
                            }
                            diagnostic.physicsShapeCount = sprite.GetPhysicsShapeCount();
                        }
                    }
                    catch (Exception exception) { diagnostic.diagnosticException = exception.ToString(); }
                    result.sprites.Add(diagnostic);
                }
            }
        }
        finally { collectingPhysicsDiagnostics = false; }
    }

    private void OnLog(string message, string stack, LogType type)
    {
        if (finishing || (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)) return;
        report.errors.Add(new LogItem { scope = collectingPhysicsDiagnostics ? currentScope + "-physics-diagnostic" : currentScope, activeScene = SceneManager.GetActiveScene().path,
            type = type.ToString(), message = message, stack = stack, seconds = Time.realtimeSinceStartup - started });
        if (collectingPhysicsDiagnostics) {
            report.diagnosticErrorCount++;
            if (current != null) current.diagnosticErrorCount++;
            return;
        }
        if (current != null) current.errorCount++;
        if (currentScope == "legacy") report.legacyErrorCount++; else report.campaignErrorCount++;
    }

    private static List<PrefValue> SnapshotPreferences()
    {
        var values = new List<PrefValue>();
        foreach (string key in new[] { DisplayPreferences.WidthKey, DisplayPreferences.HeightKey, DisplayPreferences.FullscreenKey, SfxPreferences.MuteKey })
            values.Add(new PrefValue { key = key, type = "int", exists = PlayerPrefs.HasKey(key), intValue = PlayerPrefs.GetInt(key) });
        values.Add(new PrefValue { key = SfxPreferences.VolumeKey, type = "float", exists = PlayerPrefs.HasKey(SfxPreferences.VolumeKey), floatValue = PlayerPrefs.GetFloat(SfxPreferences.VolumeKey) });
        return values;
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
        File.WriteAllText(reportPath, JsonUtility.ToJson(report, true));
    }

    private void Finish()
    {
        if (finishing) return;
        finishing = true;
        sampling = false;
        StopAllCoroutines();
        Application.logMessageReceived -= OnLog;
        report.preferencesAfter = SnapshotPreferences();
        report.preferencesUnchanged = JsonUtility.ToJson(new PrefWrapper { values = report.preferencesBefore })
            == JsonUtility.ToJson(new PrefWrapper { values = report.preferencesAfter });
        if (!report.preferencesUnchanged) report.failures.Add("관찰 중 자체 디스플레이/SFX PlayerPrefs가 변경됨.");
        foreach (SceneResult scene in report.scenes)
            if (scene.scope == "legacy") report.missingLegacyScriptCount += scene.missingScriptCount;
            else report.missingCampaignScriptCount += scene.missingScriptCount;
        report.finishedUtc = DateTime.UtcNow.ToString("O");
        report.elapsedSeconds = Time.realtimeSinceStartup - started;
        bool passed = report.failures.Count == 0 && report.campaignErrorCount == 0 && report.missingCampaignScriptCount == 0;
        report.status = !passed ? "failed" : report.legacyErrorCount > 0 || report.missingLegacyScriptCount > 0 ? "passed-with-legacy-issues" : "passed";
        Application.targetFrameRate = previousTargetFrameRate;
        QualitySettings.vSyncCount = previousVSync;
        Application.runInBackground = previousRunInBackground;
        Save();
        Application.Quit(passed ? 0 : 1);
    }
    [Serializable] private sealed class PrefWrapper { public List<PrefValue> values; }
}
