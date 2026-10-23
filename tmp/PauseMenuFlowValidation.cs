// Temporary integration harness. Copy to Assets/Editor only in an isolated Unity project.
// Run: Unity.exe -batchmode -projectPath <clone> -executeMethod PauseMenuFlowValidation.RunBatch
// Do not pass -quit. This harness owns its isolated Play Mode and editor exit.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using sung_wook;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class PauseMenuFlowValidation
{
    private const string Prefix = "Codex.PauseMenuFlowValidation.";
    private const string MenuPath = "Assets/@Project/@sung_wook/@Game/00_Scenes/MainMenu.unity";
    private const string Stage3Path = "Assets/@Project/@sung_wook/@Game/00_Scenes/Stage/Stage_03.unity";
    private enum Phase { EnterPlay, InitialReveal, PausedKeys, AfterResume, Restart, ReturnMenu, Finish }

    [Serializable] private sealed class Check { public string name; public bool passed; public string details; }
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
        public string mode = "Saved Stage 01–05 pause references; saved Stage 03 Play Mode input/button integration, physics pause, asynchronous current-scene restart and menu return. The Stage 03 fixture releases its existing held trial through ReleaseTrial before assigning fixture charge/stars/position, and temporary progression flags isolate pause guards. No gameplay-clear claims or saved asset changes.";
        public List<Check> checks = new List<Check>();
        public List<SceneLoad> sceneLoads = new List<SceneLoad>();
        public List<string> errors = new List<string>();
    }

    private static Keyboard probeKeyboard;
    private static Keyboard previousKeyboard;
    private static InputSettings.EditorInputBehaviorInPlayMode previousEditorInput;
    private static InputSettings.BackgroundBehavior previousBackground;
    private static InputSettings.UpdateMode previousUpdateMode;
    private static bool previousRunInBackground;
    private static readonly Dictionary<Behaviour, bool> inputStates = new Dictionary<Behaviour, bool>();
    private static Vector2 pausedPosition;
    private static Vector2 pausedVelocity;
    private static float pausedFixedTime;
    private static float pausedCharge;
    private static int pausedStars;
    private static int originalBallId;
    private static global::LeverMover leverMover;
    private static GearLeverController preDisabledInput;
    private static Quaternion pausedLeverRotation;

    static PauseMenuFlowValidation()
    {
        if (!Application.isBatchMode) return;
        if (SessionState.GetBool(Prefix + "Active", false)) ResumeCallbacks();
        if (SessionState.GetBool(Prefix + "Exiting", false)) EditorApplication.update += ExitWhenReady;
    }

    private static string ReportPath
    {
        get
        {
            string folder = Environment.GetEnvironmentVariable("CODEX_PAUSE_VALIDATION_OUTPUT");
            return Path.Combine(string.IsNullOrWhiteSpace(folder) ? Path.Combine(Directory.GetCurrentDirectory(), "Logs") : Path.GetFullPath(folder), "PauseMenuFlowValidation.json");
        }
    }

    public static void RunBatch()
    {
        if (!Application.isBatchMode)
        {
            Debug.LogWarning("Pause validation must run in a separately launched isolated batch editor.");
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
            SessionState.SetBool(Prefix + "InitialIntermediate", false);
            SessionState.SetInt(Prefix + "ServiceInstance", 0);
            SessionState.SetString(Prefix + "ExpectedScene", "");
            foreach (int stage in new[] { 1, 2, 3, 4, 5 })
            {
                string path = ElectricStageProgression.StageScenePath(stage);
                if (stage == 1)
                {
                    bool registered = false;
                    foreach (EditorBuildSettingsScene entry in EditorBuildSettings.scenes)
                        if (entry.enabled && entry.path == path) registered = true;
                    CheckResult("stage-01-is-enabled-in-build-settings-for-restart", registered,
                        "Stage 01 is registered in enabled Build Settings; runtime loadability is checked after entering Play Mode.");
                }
                EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                PauseMenuController pause = FindInActiveScene<PauseMenuController>();
                Require(pause != null, "Saved Stage " + stage + " has no PauseMenuController.");
                GameObject menuRoot = Reference<GameObject>(pause, "menuRoot");
                CheckResult("stage-" + stage + "-saved-pause-ui-is-closed-and-referenced", menuRoot != null && !menuRoot.activeSelf
                    && pause.isActiveAndEnabled, "Pause listener stays active while its saved menuRoot is inactive.");
                foreach (string field in new[] { "resumeButton", "restartButton", "mainMenuButton" })
                {
                    Button button = Reference<Button>(pause, field);
                    Image image = button != null ? button.targetGraphic as Image : null;
                    CheckResult("stage-" + stage + "-" + field + "-saved-and-original-colors", button != null
                        && menuRoot != null && button.transform.IsChildOf(menuRoot.transform) && image != null
                        && image.color == Color.white && image.sprite != null && button.transition == Selectable.Transition.None,
                        "Saved button is inside the authored pause UI, has an existing sprite and retains white tint without selectable recoloring.");
                }
                CheckResult("stage-" + stage + "-event-system-is-present", FindInActiveScene<EventSystem>() != null,
                    "An EventSystem is saved with the gameplay scene.");
            }
            EditorSceneManager.OpenScene(Stage3Path, OpenSceneMode.Single);
            ResumeCallbacks();
        }
        catch (Exception error) { Fail(error); }
    }

    private static void ResumeCallbacks()
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
            Require((DateTime.UtcNow - started).TotalSeconds < 180d, "Pause flow exceeded the 180-second real-time watchdog.");
            Require(Load().errors.Count == 0, "A Unity error was logged during pause verification.");
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

            SceneTransitionController service = Service();
            if (service == null) return;
            CanvasGroup overlay = Reference<CanvasGroup>(service, "fadeOverlay");
            Require(overlay != null, "Saved transition prefab has no Fade Overlay reference.");
            if (phase == Phase.InitialReveal)
            {
                PauseMenuController pause = PauseController();
                if (SceneTransitionController.IsBusy)
                {
                    if (overlay.alpha > 0.01f && overlay.alpha < 0.99f) SessionState.SetBool(Prefix + "InitialIntermediate", true);
                    pause.Pause();
                    Require(!pause.IsPaused, "Pause must refuse to take ownership during initial transition.");
                    return;
                }
                ElectricStageSession2D session = Object.FindFirstObjectByType<ElectricStageSession2D>();
                if (session == null || !session.IsInitialized) return;
                CheckResult("initial-stage-reveal-is-unscaled-and-pause-guarded", SessionState.GetBool(Prefix + "InitialIntermediate", false)
                    && Time.timeScale == 1f && !pause.IsPaused && !Reference<GameObject>(pause, "menuRoot").activeSelf,
                    "Pause cannot open during the saved transition's initial fade, then the authored menu remains closed.");
                string stage1Path = ElectricStageProgression.StageScenePath(1);
                bool stage1Loadable = Application.CanStreamedLevelBeLoaded(stage1Path);
                CheckResult("stage-01-is-loadable-in-play-mode-for-restart", stage1Loadable,
                    "In Play Mode, Application.CanStreamedLevelBeLoaded(" + stage1Path + ")=" + stage1Loadable
                    + "; activeScene=" + SceneManager.GetActiveScene().path + ".");
                SessionState.SetInt(Prefix + "ServiceInstance", service.GetInstanceID());
                CheckProgressionGuards(pause);
                PreparePausedFixture(pause);
                SetPhase(Phase.PausedKeys);
                return;
            }
            if (phase == Phase.PausedKeys)
            {
                if (EditorApplication.timeSinceStartup - SessionState.GetFloat(Prefix + "PauseStarted", 0f) < 0.3d
                    || Time.frameCount <= SessionState.GetInt(Prefix + "PauseFrame", 0) + 2) return;
                CheckPausedFixture();
                ReleaseKeyboard();
                PauseMenuController pause = PauseController();
                Click(Reference<Button>(pause, "resumeButton"));
                CheckResult("saved-resume-button-restores-time-and-closes-menu", !pause.IsPaused && Time.timeScale == 1f
                    && !Reference<GameObject>(pause, "menuRoot").activeSelf, "The saved Continue button restores gameplay immediately.");
                bool restored = true;
                foreach (var pair in inputStates) if (pair.Key != null) restored &= pair.Key.enabled == pair.Value;
                CheckResult("resume-restores-only-originally-enabled-input-components", restored && preDisabledInput != null && !preDisabledInput.enabled,
                    "Every captured input enabled state is preserved, including the intentionally pre-disabled GearLeverController fixture.");
                CheckResult("resume-keeps-pre-pause-rigidbody-state", Vector2.Distance(Ball().Body.position, pausedPosition) < 0.0001f
                    && Vector2.Distance(Ball().Body.linearVelocity, pausedVelocity) < 0.0001f,
                    "Continue preserves position and momentum rather than resetting the attempt.");
                DispatchEscape(pause);
                CheckResult("escape-reopens-pause", pause.IsPaused && Time.timeScale == 0f, "A second synthetic Esc press is handled by the actual controller Update method.");
                DispatchEscape(pause);
                CheckResult("escape-resumes-pause", !pause.IsPaused && Time.timeScale == 1f, "A third Esc press invokes the same resume flow as the saved Continue button.");
                ReleaseKeyboard();
                SessionState.SetInt(Prefix + "ResumedFrame", Time.frameCount);
                SessionState.SetFloat(Prefix + "ResumedStarted", (float)EditorApplication.timeSinceStartup);
                SetPhase(Phase.AfterResume);
                return;
            }
            if (phase == Phase.AfterResume)
            {
                if (Time.frameCount <= SessionState.GetInt(Prefix + "ResumedFrame", 0) + 2
                    || EditorApplication.timeSinceStartup - SessionState.GetFloat(Prefix + "ResumedStarted", 0f) < 0.15d) return;
                CheckResult("resumed-physics-advances-again", Time.fixedTime > pausedFixedTime
                    && Vector2.Distance(Ball().Body.position, pausedPosition) > 0.0001f,
                    "After at least 0.15 real seconds and three frames, the same released Rigidbody advances naturally after Time.timeScale returns to 1.");
                PauseMenuController pause = PauseController();
                pause.Pause();
                Require(pause.IsPaused, "Could not pause before the saved Restart action.");
                PrepareLoad(Stage3Path);
                Button restart = Reference<Button>(pause, "restartButton");
                Click(restart);
                AsyncOperation operation = Reference<AsyncOperation>(service, "pendingLoad");
                CheckResult("saved-restart-begins-current-scene-async-load", SceneTransitionController.IsBusy && Time.timeScale == 0f
                    && operation != null && !operation.allowSceneActivation && SceneManager.GetActiveScene().path == Stage3Path
                    && overlay.blocksRaycasts, "The paused Restart button loads the current Stage 03 behind the shared fade.");
                restart.onClick.Invoke();
                CheckResult("restart-double-click-is-one-operation", Reference<AsyncOperation>(service, "pendingLoad") == operation,
                    "A second saved Restart invocation cannot replace or duplicate the asynchronous load.");
                SetPhase(Phase.Restart);
                return;
            }
            if (phase == Phase.Restart)
            {
                if (!AwaitLoad(service, overlay)) return;
                ElectricStageSession2D session = Object.FindFirstObjectByType<ElectricStageSession2D>();
                if (session == null || !session.IsInitialized) return;
                ElectricStageSelector selector = Object.FindFirstObjectByType<ElectricStageSelector>();
                ElectricLearningController2D lesson = Object.FindFirstObjectByType<ElectricLearningController2D>();
                PauseMenuController pause = PauseController();
                bool freshInputs = true;
                foreach (Behaviour input in InputComponents()) if (input.gameObject.activeInHierarchy) freshInputs &= input.enabled;
                int count = Load().sceneLoads.FindAll(entry => entry.path == Stage3Path).Count
                    - SessionState.GetInt(Prefix + "ExpectedLoadCountBefore", 0);
                CheckResult("restart-loads-only-current-stage-once", count == 1 && selector.SelectedStage == 3,
                    "Actual Stage 03 sceneLoaded callback count=" + count + ".");
                CheckResult("restart-restores-fresh-attempt-and-inputs", Ball().GetInstanceID() != originalBallId && Ball().Stars == 0
                    && lesson != null && !lesson.HasBaseline && !lesson.HasResult && lesson.IsPreparingTrial && !pause.IsPaused
                    && !Reference<GameObject>(pause, "menuRoot").activeSelf && Time.timeScale == 1f && freshInputs,
                    "Fresh scene initialization resets the test's star/position state, current trial, paused UI and disabled-input fixture.");
                pause.Pause();
                Require(pause.IsPaused, "Could not pause before the saved Main Menu action.");
                PrepareLoad(MenuPath);
                Click(Reference<Button>(pause, "mainMenuButton"));
                CheckResult("saved-main-menu-button-begins-async-while-paused", SceneTransitionController.IsBusy && Time.timeScale == 0f
                    && Reference<AsyncOperation>(service, "pendingLoad") != null, "The saved Main Menu button hands pause ownership to the shared unscaled transition.");
                SetPhase(Phase.ReturnMenu);
                return;
            }
            if (phase == Phase.ReturnMenu)
            {
                if (!AwaitLoad(service, overlay)) return;
                MainMenuController menu = Object.FindFirstObjectByType<MainMenuController>();
                Require(menu != null, "Return did not produce the saved main menu.");
                CheckResult("return-to-menu-clears-pause-and-stage-objects", !menu.IsSettingsOpen && Time.timeScale == 1f
                    && Object.FindFirstObjectByType<ElectricBall2D>() == null && Object.FindFirstObjectByType<PauseMenuController>() == null
                    && Reference<GameObject>(menu, "mainContent").activeInHierarchy,
                    "The actual saved menu is interactive and the destroyed gameplay scene leaves no pause state or player behind.");
                SetPhase(Phase.Finish);
                Finish();
            }
        }
        catch (Exception error) { Fail(error); }
    }

    private static void CheckProgressionGuards(PauseMenuController pause)
    {
        ElectricStageProgression progression = Object.FindFirstObjectByType<ElectricStageProgression>();
        Require(progression != null, "Stage 03 has no progression owner.");
        foreach (string property in new[] { "IsTransitioning", "IsGameComplete" })
        {
            FieldInfo field = typeof(ElectricStageProgression).GetField("<" + property + ">k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            Require(field != null, "Progression test fixture could not find property backing field " + property + ".");
            bool original = (bool)field.GetValue(progression);
            try
            {
                field.SetValue(progression, true);
                pause.Pause();
                CheckResult("pause-refuses-progression-" + property, !pause.IsPaused && Time.timeScale == 1f,
                    "Fixture marks " + property + " without claiming a clear; the pause API refuses to reopen gameplay UI.");
            }
            finally { field.SetValue(progression, original); }
        }
        Time.timeScale = 0f;
        pause.Pause();
        CheckResult("pause-refuses-time-owned-by-another-screen", !pause.IsPaused && Time.timeScale == 0f,
            "A preexisting zero time scale remains owned by its original flow.");
        Time.timeScale = 1f;
    }

    private static void PreparePausedFixture(PauseMenuController pause)
    {
        SetupKeyboard();
        ElectricBall2D ball = Ball();
        originalBallId = ball.GetInstanceID();
        ElectricLearningController2D lesson = Object.FindFirstObjectByType<ElectricLearningController2D>();
        Require(lesson != null, "Stage 03 has no lesson owner for the released-body fixture.");
        // Release the saved experiment hold through its existing lifecycle before testing a dynamic pause.
        // Merely changing bodyType would leave PrepareTrial's FreezeAll constraints in place.
        lesson.SendMessage("ReleaseTrial", SendMessageOptions.RequireReceiver);
        Require(!lesson.IsPreparingTrial && (ball.Body.constraints & RigidbodyConstraints2D.FreezePositionY) == 0,
            "The fixture body must be released from the existing Stage 03 trial hold before pause physics is tested.");
        ball.Body.bodyType = RigidbodyType2D.Dynamic;
        ball.Body.simulated = true;
        ball.Body.position = new Vector2(-5f, 4f);
        ball.Body.linearVelocity = new Vector2(0.6f, -0.2f);
        ball.SetChargeMagnitude(0.75f);
        ball.SetPositive();
        ball.CollectStar();
        preDisabledInput = Object.FindFirstObjectByType<GearLeverController>();
        Require(preDisabledInput != null, "Stage 03 has no existing GearLeverController for enabled-state preservation check.");
        preDisabledInput.enabled = false;
        inputStates.Clear();
        foreach (Behaviour input in InputComponents()) inputStates[input] = input.enabled;
        leverMover = Object.FindFirstObjectByType<global::LeverMover>();
        Require(leverMover != null, "Stage 03 has no existing LeverMover for the stale-drag fixture.");
        // This marks an in-progress drag fixture only; it does not simulate a mouse click or change the lever.
        Field(leverMover, "dragging").SetValue(leverMover, true);
        Transform pivot = leverMover != null ? Reference<Transform>(leverMover, "pivot") : null;
        pausedLeverRotation = pivot != null ? pivot.localRotation : Quaternion.identity;
        DispatchEscape(pause);
        CheckResult("escape-opens-saved-pause-ui-and-freezes-time", pause.IsPaused && Time.timeScale == 0f
            && Reference<GameObject>(pause, "menuRoot").activeInHierarchy,
            "InputSystem delivered Esc; the controller's actual Update handler opened its authored menu.");
        bool allDisabled = true;
        foreach (Behaviour input in inputStates.Keys) if (input != null && input.gameObject.activeInHierarchy) allDisabled &= !input.enabled;
        CheckResult("pause-disables-all-gameplay-input-and-launcher-components", allDisabled && inputStates.Count >= 7,
            "Captured " + inputStates.Count + " player, learning, stage, lever and launcher components; none remains enabled while paused.");
        CheckResult("pause-cancels-in-progress-lever-drag", !Value<bool>(leverMover, "dragging"),
            "The existing LeverMover's true dragging fixture is cleared by OnDisable, so releasing the mouse while paused cannot leave a stale drag after resume.");
        CheckResult("pause-selects-continue-button", EventSystem.current != null
            && EventSystem.current.currentSelectedGameObject == Reference<Button>(pause, "resumeButton").gameObject,
            "The saved Continue button receives UI keyboard focus.");
        pausedPosition = ball.Body.position;
        pausedVelocity = ball.Body.linearVelocity;
        pausedCharge = ball.SignedCharge;
        pausedStars = ball.Stars;
        pausedFixedTime = Time.fixedTime;
        InputSystem.QueueStateEvent(probeKeyboard, new KeyboardState(Key.Space, Key.R, Key.T, Key.E, Key.B, Key.Z, Key.X, Key.C, Key.V, Key.N));
        PumpDynamicInput();
        CheckResult("paused-gameplay-keys-are-delivered-to-input-system", probeKeyboard.spaceKey.isPressed && probeKeyboard.rKey.isPressed
            && probeKeyboard.tKey.isPressed && probeKeyboard.eKey.isPressed,
            "Virtual keyboard keys are held across several actual Play Mode frames to exercise disabled-input behavior.");
        SessionState.SetFloat(Prefix + "PauseStarted", (float)EditorApplication.timeSinceStartup);
        SessionState.SetInt(Prefix + "PauseFrame", Time.frameCount);
    }

    private static void CheckPausedFixture()
    {
        ElectricBall2D ball = Ball();
        ElectricLearningController2D lesson = Object.FindFirstObjectByType<ElectricLearningController2D>();
        CheckResult("pause-freezes-dynamic-rigidbody-and-physics-time", PauseController().IsPaused && Time.timeScale == 0f
            && Time.fixedTime == pausedFixedTime && Vector2.Distance(ball.Body.position, pausedPosition) < 0.0001f
            && Vector2.Distance(ball.Body.linearVelocity, pausedVelocity) < 0.0001f && ball.Body.bodyType == RigidbodyType2D.Dynamic,
            "Across at least three real frames and 0.3 seconds, physics time, dynamic position, velocity and body type remain unchanged.");
        CheckResult("paused-space-r-t-and-charge-commands-do-not-change-attempt", Mathf.Abs(ball.SignedCharge - pausedCharge) < 0.0001f
            && ball.Stars == pausedStars && lesson != null && !lesson.HasBaseline && !lesson.IsPreparingTrial,
            "Space/charge keys leave q=+0.75; R leaves the fixture star intact; T cannot snap the fixture position or change its dynamic body; B cannot record a baseline.");
        bool noQueuedLaunch = true;
        foreach (DistanceLauncher2D launcher in ComponentsInActiveScene<DistanceLauncher2D>()) noQueuedLaunch &= !Value<bool>(launcher, "launchRequested");
        foreach (VoltageLauncher2D launcher in ComponentsInActiveScene<VoltageLauncher2D>()) noQueuedLaunch &= !Value<bool>(launcher, "launchRequested");
        CheckResult("paused-e-does-not-queue-launch-after-resume", noQueuedLaunch,
            "Both launcher types retain no pending launch request while E is held during pause.");
        Transform pivot = leverMover != null ? Reference<Transform>(leverMover, "pivot") : null;
        CheckResult("pause-keeps-existing-lever-position", pivot != null && Quaternion.Angle(pivot.localRotation, pausedLeverRotation) < 0.001f,
            "The existing lever transform remains stable while its input component is disabled.");
    }

    private static IEnumerable<Behaviour> InputComponents()
    {
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            foreach (Behaviour behaviour in root.GetComponentsInChildren<Behaviour>(true))
                if (behaviour is PlayerController || behaviour is PlayerInputReader || behaviour is ElectricStageInput2D
                    || behaviour is ElectricLearningInput2D || behaviour is GearLeverController || behaviour is global::LeverReader
                    || behaviour is global::LeverMover || behaviour is DistanceLauncher2D || behaviour is VoltageLauncher2D)
                    yield return behaviour;
    }

    private static void SetupKeyboard()
    {
        previousKeyboard = Keyboard.current;
        previousEditorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
        previousBackground = InputSystem.settings.backgroundBehavior;
        previousUpdateMode = InputSystem.settings.updateMode;
        previousRunInBackground = Application.runInBackground;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
        Application.runInBackground = true;
        probeKeyboard = InputSystem.AddDevice<Keyboard>();
        probeKeyboard.MakeCurrent();
        ReleaseKeyboard();
    }

    private static void DispatchEscape(PauseMenuController controller)
    {
        ReleaseKeyboard();
        InputSystem.QueueStateEvent(probeKeyboard, new KeyboardState(Key.Escape));
        PumpDynamicInput();
        Require(probeKeyboard.escapeKey.wasPressedThisFrame, "Synthetic Esc input failed to reach the InputSystem keyboard.");
        // Invoke the actual input handler in the same input update, avoiding editor batch focus races.
        controller.SendMessage("Update", SendMessageOptions.RequireReceiver);
        ReleaseKeyboard();
    }

    private static void ReleaseKeyboard()
    {
        if (probeKeyboard == null || !probeKeyboard.added) return;
        InputSystem.QueueStateEvent(probeKeyboard, new KeyboardState());
        PumpDynamicInput();
    }

    private static void PumpDynamicInput()
    {
        MethodInfo update = typeof(InputSystem).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Static,
            null, new[] { typeof(InputUpdateType) }, null);
        Require(update != null, "Input System has no Dynamic update dispatcher for the isolated probe keyboard.");
        update.Invoke(null, new object[] { InputUpdateType.Dynamic });
    }

    private static void RestoreKeyboard()
    {
        if (probeKeyboard == null) return;
        if (probeKeyboard.added) InputSystem.RemoveDevice(probeKeyboard);
        probeKeyboard = null;
        if (previousKeyboard != null && previousKeyboard.added) previousKeyboard.MakeCurrent();
        InputSystem.settings.editorInputBehaviorInPlayMode = previousEditorInput;
        InputSystem.settings.backgroundBehavior = previousBackground;
        InputSystem.settings.updateMode = previousUpdateMode;
        Application.runInBackground = previousRunInBackground;
    }

    private static void PrepareLoad(string target)
    {
        SessionState.SetString(Prefix + "ExpectedScene", target);
        SessionState.SetInt(Prefix + "ExpectedLoadCountBefore", Load().sceneLoads.FindAll(entry => entry.path == target).Count);
        SessionState.SetBool(Prefix + "FadeOutIntermediate", false);
        SessionState.SetBool(Prefix + "FadeInIntermediate", false);
        SessionState.SetBool(Prefix + "ExpectedSceneLoaded", false);
    }

    private static bool AwaitLoad(SceneTransitionController service, CanvasGroup overlay)
    {
        string expected = SessionState.GetString(Prefix + "ExpectedScene", "");
        bool loaded = SessionState.GetBool(Prefix + "ExpectedSceneLoaded", false);
        if (SceneTransitionController.IsBusy)
        {
            Require(Time.timeScale == 0f && overlay.blocksRaycasts, "Transition must keep gameplay frozen and intercept UI until the reveal completes.");
            if (overlay.alpha > 0.01f && overlay.alpha < 0.99f)
                SessionState.SetBool(Prefix + (loaded ? "FadeInIntermediate" : "FadeOutIntermediate"), true);
            return false;
        }
        if (expected == MenuPath)
        {
            MainMenuController menu = Object.FindFirstObjectByType<MainMenuController>();
            if (menu == null || !Reference<Button>(menu, "startButton").IsInteractable()) return false;
        }
        SceneTransitionController[] all = Object.FindObjectsByType<SceneTransitionController>(FindObjectsSortMode.None);
        CheckResult("loaded-scene-ready-" + Path.GetFileNameWithoutExtension(expected), SceneManager.GetActiveScene().path == expected
            && !SceneTransitionController.IsBusy && overlay.alpha == 0f && !overlay.blocksRaycasts && Time.timeScale == 1f
            && all.Length == 1 && service.GetInstanceID() == SessionState.GetInt(Prefix + "ServiceInstance", 0),
            "The same persistent transition instance completes the reveal and restores Time.timeScale to 1.");
        CheckResult("fade-out-and-in-observed-" + Path.GetFileNameWithoutExtension(expected),
            SessionState.GetBool(Prefix + "FadeOutIntermediate", false) && SessionState.GetBool(Prefix + "FadeInIntermediate", false),
            "Observed intermediate fade alpha both before and after the actual sceneLoaded callback, including same-path restart.");
        CheckResult("actual-sceneLoaded-received-" + Path.GetFileNameWithoutExtension(expected), loaded, expected);
        return true;
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
            "Observed in the real SceneManager.sceneLoaded callback before incoming reveal.");
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Prefix + "Active", false) || state != PlayModeStateChange.EnteredEditMode) return;
        Fail(new InvalidOperationException("Play Mode ended before the pause harness completed its flows."));
    }

    private static SceneTransitionController Service()
    {
        foreach (SceneTransitionController service in Object.FindObjectsByType<SceneTransitionController>(FindObjectsSortMode.None))
            if (service.gameObject.scene.name == "DontDestroyOnLoad") return service;
        return Object.FindFirstObjectByType<SceneTransitionController>();
    }
    private static PauseMenuController PauseController()
    {
        PauseMenuController pause = FindInActiveScene<PauseMenuController>();
        Require(pause != null, "Current saved gameplay scene has no PauseMenuController.");
        return pause;
    }
    private static ElectricBall2D Ball()
    {
        ElectricBall2D ball = Object.FindFirstObjectByType<ElectricBall2D>();
        Require(ball != null, "Current saved gameplay scene has no ElectricBall2D.");
        return ball;
    }
    private static T FindInActiveScene<T>() where T : Component
    {
        foreach (T component in ComponentsInActiveScene<T>()) return component;
        return null;
    }
    private static IEnumerable<T> ComponentsInActiveScene<T>() where T : Component
    {
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            foreach (T component in root.GetComponentsInChildren<T>(true)) yield return component;
    }
    private static FieldInfo Field(object owner, string name)
    {
        FieldInfo field = owner?.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Require(field != null, "Missing saved field " + name + ".");
        return field;
    }
    private static T Reference<T>(object owner, string field) where T : class => Field(owner, field).GetValue(owner) as T;
    private static T Value<T>(object owner, string field) => (T)Field(owner, field).GetValue(owner);
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
        RestoreKeyboard();
        Report report = Load();
        bool passed = report.errors.Count == 0 && (Phase)SessionState.GetInt(Prefix + "Phase", 0) == Phase.Finish && report.checks.Count >= 35;
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
