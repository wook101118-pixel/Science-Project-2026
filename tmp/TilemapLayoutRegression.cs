// Temporary integration harness. Copy only to an isolated clone Assets/Editor.
// Unity -batchmode -projectPath <clone> -executeMethod TilemapLayoutRegression.RunBatch
// No -quit: the harness owns Play Mode and exits after saving its report.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Tilemaps;
using sung_wook;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class TilemapLayoutRegression
{
    private const string Prefix = "Codex.TilemapLayoutRegression.";
    private const string Output = "C:/Users/wook1/GitBub-Unity/Science-Project-2026/Logs/TilemapLayout/Regression.json";
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    [Serializable] private sealed class Check { public string name, details; public bool passed; }
    [Serializable] private sealed class Report
    {
        public string status = "running", startedUtc = DateTime.UtcNow.ToString("O"), finishedUtc;
        public string scope = "Isolated batch editor uses saved actual pause/result/menu Button events and shared asynchronous transitions. Result ShowStageResult is a fixture for navigation, not proof of a gameplay clear. Existing unlock preference is restored. Charge states use the actual ElectricBall2D API.";
        public List<Check> checks = new List<Check>();
        public List<string> errors = new List<string>(), sceneLoads = new List<string>(), captures = new List<string>();
    }
    private static Report report;
    private static Stack<IEnumerator> steps;
    private static double started;
    private static int serviceId;
    private static bool finishing, preferenceChanged, hadUnlock;
    private static int previousUnlock;

    static TilemapLayoutRegression()
    {
        if (!Application.isBatchMode) return;
        if (SessionState.GetBool(Prefix + "Active", false)) Subscribe();
        if (SessionState.GetBool(Prefix + "ExitPending", false)) EditorApplication.update += ExitWhenReady;
    }

    public static void RunBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Isolated batch editor required.");
        string project = Directory.GetParent(Application.dataPath).FullName.Replace('\\', '/');
        if (project != "C:/Users/wook1/AppData/Local/Temp/CodexUnityVisualBuild") throw new InvalidOperationException("Wrong validation project.");
        report = new Report(); Save();
        try
        {
            var enabled = new List<string>();
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes) if (scene.enabled) enabled.Add(scene.path);
            CheckResult("menu-is-first-enabled-build-scene", enabled.Count == 6 && enabled[0] == ElectricGameCompleteView.MainMenuPath, string.Join("; ", enabled));
            CheckResult("pause-and-result-share-menu-path", PauseMenuController.MainMenuPath == ElectricGameCompleteView.MainMenuPath, PauseMenuController.MainMenuPath);
            CheckResult("new-campaign-and-stage-one-share-path", ElectricGameCompleteView.FirstStagePath == ElectricStageProgression.StageScenePath(1), ElectricStageProgression.StageScenePath(1));
            for (int stage = 1; stage <= 5; stage++) CheckResult("stage-" + stage + "-build-path-exists", enabled.Contains(ElectricStageProgression.StageScenePath(stage)) && File.Exists(ElectricStageProgression.StageScenePath(stage)), ElectricStageProgression.StageScenePath(stage));
            SessionState.SetBool(Prefix + "Active", true);
            SessionState.SetBool(Prefix + "EnterRequested", false);
            SessionState.SetString(Prefix + "StartedUtc", DateTime.UtcNow.ToString("O"));
            EditorSceneManager.OpenScene(ElectricGameCompleteView.MainMenuPath, OpenSceneMode.Single);
            Subscribe();
        }
        catch (Exception error) { Finish(error); }
    }

    private static void Subscribe()
    {
        EditorApplication.update -= Tick; EditorApplication.update += Tick;
        Application.logMessageReceived -= OnLog; Application.logMessageReceived += OnLog;
        SceneManager.sceneLoaded -= OnSceneLoaded; SceneManager.sceneLoaded += OnSceneLoaded;
    }
    private static void Tick()
    {
        if (!SessionState.GetBool(Prefix + "Active", false) || finishing) return;
        try
        {
            if (report == null) report = JsonUtility.FromJson<Report>(File.ReadAllText(Output));
            if ((DateTime.UtcNow - DateTime.Parse(SessionState.GetString(Prefix + "StartedUtc", DateTime.UtcNow.ToString("O")))).TotalSeconds > 240d) throw new TimeoutException("Integration watchdog exceeded.");
            if (!EditorApplication.isPlaying)
            {
                if (!SessionState.GetBool(Prefix + "EnterRequested", false) && !EditorApplication.isCompiling && !EditorApplication.isUpdating && !EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    SessionState.SetBool(Prefix + "EnterRequested", true); EditorApplication.EnterPlaymode();
                }
                return;
            }
            if (EditorApplication.isPaused) return;
            if (steps == null) { started = EditorApplication.timeSinceStartup; steps = new Stack<IEnumerator>(); steps.Push(Workflow()); }
            if (report.errors.Count > 0) throw new InvalidOperationException("Unity reported runtime errors.");
            while (steps.Count > 0)
            {
                IEnumerator current = steps.Peek();
                if (!current.MoveNext()) { steps.Pop(); continue; }
                if (current.Current is IEnumerator nested) { steps.Push(nested); continue; }
                return;
            }
            Finish(null);
        }
        catch (Exception error) { Finish(error); }
    }

    private static IEnumerator Workflow()
    {
        yield return Ready(ElectricGameCompleteView.MainMenuPath, "initial-menu");
        serviceId = Service().GetInstanceID();
        for (int stage = 1; stage <= 5; stage++) CheckResult("stage-" + stage + "-runtime-loadable", Application.CanStreamedLevelBeLoaded(ElectricStageProgression.StageScenePath(stage)), ElectricStageProgression.StageScenePath(stage));
        Click(Find<MainMenuController>(), "startButton");
        yield return Ready(ElectricStageProgression.StageScenePath(1), "saved-start-button");
        for (int stage = 1; stage <= 5; stage++)
        {
            string stagePath = ElectricStageProgression.StageScenePath(stage);
            if (SceneManager.GetActiveScene().path != stagePath)
            {
                Require(SceneTransitionController.LoadScene(stagePath), "Stage fixture load rejected.");
                yield return Ready(stagePath, "stage-" + stage + "-fixture-load");
            }
            CheckResult("stage-" + stage + "-selector-is-correct", Find<ElectricStageSelector>().SelectedStage == stage, SceneManager.GetActiveScene().path);
            CheckWhiteHexBackground(stage);
            yield return CheckFixedElectrodes(stage);
            yield return CheckRoundVisual(stage);
            PauseMenuController pause = Find<PauseMenuController>(); pause.Pause();
            CheckResult("stage-" + stage + "-pause-opens", pause.IsPaused && Time.timeScale == 0f, "Saved pause controller owns the paused state.");
            Click(pause, "mainMenuButton");
            CheckResult("stage-" + stage + "-pause-menu-load-starts", SceneTransitionController.IsBusy && Time.timeScale == 0f, "Saved main-menu button starts shared unscaled transition.");
            yield return Ready(ElectricGameCompleteView.MainMenuPath, "stage-" + stage + "-pause-return");
            CheckResult("stage-" + stage + "-returned-menu-ready", !Find<MainMenuController>().IsSettingsOpen && !Find<MainMenuController>().IsPracticeOpen, "Fresh main menu opens without modal state.");
            Require(SceneTransitionController.LoadScene(stagePath), "Result fixture stage load rejected.");
            yield return Ready(stagePath, "stage-" + stage + "-result-fixture");
            ElectricGameCompleteView result = FindInScene<ElectricGameCompleteView>();
            result.ShowStageResult(stage, "Temporary navigation fixture");
            CheckResult("stage-" + stage + "-result-opens", result.IsResultOpen && Time.timeScale == 0f, "Fixture exercises the real saved result UI.");
            Click(result, "mainMenuButton");
            yield return Ready(ElectricGameCompleteView.MainMenuPath, "stage-" + stage + "-result-return");
        }
        hadUnlock = PlayerPrefs.HasKey(ElectricStageProgression.UnlockKey);
        previousUnlock = PlayerPrefs.GetInt(ElectricStageProgression.UnlockKey, 1);
        PlayerPrefs.SetInt(ElectricStageProgression.UnlockKey, 5); preferenceChanged = true;
        MainMenuController menu = Find<MainMenuController>();
        Click(menu, "practiceButton");
        Require(menu.PracticeStage(3), "Saved practice route rejected.");
        yield return Ready(ElectricStageProgression.StageScenePath(3), "practice-stage-three");
        ElectricGameCompleteView screen = FindInScene<ElectricGameCompleteView>(); screen.ShowStageResult(3, "Retry fixture");
        Click(screen, "retryCurrentButton");
        yield return Ready(ElectricStageProgression.StageScenePath(3), "saved-result-retry-current");
        screen = FindInScene<ElectricGameCompleteView>(); screen.ShowStageResult(3, "Continue fixture");
        Click(screen, "restartButton");
        yield return Ready(ElectricStageProgression.StageScenePath(4), "saved-result-next-stage");
        Require(SceneTransitionController.LoadScene(ElectricStageProgression.StageScenePath(5)), "Final stage fixture rejected.");
        yield return Ready(ElectricStageProgression.StageScenePath(5), "final-stage-fixture");
        screen = FindInScene<ElectricGameCompleteView>(); screen.ShowStageResult(5, "Restart campaign fixture");
        Click(screen, "restartButton");
        yield return Ready(ElectricStageProgression.StageScenePath(1), "saved-final-result-restarts-tutorial");
        CheckResult("all-navigation-flows-have-no-runtime-errors", report.errors.Count == 0, "Actual saved button bindings, scenes and transition service completed without error logs.");
    }

    private static void CheckWhiteHexBackground(int stage)
    {
        Tilemap background = null;
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            foreach (Tilemap map in root.GetComponentsInChildren<Tilemap>(true))
                if (map.name == "Background") background = map;
        Require(background != null, "Saved HexBackground/Background tilemap is missing.");
        int tiles = 0; foreach (TileBase tile in background.GetTilesBlock(background.cellBounds)) if (tile != null) tiles++;
        CheckResult("stage-" + stage + "-hex-background-is-white", Vector4.Distance(background.color, Color.white) < .001f, "Saved tilemap tint=" + background.color);
        int expectedTiles = stage == 1 ? 3102 : 2160;
        CheckResult("stage-" + stage + "-hex-background-keeps-authored-tiles", tiles == expectedTiles, "Actual occupied cells=" + tiles + ", saved authored count=" + expectedTiles);
        if (stage == 2) CapturePlayer(Find<ElectricBall2D>(), "Stage2-WhiteHexOverview", true);
    }

    private static IEnumerator CheckFixedElectrodes(int stage)
    {
        if (stage > 3) yield break;
        ElectricStageSelector selector = Find<ElectricStageSelector>();
        Transform source = stage == 3 ? Ref<Transform>(selector.GetStageComponent<DistanceLauncher2D>(3), "source")
            : selector.GetStageComponent<ContactCharger2D>(stage).transform;
        Require(source != null, "Saved fixed electrode source is missing.");
        Vector3 initial = source.position;
        Rigidbody2D sourceBody = source.GetComponent<Rigidbody2D>();
        Vector2 initialBody = sourceBody != null ? sourceBody.position : (Vector2)initial;
        foreach (float input in new[] { -1f, 0f, 1f, -.5f, .5f })
        {
            selector.Lever.SetSignedValue(input);
            float until = Time.realtimeSinceStartup + .16f; while (Time.realtimeSinceStartup < until) yield return null;
            CheckResult("stage-" + stage + "-electrode-fixed-at-lever-" + input, Vector3.Distance(source.position, initial) < .0001f && (sourceBody == null || Vector2.Distance(sourceBody.position, initialBody) < .0001f), "Input=" + input + ", initial=" + initial + ", actual=" + source.position);
        }
        selector.Lever.UseLeverInput();
    }

    private static IEnumerator CheckRoundVisual(int stage)
    {
        ElectricBall2D ball = Find<ElectricBall2D>();
        ContactCharger2D contact = stage <= 2 ? Find<ElectricStageSelector>().GetStageComponent<ContactCharger2D>(stage) : null;
        bool contactWasEnabled = contact != null && contact.enabled;
        // Keep the explicit visual fixture independent of a real nearby electrode recharging the ball.
        if (contact != null) contact.enabled = false;
        ChangePlayerVisual visual = ball.GetComponentInChildren<ChangePlayerVisual>(true);
        Require(visual != null, "Missing saved ChangePlayerVisual.");
        SpriteRenderer body = Ref<SpriteRenderer>(visual, "bodyRenderer");
        Sprite original = body != null ? body.sprite : null;
        Require(original != null, "Missing original circle sprite.");
        Vector3 originalScale = body.transform.lossyScale;
        CheckResult("stage-" + stage + "-circle-sprite-has-square-bounds", original.name == "Circle" && Mathf.Abs(original.bounds.size.x - original.bounds.size.y) < .001f && Mathf.Abs(Mathf.Abs(originalScale.x) - Mathf.Abs(originalScale.y)) < .001f, "Sprite=" + original.name + ", sprite bounds=" + original.bounds.size + ", world scale=" + originalScale);
        foreach (Polarity polarity in new[] { Polarity.Positive, Polarity.Neutral, Polarity.Negative })
        {
            ball.SetChargeMagnitude(1f); ball.SetPolarity(polarity);
            float until = Time.realtimeSinceStartup + .5f; while (Time.realtimeSinceStartup < until) yield return null;
            SpriteRenderer overlay = Ref<SpriteRenderer>(visual, "chargeRenderer");
            CheckResult("stage-" + stage + "-" + polarity + "-keeps-round-body", body.enabled && body.gameObject.activeInHierarchy && body.sprite == original && (overlay == null || !overlay.enabled), "Original body remains visible; rectangular charge texture overlay stays disabled; color=" + body.color);
            TMPro.TMP_Text[] labels = Ref<TMPro.TMP_Text[]>(visual, "neutralLabels");
            string expected = polarity == Polarity.Positive ? "+" : polarity == Polarity.Negative ? "-" : "0";
            CheckResult("stage-" + stage + "-" + polarity + "-shows-charge-symbol", labels != null && labels.Length > 0 && Array.TrueForAll(labels, text => text != null && text.enabled && text.text == expected), "Expected sign=" + expected);
            foreach (TMPro.TMP_Text text in labels) text.ForceMeshUpdate();
            CheckResult("stage-" + stage + "-" + polarity + "-charge-symbol-has-rendered-geometry", Array.TrueForAll(labels, text => text.textInfo.characterCount > 0 && text.textInfo.characterInfo[0].isVisible && text.textInfo.meshInfo.Length > 0 && text.textInfo.meshInfo[0].vertexCount >= 4), "The displayed sign has a visible rendered character quad.");
            CheckResult("stage-" + stage + "-" + polarity + "-visual-keeps-physical-polarity", ball.CurrentPolarity == polarity && Mathf.Abs(ball.SignedCharge - (float)polarity) < .001f, "Physical signed charge=" + ball.SignedCharge);
            Color expectedColor = RefColor(visual, polarity == Polarity.Positive ? "positiveColor" : polarity == Polarity.Negative ? "negativeColor" : "neutralColor");
            CheckResult("stage-" + stage + "-" + polarity + "-restores-original-color", Vector4.Distance(body.color, expectedColor) < .01f, "Expected=" + expectedColor + ", actual=" + body.color);
            if (stage == 1) CapturePlayer(ball, polarity.ToString());
        }
        ball.SetChargeMagnitude(0f); float zeroUntil = Time.realtimeSinceStartup + .28f; while (Time.realtimeSinceStartup < zeroUntil) yield return null;
        CheckResult("stage-" + stage + "-zero-magnitude-shows-zero", Array.TrueForAll(Ref<TMPro.TMP_Text[]>(visual, "neutralLabels"), text => text != null && text.enabled && text.text == "0"), "Same selected negative state with zero physical magnitude shows 0.");
        CheckResult("stage-" + stage + "-zero-magnitude-is-neutral-gray", Vector4.Distance(body.color, RefColor(visual, "neutralColor")) < .01f && ball.SignedCharge == 0f, "Zero actual charge restores the original gray circle.");
        ball.SetPolarity(Polarity.Positive); ball.SetChargeMagnitude(.5f); float halfUntil = Time.realtimeSinceStartup + .28f; while (Time.realtimeSinceStartup < halfUntil) yield return null;
        Color halfColor = Color.Lerp(RefColor(visual, "neutralColor"), RefColor(visual, "positiveColor"), .5f);
        CheckResult("stage-" + stage + "-half-charge-keeps-original-tint-interpolation", Vector4.Distance(body.color, halfColor) < .01f && Mathf.Abs(ball.SignedCharge - .5f) < .001f && body.sprite == original, "Expected=" + halfColor + ", actual=" + body.color);
        ball.SetNeutral();
        if (contact != null) contact.enabled = contactWasEnabled;
    }

    private static void CapturePlayer(ElectricBall2D ball, string label, bool overview = false)
    {
        Camera camera = Camera.main; if (camera == null) return;
        Vector3 previousPosition = camera.transform.position;
        float previousSize = camera.orthographicSize;
        RenderTexture previousTarget = camera.targetTexture, previousActive = RenderTexture.active;
        int width = overview ? 1280 : 512, height = overview ? 720 : 512;
        var target = new RenderTexture(width, height, 24);
        Texture2D pixels = null;
        try
        {
            if (!overview) { camera.transform.position = new Vector3(ball.transform.position.x, ball.transform.position.y, previousPosition.z); camera.orthographicSize = 1.8f; }
            camera.targetTexture = target; camera.Render();
            RenderTexture.active = target; pixels = new Texture2D(width, height, TextureFormat.RGBA32, false);
            pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0); pixels.Apply();
            string path = Path.Combine(Path.GetDirectoryName(Output), "Regression-" + label + ".png");
            File.WriteAllBytes(path, pixels.EncodeToPNG()); report.captures.Add(path); Save();
        }
        finally
        {
            camera.targetTexture = previousTarget; camera.transform.position = previousPosition; camera.orthographicSize = previousSize; RenderTexture.active = previousActive;
            if (pixels != null) Object.DestroyImmediate(pixels); target.Release(); Object.DestroyImmediate(target);
        }
    }

    private static IEnumerator Ready(string path, string label)
    {
        double deadline = EditorApplication.timeSinceStartup + 25d;
        while (SceneManager.GetActiveScene().path != path || SceneTransitionController.IsBusy || !SceneTransitionController.IsAvailable)
        {
            Require(EditorApplication.timeSinceStartup < deadline, label + " timed out; current=" + SceneManager.GetActiveScene().path); yield return null;
        }
        yield return null;
        SceneTransitionController service = Service(); CanvasGroup overlay = Ref<CanvasGroup>(service, "fadeOverlay");
        CheckResult(label + "-restores-ready-state", SceneManager.GetActiveScene().path == path && Time.timeScale == 1f && !SceneTransitionController.IsBusy && overlay.alpha <= .001f && !overlay.blocksRaycasts && Object.FindObjectsByType<SceneTransitionController>(FindObjectsSortMode.None).Length == 1 && (serviceId == 0 || service.GetInstanceID() == serviceId), "scene=" + path + ", timeScale=" + Time.timeScale + ", overlay=" + overlay.alpha + ", persistent service=" + service.GetInstanceID());
    }
    private static SceneTransitionController Service() => Object.FindFirstObjectByType<SceneTransitionController>();
    private static T Find<T>() where T : Object { T value = Object.FindFirstObjectByType<T>(); Require(value != null, "Missing active " + typeof(T).Name); return value; }
    private static T FindInScene<T>() where T : Component
    {
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects()) { T found = root.GetComponentInChildren<T>(true); if (found != null) return found; }
        throw new InvalidOperationException("Missing saved " + typeof(T).Name);
    }
    private static T Ref<T>(object target, string name) where T : class
    {
        for (Type type = target.GetType(); type != null; type = type.BaseType) { FieldInfo field = type.GetField(name, Fields); if (field != null) return field.GetValue(target) as T; }
        throw new InvalidOperationException("Missing field " + name);
    }
    private static Color RefColor(object target, string name) => (Color)target.GetType().GetField(name, Fields).GetValue(target);
    private static void Click(object target, string field)
    {
        Button button = Ref<Button>(target, field); Require(button != null && button.IsActive() && button.IsInteractable(), "Saved button unavailable: " + field); button.onClick.Invoke();
    }
    private static void CheckResult(string name, bool passed, string details) { report.checks.Add(new Check { name = name, passed = passed, details = details }); Save(); Require(passed, name + ": " + details); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Save() { Directory.CreateDirectory(Path.GetDirectoryName(Output)); File.WriteAllText(Output, JsonUtility.ToJson(report, true)); }
    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) { if (report != null) { report.sceneLoads.Add(scene.path); Save(); } }
    private static void OnLog(string message, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) { if (report == null) report = JsonUtility.FromJson<Report>(File.ReadAllText(Output)); report.errors.Add(message + "\n" + stack); Save(); } }
    private static void Finish(Exception error)
    {
        if (finishing) return; finishing = true;
        if (report == null) report = JsonUtility.FromJson<Report>(File.ReadAllText(Output));
        if (error != null) report.errors.Add(error.ToString());
        if (preferenceChanged) { if (hadUnlock) PlayerPrefs.SetInt(ElectricStageProgression.UnlockKey, previousUnlock); else PlayerPrefs.DeleteKey(ElectricStageProgression.UnlockKey); PlayerPrefs.Save(); }
        report.status = report.errors.Count == 0 && !report.checks.Exists(check => !check.passed) ? "passed" : "failed"; report.finishedUtc = DateTime.UtcNow.ToString("O"); Save();
        SessionState.SetBool(Prefix + "Active", false); SessionState.SetBool(Prefix + "ExitPending", true); SessionState.SetInt(Prefix + "ExitCode", report.status == "passed" ? 0 : 1);
        EditorApplication.update -= Tick; Application.logMessageReceived -= OnLog; SceneManager.sceneLoaded -= OnSceneLoaded;
        Time.timeScale = 1f; if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
        EditorApplication.update -= ExitWhenReady; EditorApplication.update += ExitWhenReady;
    }
    private static void ExitWhenReady()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        SessionState.SetBool(Prefix + "ExitPending", false);
        int code = SessionState.GetInt(Prefix + "ExitCode", 1);
        if (code == 0 && Array.IndexOf(Environment.GetCommandLineArgs(), "--build-after-tilemap-validation") >= 0)
        {
            EditorApplication.update -= ExitWhenReady;
            try { TilemapLayoutReleaseBuild.Build(); } catch (Exception error) { Debug.LogException(error); code = 1; }
        }
        EditorApplication.Exit(code);
    }
}


