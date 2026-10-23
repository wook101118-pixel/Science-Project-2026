// Temporary opt-in standalone fixture. Root owns full registry backup/restore around execution.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using sung_wook;
using Object = UnityEngine.Object;

public sealed class QaBeginnerHudProbe : MonoBehaviour
{
    [Serializable] private sealed class Check { public string name, details; public bool passed; }
    [Serializable] private sealed class TextLayout { public string label, text; public bool overflow, inside; public float fontPixels, x, y, width, height; }
    [Serializable] private sealed class Report
    {
        public string status = "running", startedUtc, finishedUtc;
        public string scope = "Standalone saved UI button callbacks, virtual InputSystem Esc delivered to the real pause Update, native render sizes and screen captures. Stage 3 public lesson commands and stage 4 position/velocity fixtures inspect HUD state. These fixtures are not a route-clear proof or first-time human usability test. Registry preservation is owned by the external runner.";
        public List<Check> checks = new List<Check>();
        public List<TextLayout> layouts = new List<TextLayout>();
        public List<string> screenshots = new List<string>(), errors = new List<string>();
    }
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private Report report;
    private string reportPath;
    private Keyboard keyboard, oldKeyboard;
    private InputSettings.BackgroundBehavior oldBackground;
    private bool finishing;
    private float started;
    private ElectricStageSelector selector;
    private ElectricLearningController2D lesson;
    private ElectricStageHudView hud;
    private ElectricBall2D ball;
    private CollapsiblePanel help;
    private PauseMenuController pause;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (Application.isEditor || Array.IndexOf(Environment.GetCommandLineArgs(), "--qa-beginner-hud") < 0) return;
        string path = null;
        foreach (string arg in Environment.GetCommandLineArgs()) if (arg.StartsWith("--qa-report=", StringComparison.Ordinal)) path = arg.Substring(12);
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)) return;
        GameObject host = new GameObject("Temporary Beginner HUD QA");
        DontDestroyOnLoad(host);
        host.AddComponent<QaBeginnerHudProbe>().Initialize(path);
    }
    private void Initialize(string path)
    {
        reportPath = path; started = Time.realtimeSinceStartup;
        report = new Report { startedUtc = DateTime.UtcNow.ToString("O") };
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        oldKeyboard = Keyboard.current;
        oldBackground = InputSystem.settings.backgroundBehavior;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        keyboard = InputSystem.AddDevice<Keyboard>("Temporary beginner HUD QA keyboard"); keyboard.MakeCurrent();
        Application.runInBackground = true; Application.targetFrameRate = 60; QualitySettings.vSyncCount = 0;
        Application.logMessageReceived += OnLog;
        Flush(); StartCoroutine(Guarded(Run()));
    }
    private IEnumerator Run()
    {
        yield return Ready(null);
        float deadline = Time.realtimeSinceStartup + 15f;
        while (!UnityEngine.Rendering.SplashScreen.isFinished && Time.realtimeSinceStartup < deadline) yield return null;
        for (int stage = 1; stage <= 5; stage++)
        {
            Require(SceneTransitionController.LoadScene(ElectricStageProgression.StageScenePath(stage)), "Rejected stage " + stage);
            yield return Ready("Stage_" + stage.ToString("00"));
            selector = Find<ElectricStageSelector>(); lesson = selector.GetComponent<ElectricLearningController2D>();
            hud = Find<ElectricStageHudView>(); ball = selector.Ball; help = Find<CollapsiblePanel>(); pause = Find<PauseMenuController>();
            yield return null;
            Add("stage" + stage + "-fresh-help-collapsed", !help.IsOpen && !pause.IsPaused && Time.timeScale == 1f, "Saved scene starts folded with running time.");
            if (stage == 3) Add("stage3-fixed-preparation-visible", lesson.IsPreparingTrial && Quest().Contains("공 고정"), Quest());
            if (stage == 1)
            {
                TutorialController2D tutorial = selector.GetComponent<TutorialController2D>();
                Add("stage1-tutorial-hud", tutorial != null && tutorial.IsInitialized && Quest().StartsWith(tutorial.CurrentTaskText, StringComparison.Ordinal), Quest());
            }
            foreach (Vector2Int size in new[] { new Vector2Int(640, 480), new Vector2Int(1600, 900) })
            {
                Screen.SetResolution(size.x, size.y, FullScreenMode.Windowed);
                yield return Delay(.8f); Refresh();
                string label = "stage" + stage + "-" + size.x;
                Add(label + "-native-size", Screen.width == size.x && Screen.height == size.y, Screen.width + "x" + Screen.height);
                // A non-default scale proves ownership restores the actual prior value, not a hard-coded 1.
                float originalScale = stage == 2 && size.x == 640 ? .5f : 1f;
                Time.timeScale = originalScale;
                Behaviour reader = ball.GetComponent<PlayerInputReader>();
                Behaviour controller = ball.GetComponent<PlayerController>();
                bool readerEnabled = reader != null && reader.enabled, controllerEnabled = controller != null && controller.enabled;
                DistanceLauncher2D distance = stage == 3 ? Find<DistanceLauncher2D>() : null;
                VoltageLauncher2D voltage = stage == 5 ? Find<VoltageLauncher2D>() : null;
                MonoBehaviour launcher = distance != null ? (MonoBehaviour)distance : voltage;
                string taskBeforeHelp = Quest();
                ElectricForceSample sampleBeforeHelp = default;
                bool hasSampleBeforeHelp = distance != null && distance.TryGetSample(out sampleBeforeHelp);
                float voltsBeforeHelp = voltage != null ? voltage.Voltage : 0f;
                bool launchedBeforeHelp = distance != null ? distance.HasLaunchSample : voltage != null && voltage.HasLaunchSample;
                float feedbackTimeBeforeHelp = distance != null ? distance.LastCommandFeedbackTime : voltage != null ? voltage.LastCommandFeedbackTime : 0f;
                if (distance != null) distance.RequestLaunch();
                if (voltage != null) voltage.RequestLaunch();
                bool queuedBeforePause = launcher != null && Field<bool>(launcher, "launchRequested");
                help.ToggleButton.onClick.Invoke();
                Vector2 position = ball.Body.position, velocity = ball.Body.linearVelocity;
                float fixedTime = Time.fixedTime;
                yield return Delay(.35f); Refresh();
                Add(label + "-reading-pause-owner", help.IsOpen && help.PausesForReading && pause.IsPaused && Time.timeScale == 0f
                    && (reader == null || !reader.enabled) && (controller == null || !controller.enabled), "Existing PauseMenu owns time and input; its menu is hidden.");
                Add(label + "-physics-frozen", Vector2.Distance(position, ball.Body.position) < .0001f
                    && Vector2.Distance(velocity, ball.Body.linearVelocity) < .0001f && Time.fixedTime == fixedTime, "Actual body position, velocity and fixed time are unchanged during reading.");
                if (launcher != null)
                {
                    Add(label + "-pause-cancels-preexisting-request", queuedBeforePause && !Field<bool>(launcher, "launchRequested"), "An E/UI request queued immediately before pause is discarded without disabling the measurement provider.");
                    if (distance != null) distance.RequestLaunch();
                    if (voltage != null) voltage.RequestLaunch();
                    bool pausedLaunchEdge = KeyPress(launcher, Key.E);
                    ElectricForceSample pausedSample = default;
                    bool hasPausedSample = distance != null && distance.TryGetSample(out pausedSample);
                    bool sameSample = distance == null || (hasSampleBeforeHelp && hasPausedSample
                        && sampleBeforeHelp.IsInRange == pausedSample.IsInRange && sampleBeforeHelp.Position == pausedSample.Position
                        && sampleBeforeHelp.Distance == pausedSample.Distance && sampleBeforeHelp.Force == pausedSample.Force
                        && sampleBeforeHelp.SignedCharge == pausedSample.SignedCharge && sampleBeforeHelp.SourceCharge == pausedSample.SourceCharge);
                    bool unchangedRecord = distance != null
                        ? distance.HasLaunchSample == launchedBeforeHelp && distance.LastCommandFeedbackTime == feedbackTimeBeforeHelp
                        : voltage.HasLaunchSample == launchedBeforeHelp && voltage.LastCommandFeedbackTime == feedbackTimeBeforeHelp && voltage.Voltage == voltsBeforeHelp;
                    Add(label + "-paused-provider-retains-task-sample", launcher.isActiveAndEnabled && taskBeforeHelp == Quest() && sameSample && unchangedRecord,
                        "Before=" + taskBeforeHelp + "; paused=" + Quest() + "; sample identical=" + sameSample + "; charge/launch record unchanged=" + unchangedRecord);
                    Add(label + "-paused-e-and-ui-requests-rejected", pausedLaunchEdge && !Field<bool>(launcher, "launchRequested"), "Both public RequestLaunch and a real launcher.Update virtual E edge are rejected at timeScale 0.");
                }
                RectTransform plans = Field<RectTransform>(hud, "planChoices");
                bool expectedPlans = stage == 2 || stage == 3 || stage == 5;
                Add(label + "-plan-visibility", plans != null && plans.gameObject.activeSelf == expectedPlans, "Plans should be visible=" + expectedPlans);
                if (expectedPlans && plans != null)
                {
                    Button[] choices = plans.GetComponentsInChildren<Button>();
                    ElectricExperimentPlan2D plan = selector.GetComponent<ElectricExperimentPlan2D>();
                    Require(choices.Length == 2 && plan != null, "Missing two saved plan buttons.");
                    choices[1].onClick.Invoke(); yield return null;
                    bool second = plan.SelectedPlan == 1;
                    choices[0].onClick.Invoke(); yield return null; Refresh();
                    Add(label + "-saved-plan-button-callbacks", second && plan.SelectedPlan == 0 && help.IsOpen && pause.IsPaused, "Actual saved button callbacks select 1 then 0 without resuming physics.");
                }
                Layout(label); yield return Capture(label + "-help-open");
                bool edge = true;
                if (size.x == 1600) edge = Esc(pause); else help.ToggleButton.onClick.Invoke();
                yield return Delay(.35f); Refresh();
                Add(label + "-close-restores-prior-state", edge && !help.IsOpen && !pause.IsPaused
                    && Mathf.Approximately(Time.timeScale, originalScale) && (reader == null || reader.enabled == readerEnabled)
                    && (controller == null || controller.enabled == controllerEnabled), "Close=" + (size.x == 1600 ? "virtual Esc real Pause.Update" : "saved toggle callback") + "; restored scale=" + Time.timeScale);
                if (launcher != null)
                {
                    bool unchangedRecord = distance != null
                        ? distance.HasLaunchSample == launchedBeforeHelp && distance.LastCommandFeedbackTime == feedbackTimeBeforeHelp
                        : voltage.HasLaunchSample == launchedBeforeHelp && voltage.LastCommandFeedbackTime == feedbackTimeBeforeHelp;
                    Add(label + "-resume-does-not-leak-paused-launch", launcher.isActiveAndEnabled && !Field<bool>(launcher, "launchRequested") && unchangedRecord,
                        "After actual resumed physics frames, no delayed launch or even launch-attempt feedback was produced.");
                }
                Time.timeScale = 1f;
                if (size.x == 640) yield return Capture(label + "-help-closed");
            }
            if (stage == 2)
            {
                pause.Pause(); Field<Button>(pause, "settingsButton").onClick.Invoke(); help.ToggleButton.onClick.Invoke();
                Add("settings-retains-pause-ownership", pause.IsPaused && pause.IsSettingsOpen && !help.IsOpen && Time.timeScale == 0f, "Help cannot intrude into pause settings.");
                Field<Button>(pause, "closeSettingsButton").onClick.Invoke(); Field<Button>(pause, "resumeButton").onClick.Invoke();
                yield return null;
                Add("settings-resume-restores-game", !pause.IsPaused && !pause.IsSettingsOpen && !help.IsOpen && Time.timeScale == 1f, "Saved settings and resume button callbacks.");
            }
            if (stage == 3) yield return Stage3();
            if (stage == 4) yield return Stage4();
            // Transition while reading must never restore old gameplay over a new scene's fade.
            help.ToggleButton.onClick.Invoke(); yield return null;
        }
        Require(SceneTransitionController.LoadScene(PauseMenuController.MainMenuPath), "Return transition rejected.");
        yield return Ready("MainMenu");
        Add("reading-scene-transition-finishes-running", Time.timeScale == 1f && !SceneTransitionController.IsBusy, "Old help owner cannot override scene-transition time restoration.");
        Add("runtime-errors", report.errors.Count == 0, string.Join("\n", report.errors));
        Finish(null);
    }
    private IEnumerator Stage3()
    {
        lesson.SetBallCharge(0f); lesson.RequestLaunch(); yield return FixedFrames(2); Refresh();
        string feedback = lesson.RecentFeedback;
        ElectricExperimentPlan2D plan = selector.GetComponent<ElectricExperimentPlan2D>();
        string task = plan != null && !string.IsNullOrEmpty(plan.CurrentTaskOverride) ? plan.CurrentTaskOverride : lesson.CurrentTaskText;
        Add("stage3-rejection-does-not-hide-task", !string.IsNullOrEmpty(feedback) && Footer().Contains(feedback)
            && Quest().StartsWith(task, StringComparison.Ordinal) && Quest().Contains("공 고정"), "Task=" + Quest() + "; feedback=" + Footer());
        lesson.SetBallCharge(1f); lesson.PrepareTrial();
        selector.Lever.SetValue(.2f); yield return FixedFrames(2);
        lesson.RecordBaseline(); selector.Lever.SetValue(.8f); yield return FixedFrames(2);
        lesson.RequestLaunch(); yield return FixedFrames(2); Refresh();
        Add("stage3-launched-landing-hint", !lesson.IsPreparingTrial && Quest().Contains("A/D"), Quest());
        yield return Capture("stage3-launched-hint");
    }
    private IEnumerator Stage4()
    {
        // Layout-only fixture: current body is temporarily placed inside/outside the saved actual field.
        UniformField2D field = Find<UniformField2D>(); Bounds bounds = field.GetComponent<BoxCollider2D>().bounds;
        selector.Lever.SetValue(0f); // Saved field's actual downward setting, independent of prior UI callbacks.
        RigidbodyConstraints2D constraints = ball.Body.constraints;
        ball.Body.constraints = RigidbodyConstraints2D.FreezeAll;
        ball.Body.position = new Vector2(bounds.min.x - 2f, bounds.center.y); Physics2D.SyncTransforms();
        lesson.ResetTransientObservations(); yield return null; Refresh();
        Add("stage4-outside-field-hud", !lesson.IsInsideField && Quest().Contains("장 밖") && Quest().Contains("장 안으로"), Quest());
        ball.Body.position = bounds.center; Physics2D.SyncTransforms();
        lesson.SetBallCharge(-1f); yield return null; Refresh();
        float ascent = lesson.FieldAscentProgress, descent = lesson.NeutralFieldDescentProgress, brake = lesson.FieldBrakeProgress;
        string status = lesson.FieldStatusText;
        for (int i = 0; i < 5; i++) { string read = lesson.FieldStatusText; }
        Add("stage4-inside-progress-and-feedback", lesson.IsInsideField && Quest().Contains("장 안") && Quest().Contains("연속 상승")
            && Footer().Contains(lesson.RecentFeedback) && !string.IsNullOrEmpty(lesson.RecentFeedback), Quest() + "; " + Footer());
        Add("stage4-status-getter-readonly", ascent == lesson.FieldAscentProgress && descent == lesson.NeutralFieldDescentProgress
            && brake == lesson.FieldBrakeProgress, status + "; repeated reads do not advance observations.");
        yield return Capture("stage4-inside-progress-fixture");
        ball.Body.constraints = constraints;
    }
    private void Layout(string label)
    {
        Canvas.ForceUpdateCanvases();
        bool valid = true; int count = 0;
        foreach (TMP_Text text in hud.GetComponentsInChildren<TMP_Text>())
        {
            if (!text.isActiveAndEnabled || string.IsNullOrEmpty(text.text)) continue;
            text.ForceMeshUpdate(); Rect rect = ScreenRect(text.rectTransform);
            bool inside = rect.xMin >= -1f && rect.yMin >= -1f && rect.xMax <= Screen.width + 1f && rect.yMax <= Screen.height + 1f;
            float pixels = text.fontSize * text.canvas.scaleFactor;
            report.layouts.Add(new TextLayout { label = label, text = text.text, overflow = text.isTextOverflowing,
                inside = inside, fontPixels = pixels, x = rect.x, y = rect.y, width = rect.width, height = rect.height });
            valid &= inside && !text.isTextOverflowing && pixels >= 13.9f; count++;
        }
        Add(label + "-visible-text-readable-geometry", count >= 6 && valid, "All active HUD/help/plan text: >=14 screen pixels, TMP overflow false, every text rect inside native screen. Measured rows=" + count + ". Screenshots require visual review too.");
    }
    private static Rect ScreenRect(RectTransform rect)
    {
        Vector3[] corners = new Vector3[4]; rect.GetWorldCorners(corners);
        Canvas canvas = rect.GetComponentInParent<Canvas>();
        Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        Vector2 min = RectTransformUtility.WorldToScreenPoint(camera, corners[0]), max = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }
    private bool Esc(PauseMenuController target) => KeyPress(target, Key.Escape);
    private bool KeyPress(MonoBehaviour target, Key key)
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState()); Pump();
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(key)); Pump();
        bool edge = keyboard[key].wasPressedThisFrame; target.SendMessage("Update", SendMessageOptions.RequireReceiver);
        InputSystem.QueueStateEvent(keyboard, new KeyboardState()); Pump(); return edge;
    }
    private static void Pump() => typeof(InputSystem).GetMethod("Update", BindingFlags.Static | BindingFlags.NonPublic, null,
        new[] { typeof(InputUpdateType) }, null).Invoke(null, new object[] { InputUpdateType.Dynamic });
    private void Refresh() => hud.SendMessage("LateUpdate", SendMessageOptions.RequireReceiver);
    private string Quest() { Refresh(); return Field<TMP_Text>(hud, "questText").text; }
    private string Footer() => Field<TMP_Text>(hud, "controlsText").text;
    private static T Find<T>() where T : Object { T value = Object.FindFirstObjectByType<T>(); Require(value != null, "Missing " + typeof(T).Name); return value; }
    private static T Field<T>(object target, string name) { FieldInfo field = target.GetType().GetField(name, Members); Require(field != null, "Missing field " + name); return (T)field.GetValue(target); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static IEnumerator Delay(float seconds) { float until = Time.realtimeSinceStartup + seconds; while (Time.realtimeSinceStartup < until) yield return null; }
    private static IEnumerator FixedFrames(int count) { for (int i = 0; i < count; i++) yield return new WaitForFixedUpdate(); }
    private IEnumerator Ready(string scene)
    {
        float until = Time.realtimeSinceStartup + 25f;
        while ((!SceneTransitionController.IsAvailable || SceneTransitionController.IsBusy || (scene != null && SceneManager.GetActiveScene().name != scene)) && Time.realtimeSinceStartup < until) yield return null;
        Require(SceneTransitionController.IsAvailable && !SceneTransitionController.IsBusy && (scene == null || SceneManager.GetActiveScene().name == scene), "Scene ready timeout: " + scene);
        yield return null; yield return null;
    }
    private IEnumerator Capture(string label)
    {
        Debug.developerConsoleVisible = false; yield return new WaitForEndOfFrame();
        Texture2D image = ScreenCapture.CaptureScreenshotAsTexture(); Require(image != null, "Native capture failed.");
        string path = Path.Combine(Path.GetDirectoryName(reportPath), Path.GetFileNameWithoutExtension(reportPath) + "-" + label + ".png");
        File.WriteAllBytes(path, image.EncodeToPNG()); Object.Destroy(image); report.screenshots.Add(path); Flush();
    }
    private void Add(string name, bool passed, string details) { report.checks.Add(new Check { name = name, passed = passed, details = details }); Flush(); }
    private void Flush() => File.WriteAllText(reportPath, JsonUtility.ToJson(report, true));
    private void OnLog(string message, string stack, LogType type) { if (type == LogType.Error || type == LogType.Assert || type == LogType.Exception) { report.errors.Add(message + "\n" + stack); Flush(); } }
    private void Update() { if (!finishing && Time.realtimeSinceStartup - started > 240f) Finish(new TimeoutException("Beginner HUD probe exceeded 240 real seconds.")); }
    private IEnumerator Guarded(IEnumerator sequence)
    {
        Stack<IEnumerator> stack = new Stack<IEnumerator>(); stack.Push(sequence);
        while (stack.Count > 0 && !finishing)
        {
            object item = null; bool next = false; Exception error = null;
            try { next = stack.Peek().MoveNext(); if (next) item = stack.Peek().Current; } catch (Exception caught) { error = caught; }
            if (error != null) { Finish(error); yield break; }
            if (!next) stack.Pop(); else if (item is IEnumerator child) stack.Push(child); else yield return item;
        }
    }
    private void Finish(Exception error)
    {
        if (finishing) return; finishing = true;
        if (error != null) report.errors.Add(error.ToString());
        Application.logMessageReceived -= OnLog;
        if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
        if (oldKeyboard != null && oldKeyboard.added) oldKeyboard.MakeCurrent();
        InputSystem.settings.backgroundBehavior = oldBackground;
        report.status = report.errors.Count == 0 && !report.checks.Exists(c => !c.passed) ? "passed" : "failed";
        report.finishedUtc = DateTime.UtcNow.ToString("O"); Flush(); Application.Quit(report.status == "passed" ? 0 : 1);
    }
}
