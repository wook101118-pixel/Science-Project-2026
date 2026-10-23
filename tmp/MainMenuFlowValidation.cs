// Temporary integration harness. Copy to Assets/Editor only in an isolated Unity project.
// Run: Unity.exe -batchmode -projectPath <clone> -executeMethod MainMenuFlowValidation.RunBatch
// Do not pass -quit: the saved menu Quit button is exercised last, then the harness exits Unity.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using sung_wook;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class MainMenuFlowValidation
{
    private const string Prefix = "Codex.MainMenuFlowValidation.";
    private const string MenuPath = "Assets/@Project/@sung_wook/@Game/00_Scenes/MainMenu.unity";
    private const string Stage2Path = "Assets/@Project/@sung_wook/@Game/00_Scenes/Stage/Stage_02.unity";
    private const string Stage5Path = "Assets/@Project/@sung_wook/@Game/00_Scenes/Stage/Stage_05.unity";
    private const string DefaultOutput = "C:/Users/wook1/GitBub-Unity/Science-Project-2026/Logs";

    private enum Phase { EnterPlay, InitialReveal, Settings, FirstStart, LoadStage5, RestartFromComplete, ReturnMenu, QuitRequested }

    [Serializable] private sealed class Check
    {
        public string name;
        public bool passed;
        public string details;
    }

    [Serializable] private sealed class SceneLoad
    {
        public string path;
        public bool transitionBusy;
        public float overlayAlpha;
        public float timeScale;
    }

    [Serializable] private sealed class Report
    {
        public string status = "running";
        public string mode = "Actual saved menu buttons, asynchronous sceneLoaded callbacks and saved completion restart UI in isolated Play Mode. No gameplay or physics-clear claims; no asset or scene saves.";
        public List<Check> checks = new List<Check>();
        public List<SceneLoad> sceneLoads = new List<SceneLoad>();
        public List<string> errors = new List<string>();
    }

    static MainMenuFlowValidation()
    {
        if (!Application.isBatchMode) return;
        if (SessionState.GetBool(Prefix + "Active", false)) Resume();
        if (SessionState.GetBool(Prefix + "Exiting", false)) EditorApplication.update += ExitWhenReady;
    }

    private static string ReportPath
    {
        get
        {
            string folder = Environment.GetEnvironmentVariable("CODEX_MENU_VALIDATION_OUTPUT");
            return Path.Combine(string.IsNullOrWhiteSpace(folder) ? DefaultOutput : Path.GetFullPath(folder), "MainMenuFlowValidation.json");
        }
    }

    public static void RunBatch()
    {
        if (!Application.isBatchMode)
        {
            Debug.LogWarning("Menu validation must run in a separately launched isolated batch editor.");
            return;
        }
        Save(new Report());
        try
        {
            SessionState.SetBool(Prefix + "Exiting", false);
            SessionState.SetBool(Prefix + "Active", true);
            SessionState.SetString(Prefix + "StartedUtc", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            SessionState.SetInt(Prefix + "Phase", (int)Phase.EnterPlay);
            SessionState.SetBool(Prefix + "EnterRequested", false);
            SessionState.SetBool(Prefix + "QuitExitObserved", false);
            SessionState.SetBool(Prefix + "InitialIntermediate", false);
            SessionState.SetInt(Prefix + "ServiceInstance", 0);
            SessionState.SetString(Prefix + "ExpectedScene", "");
            var enabled = new List<string>();
            foreach (EditorBuildSettingsScene entry in EditorBuildSettings.scenes)
                if (entry.enabled) enabled.Add(entry.path);
            CheckResult("main-menu-is-first-enabled-build-scene", enabled.Count > 0 && enabled[0] == MenuPath, string.Join("; ", enabled));
            CheckResult("first-and-final-stages-remain-enabled", enabled.Contains(Stage2Path) && enabled.Contains(Stage5Path), "Stage 02 and Stage 05 are registered.");
            EditorSceneManager.OpenScene(MenuPath, OpenSceneMode.Single);
            Resume();
        }
        catch (Exception error) { Fail(error); }
    }

    private static void Resume()
    {
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(Prefix + "Active", false)) return;
        try
        {
            DateTime started = DateTime.Parse(SessionState.GetString(Prefix + "StartedUtc", ""), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            Require((DateTime.UtcNow - started).TotalSeconds < 180d, "Menu flow exceeded the 180-second real-time watchdog.");
            Require(Load().errors.Count == 0, "A Unity error was logged during menu verification.");
            Phase phase = (Phase)SessionState.GetInt(Prefix + "Phase", 0);
            if (!EditorApplication.isPlaying)
            {
                if (phase == Phase.EnterPlay && !SessionState.GetBool(Prefix + "EnterRequested", false)
                    && !EditorApplication.isCompiling && !EditorApplication.isUpdating && !EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    SessionState.SetBool(Prefix + "EnterRequested", true);
                    EditorApplication.EnterPlaymode();
                }
                return;
            }
            if (EditorApplication.isPaused) return;
            if (phase == Phase.EnterPlay) { SetPhase(Phase.InitialReveal); phase = Phase.InitialReveal; }
            if (phase == Phase.QuitRequested) return;

            SceneTransitionController service = Service();
            if (service == null) return;
            CanvasGroup overlay = Reference<CanvasGroup>(service, "fadeOverlay");
            Require(overlay != null, "Saved transition prefab has no Fade Overlay reference.");
            if (phase == Phase.InitialReveal)
            {
                if (SceneTransitionController.IsBusy)
                {
                    if (overlay.alpha > 0.01f && overlay.alpha < 0.99f) SessionState.SetBool(Prefix + "InitialIntermediate", true);
                    Require(Time.timeScale == 0f && overlay.blocksRaycasts, "Initial fade must pause physics and intercept UI input.");
                    return;
                }
                if (!Reference<Button>(Menu(), "settingsButton").IsInteractable()) return;
                CheckResult("initial-menu-fade-reveals-through-intermediate-alpha", SessionState.GetBool(Prefix + "InitialIntermediate", false), "Observed alpha between 0 and 1 during the initial reveal.");
                CheckReadyState("initial-menu-ready", service, overlay, MenuPath);
                SessionState.SetInt(Prefix + "ServiceInstance", service.GetInstanceID());
                MainMenuController menu = Menu();
                foreach (string field in new[] { "startButton", "settingsButton", "quitButton", "closeSettingsButton" })
                {
                    Button button = Reference<Button>(menu, field);
                    Image image = button != null ? button.targetGraphic as Image : null;
                    CheckResult(field + "-keeps-original-sprite-colors", image != null && image.color == Color.white
                        && button.transition == Selectable.Transition.None, "Image tint is white and selectable color transitions are disabled.");
                }
                Click(Reference<Button>(menu, "settingsButton"));
                SetPhase(Phase.Settings);
                return;
            }
            if (phase == Phase.Settings)
            {
                MainMenuController menu = Menu();
                GameObject settings = Reference<GameObject>(menu, "settingsWindow");
                GameObject main = Reference<GameObject>(menu, "mainContent");
                Transform content = settings != null ? settings.transform.Find("Content") : null;
                CheckResult("settings-button-opens-empty-window", menu.IsSettingsOpen && settings != null && settings.activeInHierarchy
                    && main != null && !main.activeSelf && content != null && content.childCount == 0,
                    "Settings Content contains no options or child objects.");
                Click(Reference<Button>(menu, "closeSettingsButton"));
                CheckResult("settings-close-button-returns-to-menu", !menu.IsSettingsOpen && main.activeInHierarchy && !settings.activeSelf,
                    "The saved close button restores the main menu.");
                PrepareLoad(Stage2Path);
                Button start = Reference<Button>(menu, "startButton");
                Click(start);
                AsyncOperation first = Reference<AsyncOperation>(service, "pendingLoad");
                CheckResult("start-button-begins-async-load-immediately", SceneTransitionController.IsBusy && Time.timeScale == 0f
                    && overlay.blocksRaycasts && first != null && !first.allowSceneActivation
                    && SceneManager.GetActiveScene().path == MenuPath, "Busy is set before returning and activation waits behind the fade.");
                start.onClick.Invoke();
                CheckResult("same-frame-double-start-reuses-one-operation", Reference<AsyncOperation>(service, "pendingLoad") == first,
                    "Invoking the saved Start button twice does not replace the pending asynchronous operation.");
                SetPhase(Phase.FirstStart);
                return;
            }
            if (phase == Phase.FirstStart)
            {
                if (!AwaitLoad(service, overlay)) return;
                Report report = Load();
                int count = report.sceneLoads.FindAll(entry => entry.path == Stage2Path).Count;
                CheckResult("double-start-loads-stage-02-once", count == 1, "Observed Stage 02 sceneLoaded callback count=" + count + ".");
                CheckResult("first-stage-bootstraps-naturally", Object.FindFirstObjectByType<ElectricStageSceneBootstrap>()?.StageNumber == 2
                    && Object.FindFirstObjectByType<ElectricBall2D>() != null, "Actual saved Stage 02 gameplay objects initialized.");
                PrepareLoad(Stage5Path);
                CheckResult("final-stage-request-is-accepted", SceneTransitionController.LoadScene(Stage5Path), "Prepare saved completion restart UI; this does not claim a gameplay clear.");
                SetPhase(Phase.LoadStage5);
                return;
            }
            if (phase == Phase.LoadStage5)
            {
                if (!AwaitLoad(service, overlay)) return;
                ElectricGameCompleteView completion = FindInActiveScene<ElectricGameCompleteView>();
                Require(completion != null, "Saved Stage 05 has no completion view.");
                completion.Show();
                CheckResult("saved-completion-view-pauses-gameplay", completion.gameObject.activeInHierarchy && Time.timeScale == 0f,
                    "Show() is invoked solely to exercise its real restart-button wiring from a paused screen.");
                PrepareLoad(Stage2Path);
                Click(Reference<Button>(completion, "restartButton"));
                CheckResult("completion-restart-starts-async-while-paused", SceneTransitionController.IsBusy && Time.timeScale == 0f
                    && Reference<AsyncOperation>(service, "pendingLoad") != null, "Saved Restart button uses the shared unscaled transition.");
                SetPhase(Phase.RestartFromComplete);
                return;
            }
            if (phase == Phase.RestartFromComplete)
            {
                if (!AwaitLoad(service, overlay)) return;
                CheckResult("restart-restores-fresh-first-stage", Object.FindFirstObjectByType<ElectricBall2D>()?.Stars == 0
                    && Time.timeScale == 1f, "First-stage state is fresh and the previous completion pause does not persist.");
                PrepareLoad(MenuPath);
                CheckResult("menu-return-request-is-accepted", SceneTransitionController.LoadScene(MenuPath), "Return via the same asynchronous transition service.");
                SetPhase(Phase.ReturnMenu);
                return;
            }
            if (phase == Phase.ReturnMenu)
            {
                if (!AwaitLoad(service, overlay)) return;
                MainMenuController menu = Menu();
                CheckResult("returning-menu-resets-settings-state", !menu.IsSettingsOpen
                    && Reference<GameObject>(menu, "mainContent").activeInHierarchy, "The saved menu initializes normally on return.");
                SetPhase(Phase.QuitRequested);
                CheckResult("quit-button-is-wired", Reference<Button>(menu, "quitButton") != null, "Invoke the saved Quit button only inside the isolated editor.");
                Click(Reference<Button>(menu, "quitButton"));
            }
        }
        catch (Exception error) { Fail(error); }
    }

    private static void PrepareLoad(string target)
    {
        SessionState.SetString(Prefix + "ExpectedScene", target);
        SessionState.SetString(Prefix + "OriginalScene", SceneManager.GetActiveScene().path);
        SessionState.SetBool(Prefix + "FadeOutIntermediate", false);
        SessionState.SetBool(Prefix + "FadeInIntermediate", false);
        SessionState.SetBool(Prefix + "ExpectedSceneLoaded", false);
    }

    private static bool AwaitLoad(SceneTransitionController service, CanvasGroup overlay)
    {
        string expected = SessionState.GetString(Prefix + "ExpectedScene", "");
        string current = SceneManager.GetActiveScene().path;
        if (SceneTransitionController.IsBusy)
        {
            Require(Time.timeScale == 0f && overlay.blocksRaycasts, "Transition must pause physics and intercept UI until reveal completes.");
            if (overlay.alpha > 0.01f && overlay.alpha < 0.99f)
                SessionState.SetBool(Prefix + (current == expected ? "FadeInIntermediate" : "FadeOutIntermediate"), true);
            return false;
        }
        // The menu updates button interactability in Update, after the fade coroutine releases busy.
        if (expected == MenuPath && !Reference<Button>(Menu(), "quitButton").IsInteractable()) return false;
        CheckReadyState("loaded-scene-ready-" + Path.GetFileNameWithoutExtension(expected), service, overlay, expected);
        CheckResult("fade-out-and-in-observed-" + Path.GetFileNameWithoutExtension(expected),
            SessionState.GetBool(Prefix + "FadeOutIntermediate", false) && SessionState.GetBool(Prefix + "FadeInIntermediate", false),
            "Observed intermediate fade alpha in both the outgoing and incoming scenes.");
        CheckResult("actual-sceneLoaded-received-" + Path.GetFileNameWithoutExtension(expected),
            SessionState.GetBool(Prefix + "ExpectedSceneLoaded", false), expected);
        return true;
    }

    private static void CheckReadyState(string name, SceneTransitionController service, CanvasGroup overlay, string expected)
    {
        SceneTransitionController[] all = Object.FindObjectsByType<SceneTransitionController>(FindObjectsSortMode.None);
        int original = SessionState.GetInt(Prefix + "ServiceInstance", service.GetInstanceID());
        if (original == 0) original = service.GetInstanceID();
        CheckResult(name, SceneManager.GetActiveScene().path == expected && !SceneTransitionController.IsBusy
            && overlay.alpha == 0f && !overlay.blocksRaycasts && Time.timeScale == 1f
            && all.Length == 1 && service.GetInstanceID() == original && service.gameObject.scene.name == "DontDestroyOnLoad",
            "scene=" + SceneManager.GetActiveScene().path + ", alpha=" + overlay.alpha + ", timeScale=" + Time.timeScale
                + ", serviceCount=" + all.Length + ", persistentInstance=" + service.GetInstanceID() + ".");
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!SessionState.GetBool(Prefix + "Active", false) || !EditorApplication.isPlaying) return;
        SceneTransitionController service = Service();
        CanvasGroup overlay = service != null ? Reference<CanvasGroup>(service, "fadeOverlay") : null;
        Report report = Load();
        report.sceneLoads.Add(new SceneLoad { path = scene.path, transitionBusy = SceneTransitionController.IsBusy,
            overlayAlpha = overlay != null ? overlay.alpha : -1f, timeScale = Time.timeScale });
        Save(report);
        if (scene.path != SessionState.GetString(Prefix + "ExpectedScene", "")) return;
        SessionState.SetBool(Prefix + "ExpectedSceneLoaded", true);
        CheckResult("incoming-scene-activates-behind-opaque-fade-" + Path.GetFileNameWithoutExtension(scene.path),
            overlay != null && overlay.alpha >= 0.999f && Time.timeScale == 0f && SceneTransitionController.IsBusy,
            "Observed in the actual SceneManager.sceneLoaded callback before the incoming reveal.");
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Prefix + "Active", false) || state != PlayModeStateChange.EnteredEditMode) return;
        bool requested = (Phase)SessionState.GetInt(Prefix + "Phase", 0) == Phase.QuitRequested;
        SessionState.SetBool(Prefix + "QuitExitObserved", requested);
        try
        {
            CheckResult("quit-button-exits-editor-play-mode", requested, "Observed EditorApplication.playModeStateChanged: EnteredEditMode after saved Quit button invocation.");
            Finish();
        }
        catch (Exception error) { Fail(error); }
    }

    private static SceneTransitionController Service()
    {
        foreach (SceneTransitionController service in Object.FindObjectsByType<SceneTransitionController>(FindObjectsSortMode.None))
            if (service.gameObject.scene.name == "DontDestroyOnLoad") return service;
        return Object.FindFirstObjectByType<SceneTransitionController>();
    }

    private static MainMenuController Menu()
    {
        MainMenuController menu = Object.FindFirstObjectByType<MainMenuController>();
        Require(menu != null, "Saved menu has no MainMenuController.");
        return menu;
    }

    private static T FindInActiveScene<T>() where T : Component
    {
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            T found = root.GetComponentInChildren<T>(true);
            if (found != null) return found;
        }
        return null;
    }

    private static T Reference<T>(object owner, string field) where T : class
    {
        FieldInfo info = owner?.GetType().GetField(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Require(info != null, "Missing saved field " + field + ".");
        return info.GetValue(owner) as T;
    }

    private static void Click(Button button)
    {
        Require(button != null && button.IsActive() && button.IsInteractable() && EventSystem.current != null,
            "Saved button is not available for clicking: " + (button != null ? button.name : "missing"));
        button.onClick.Invoke();
    }

    private static void SetPhase(Phase phase) => SessionState.SetInt(Prefix + "Phase", (int)phase);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static Report Load() => JsonUtility.FromJson<Report>(File.ReadAllText(ReportPath));
    private static void Save(Report report)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
        File.WriteAllText(ReportPath, JsonUtility.ToJson(report, true));
    }
    private static void CheckResult(string name, bool passed, string details)
    {
        Report report = Load();
        report.checks.Add(new Check { name = name, passed = passed, details = details });
        Save(report);
        Require(passed, "Failed check: " + name + ". " + details);
    }
    private static void OnLog(string message, string stack, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        Report report = Load();
        report.errors.Add(message + "\n" + stack);
        Save(report);
    }
    private static void Fail(Exception error)
    {
        Report report = Load();
        report.errors.Add(error.ToString());
        Save(report);
        Finish();
    }
    private static void Finish()
    {
        if (!SessionState.GetBool(Prefix + "Active", false)) return;
        SessionState.SetBool(Prefix + "Active", false);
        Report report = Load();
        bool passed = report.errors.Count == 0 && SessionState.GetBool(Prefix + "QuitExitObserved", false) && report.checks.Count >= 25;
        foreach (Check check in report.checks) passed &= check.passed;
        report.status = passed ? "passed" : "failed";
        Save(report);
        SessionState.SetInt(Prefix + "ExitCode", passed ? 0 : 1);
        SessionState.SetBool(Prefix + "Exiting", true);
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        Application.logMessageReceived -= OnLog;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        Time.timeScale = 1f;
        if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
        EditorApplication.update -= ExitWhenReady;
        EditorApplication.update += ExitWhenReady;
    }
    private static void ExitWhenReady()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        SessionState.SetBool(Prefix + "Exiting", false);
        EditorApplication.Exit(SessionState.GetInt(Prefix + "ExitCode", 1));
    }
}
