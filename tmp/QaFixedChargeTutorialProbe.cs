// Temporary standalone-only input/physics route probe. Copy ONLY into isolated clone Assets.
// Inert unless --qa-fixed-charge-tutorial AND --qa-report=<absolute path> are supplied.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using sung_wook;
using Object = UnityEngine.Object;

[DefaultExecutionOrder(-5000)]
public sealed class QaFixedChargeTutorialProbe : MonoBehaviour
{
    [Serializable] private sealed class Check { public string name, details; public bool passed; }
    [Serializable] private sealed class Trace
    {
        public float seconds, horizontalKey, signedCharge, lever;
        public string phase, task;
        public Vector2 position, velocity, readerMove, cameraPosition;
        public int stars, tutorialStep, spaceProgress, retryCount;
        public bool bounced, movedLeft, movedRight, cycled, dragged, contact, ballInsideCamera;
    }
    [Serializable] private sealed class PhysicsSnapshot
    {
        public float mass, gravityScale, jump, maximumSpeed, acceleration, deceleration, linearDamping;
    }
    [Serializable] private sealed class Report
    {
        public string status = "running", startedUtc, finishedUtc, unity, platform, graphics;
        public int processId, actualGroundEvents, actualContactEvents, actualStarEvents, actualGoalEvents;
        public int spacePressEdges, playerControllerUpdates, leverMoverUpdates, maximumRetryCount;
        public bool initialTransitionAccepted, physicsUnchanged = true, allBallSamplesInsideCamera = true;
        public bool observedMouseGrabOnScreen, observedMousePressEdge, resultShown, stage2Loaded, unlockedStage2;
        public float elapsedSeconds, routeSimulationSeconds;
        public Vector2 spawn, leverPosition, chargerPositionAtStart, starPosition, exitPosition;
        public float maximumSourceDisplacement;
        public Vector2 minimumBallViewport = Vector2.one, maximumBallViewport = Vector2.zero;
        public PhysicsSnapshot physics;
        public string method = "Windows standalone saved Stage_01 map. Virtual Keyboard A/D/Space and Mouse states pass through the real enabled PlayerInputReader InputActions, the actual PlayerController.Update and LeverMover.Update methods, and normal Unity physics/trigger callbacks. InputSystem manual updates are used; only automatic Update dispatch of the two controllers is disabled to prevent one Space edge being consumed twice. Their real methods are invoked exactly once per probe Update. This is scripted input validation, not human or OS input.";
        public string integrity = "No ball position, velocity, mass, gravity, jump, acceleration, deceleration, force, collider, or scene terrain writes. No fake collision/collection callbacks, no added collision fixtures, no manual StageLever.SetValue, no direct PlayerMovement.SetMovex, no tutorial flag mutation, no checkpoint requests. Normal scene initialization is used without additional RestartStage. Native screenshots show actual rendered frames. Natural progression may save unlock/mastery keys; the root QA registry backup owns restoration.";
        public List<Check> checks = new List<Check>();
        public List<Trace> trace = new List<Trace>();
        public List<string> screenshots = new List<string>(), errors = new List<string>();
    }
    private enum Phase { WaitBounce, MoveLeft, MoveRight, Settle, SpaceCycle, ApproachLever, DragCharge, Contact, DragNeutral, Star, Exit, Result, WaitStage2 }
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private string reportPath;
    private Report report;
    private float startedRealtime, routeStarted, nextTrace;
    private bool loadingRequested, startedRoute, finished, resultCaptureStarted;
    private Phase phase;
    private ElectricStageSelector selector;
    private TutorialController2D tutorial;
    private ElectricStageSession2D session;
    private ElectricStageProgression progression;
    private ElectricBall2D ball;
    private PlayerMovement movement;
    private PlayerInputReader reader;
    private PlayerController controller;
    private global::LeverMover mover;
    private Transform pivot;
    private Collider2D grab;
    private Camera leverCamera;
    private Keyboard keyboard, oldKeyboard;
    private Mouse mouse, oldMouse;
    private InputSettings.UpdateMode oldUpdateMode;
    private InputSettings.BackgroundBehavior oldBackground;
    private MethodInfo controllerUpdate, leverUpdate;
    private int dragStep, cycleFrames;
    private float input;
    private Vector2 mouseScreen;
    private bool mousePressed;
    private bool inputInitialized;
    private QaProgressPreferences progressGuard;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (Application.isEditor || Array.IndexOf(Environment.GetCommandLineArgs(), "--qa-fixed-charge-tutorial") < 0) return;
        string path = null;
        foreach (string arg in Environment.GetCommandLineArgs())
            if (arg.StartsWith("--qa-report=", StringComparison.Ordinal)) path = arg.Substring("--qa-report=".Length);
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)) return;
        var root = new GameObject("Temporary Tutorial Input Route QA");
        DontDestroyOnLoad(root);
        root.AddComponent<QaFixedChargeTutorialProbe>().Initialize(path);
    }
    private void Initialize(string path)
    {
        reportPath = path;
        progressGuard = QaProgressPreferences.Capture(path + ".ProgressBackup.json");
        startedRealtime = Time.realtimeSinceStartup;
        report = new Report { startedUtc = DateTime.UtcNow.ToString("O"), unity = Application.unityVersion,
            platform = Application.platform.ToString(), graphics = SystemInfo.graphicsDeviceName + " / " + SystemInfo.graphicsDeviceType,
            processId = System.Diagnostics.Process.GetCurrentProcess().Id };
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        Application.logMessageReceived += OnLog;
        Application.runInBackground = true;
        Application.targetFrameRate = 60;
        QualitySettings.vSyncCount = 0;
        oldKeyboard = Keyboard.current;
        oldMouse = Mouse.current;
        oldUpdateMode = InputSystem.settings.updateMode;
        oldBackground = InputSystem.settings.backgroundBehavior;
        InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        keyboard = InputSystem.AddDevice<Keyboard>("Temporary tutorial route keyboard");
        mouse = InputSystem.AddDevice<Mouse>("Temporary tutorial route mouse");
        keyboard.MakeCurrent(); mouse.MakeCurrent();
        inputInitialized = true;
        Write();
    }

    private void Update()
    {
        if (finished || report == null) return;
        try
        {
            if (Time.realtimeSinceStartup - startedRealtime > 180f) throw new TimeoutException("Tutorial route watchdog exceeded 180 real seconds.");
            if (!loadingRequested)
            {
                PumpEmpty();
                if (!SceneTransitionController.IsAvailable || SceneTransitionController.IsBusy) return;
                report.initialTransitionAccepted = SceneTransitionController.LoadScene(ElectricStageProgression.StageScenePath(1));
                Require(report.initialTransitionAccepted, "Shared tutorial scene load was rejected.");
                loadingRequested = true;
                Write();
                return;
            }
            if (phase == Phase.WaitStage2 && startedRoute)
            {
                PumpEmpty();
                if (SceneManager.GetActiveScene().name == "Stage_02" && !SceneTransitionController.IsBusy)
                {
                    report.stage2Loaded = true;
                    CheckResult("result-continue-loads-stage2", Time.timeScale == 1f
                        && Object.FindFirstObjectByType<ElectricStageSelector>()?.SelectedStage == 2,
                        "Public ContinueNextStage uses the shared asynchronous fade and restores gameplay time.");
                    Finish(null);
                }
                return;
            }
            if (SceneManager.GetActiveScene().name != "Stage_01" || SceneTransitionController.IsBusy)
            {
                PumpEmpty();
                return;
            }
            if (!startedRoute)
            {
                tutorial = Object.FindFirstObjectByType<TutorialController2D>();
                if (tutorial == null || !tutorial.IsInitialized) { PumpEmpty(); return; }
                Begin();
            }
            if (tutorial.Goal.IsCleared || phase == Phase.Result)
            {
                phase = Phase.Result;
                PumpEmpty();
                Observe();
                if (!resultCaptureStarted) { resultCaptureStarted = true; StartCoroutine(ResultThenContinue()); }
                return;
            }
            if (Time.timeScale <= 0f) { PumpEmpty(); return; }
            bool pressSpace = Drive();
            Dispatch(pressSpace);
            Observe();
        }
        catch (Exception error) { Finish(error); }
    }
    private void Begin()
    {
        selector = tutorial.GetComponent<ElectricStageSelector>();
        session = tutorial.GetComponent<ElectricStageSession2D>();
        progression = tutorial.GetComponent<ElectricStageProgression>();
        ball = tutorial.Ball;
        movement = ball.GetComponent<PlayerMovement>();
        reader = ball.GetComponent<PlayerInputReader>();
        controller = ball.GetComponent<PlayerController>();
        mover = tutorial.Lever.GetComponentInChildren<global::LeverMover>(true);
        Require(selector.SelectedStage == 1 && session.IsInitialized && progression != null && mover != null,
            "Tutorial split-scene bindings are not ready.");
        pivot = Field<Transform>(mover, "pivot");
        grab = Field<Collider2D>(mover, "grabArea");
        leverCamera = Field<Camera>(mover, "cam");
        Require(pivot != null && grab != null && leverCamera != null && reader.isActiveAndEnabled,
            "Actual lever or enabled InputReader references are missing.");
        controllerUpdate = typeof(PlayerController).GetMethod("Update", Fields);
        leverUpdate = typeof(global::LeverMover).GetMethod("Update", Fields);
        Require(controllerUpdate != null && leverUpdate != null, "Production input Update methods are missing.");
        // Prevent automatic controller dispatch from consuming a Space edge twice. The reader's real actions remain enabled.
        controller.enabled = false;
        mover.enabled = false;
        report.physics = Snapshot();
        report.spawn = tutorial.SpawnPosition;
        report.leverPosition = tutorial.LeverPosition;
        report.chargerPositionAtStart = tutorial.ContactPosition;
        report.starPosition = tutorial.Star.transform.position;
        report.exitPosition = tutorial.Goal.transform.position;
        movement.LandedOnGround += OnGround;
        tutorial.Charger.ChargedFromNeutral += OnContact;
        tutorial.Star.OnCollected.AddListener(OnStar);
        tutorial.Goal.OnCleared.AddListener(OnGoal);
        routeStarted = Time.fixedTime;
        nextTrace = routeStarted;
        phase = Phase.WaitBounce;
        startedRoute = true;
        mouseScreen = Vector2.zero;
        CheckResult("tutorial-ready-on-authored-map", selector.SelectedStage == 1 && reader.isActiveAndEnabled
            && !Field<bool>(tutorial.Lever, "useManualValue"), "Actual hardware LeverReader and saved player are used; no manual lever override.");
        StartCoroutine(Capture("tutorial-start"));
        Write();
    }

    private bool Drive()
    {
        bool pressSpace = false;
        mousePressed = false;
        switch (phase)
        {
            case Phase.WaitBounce:
                input = 0f;
                if (tutorial.HasBounced) Change(Phase.MoveLeft);
                break;
            case Phase.MoveLeft:
                Steer(report.spawn.x - 1f);
                if (tutorial.HasMovedLeft) Change(Phase.MoveRight);
                break;
            case Phase.MoveRight:
                Steer(report.spawn.x + .5f);
                if (tutorial.HasMovedRight) Change(Phase.Settle);
                break;
            case Phase.Settle:
                Steer(report.spawn.x);
                if (Mathf.Abs(ball.Body.position.x - report.spawn.x) < .15f && Mathf.Abs(ball.Body.linearVelocity.x) < .25f)
                {
                    cycleFrames = 0;
                    Change(Phase.SpaceCycle);
                }
                break;
            case Phase.SpaceCycle:
                Steer(report.spawn.x);
                if (tutorial.HasCycledCharge) Change(Phase.ApproachLever);
                else { pressSpace = cycleFrames % 3 == 0; cycleFrames++; }
                break;
            case Phase.ApproachLever:
                Steer(report.leverPosition.x + .7f);
                if (Mathf.Abs(ball.Body.position.x - (report.leverPosition.x + .7f)) < .25f && Mathf.Abs(ball.Body.linearVelocity.x) < .4f)
                {
                    dragStep = 0;
                    Change(Phase.DragCharge);
                }
                break;
            case Phase.DragCharge:
                Steer(report.leverPosition.x + .7f);
                if (Drag(false))
                {
                    CheckResult("actual-mouse-drag-charges-nearby-station", tutorial.HasDraggedLever && tutorial.Lever.SignedValue > .9f
                        && report.observedMouseGrabOnScreen && report.observedMousePressEdge,
                        "Actual collider grab/drag → LeverMover → LeverReader; signed=" + tutorial.Lever.SignedValue);
                    StartCoroutine(Capture("tutorial-lever"));
                    Change(Phase.Contact);
                }
                break;
            case Phase.Contact:
                Steer(tutorial.ContactPosition.x);
                if (tutorial.HasContactCharged)
                {
                    CheckResult("actual-neutral-contact-charges-ball", report.actualContactEvents > 0 && ball.SignedCharge > 0f,
                        "Natural ContactCharger collision event; signed charge=" + ball.SignedCharge);
                    dragStep = 0;
                    Change(Phase.DragNeutral);
                }
                break;
            case Phase.DragNeutral:
                // Centering the same released lever is a normal mouse action; it removes continuous repulsion before walking left.
                input = 0f;
                if (Drag(true))
                {
                    CheckResult("actual-mouse-centering-removes-source-charge", Mathf.Abs(tutorial.Lever.SignedValue) < .001f,
                        "Mouse returns the existing lever above its pivot; source charge=" + tutorial.Charger.SignedCharge);
                    Change(Phase.Star);
                }
                break;
            case Phase.Star:
                Steer(tutorial.Star.transform.position.x);
                if (tutorial.HasCollectedStar) Change(Phase.Exit);
                break;
            case Phase.Exit:
                Steer(tutorial.Goal.transform.position.x);
                break;
        }
        return pressSpace;
    }
    private bool Drag(bool neutral)
    {
        Vector3 target = dragStep < 2 ? grab.bounds.center : pivot.position + (neutral ? Vector3.up : Vector3.left) * 2f;
        Vector3 screen = leverCamera.WorldToScreenPoint(target);
        mouseScreen = new Vector2(screen.x, screen.y);
        mousePressed = dragStep >= 1 && dragStep <= 3;
        if (dragStep == 1)
            report.observedMouseGrabOnScreen |= screen.z > 0f && screen.x >= 0f && screen.x < Screen.width && screen.y >= 0f && screen.y < Screen.height;
        dragStep++;
        // Dispatch occurs after Drive; step 4 must be dispatched to release before advancing next frame.
        return dragStep > 5;
    }
    private void Steer(float target)
    {
        float error = target - ball.Body.position.x, velocity = ball.Body.linearVelocity.x;
        float cap = Mathf.Min(report.physics.maximumSpeed, Mathf.Sqrt(2f * report.physics.acceleration * Mathf.Abs(error)));
        float desired = Mathf.Clamp(error * 2.5f - velocity * .45f, -cap, cap);
        float difference = desired - velocity;
        input = Mathf.Abs(difference) > .15f ? Mathf.Sign(difference) : 0f;
        if (Mathf.Abs(error) < .08f && Mathf.Abs(velocity) < .15f) input = 0f;
    }
    private void Dispatch(bool space)
    {
        var keys = new List<Key>();
        if (input < -.1f) keys.Add(Key.A); else if (input > .1f) keys.Add(Key.D);
        if (space) keys.Add(Key.Space);
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys.ToArray()));
        InputSystem.QueueStateEvent(mouse, new MouseState { position = mouseScreen, buttons = (ushort)(mousePressed ? 1 : 0) });
        InputSystem.Update();
        keyboard.MakeCurrent(); mouse.MakeCurrent();
        if (keyboard.spaceKey.wasPressedThisFrame) report.spacePressEdges++;
        if (mouse.leftButton.wasPressedThisFrame) report.observedMousePressEdge = true;
        controllerUpdate.Invoke(controller, null);
        leverUpdate.Invoke(mover, null);
        report.playerControllerUpdates++;
        report.leverMoverUpdates++;
    }
    private void PumpEmpty()
    {
        if (!inputInitialized) return;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        InputSystem.QueueStateEvent(mouse, new MouseState { position = mouseScreen, buttons = 0 });
        InputSystem.Update();
        keyboard.MakeCurrent(); mouse.MakeCurrent();
    }
    private void Change(Phase value)
    {
        phase = value;
        Write();
    }
    private void Observe()
    {
        if (!startedRoute || ball == null) return;
        report.maximumSourceDisplacement = Mathf.Max(report.maximumSourceDisplacement, Vector2.Distance(tutorial.ContactPosition, report.chargerPositionAtStart));
        Require(report.maximumSourceDisplacement <= .0001f, "Tutorial electrode moved while the real lever was dragged.");
        PhysicsSnapshot physics = Snapshot();
        report.physicsUnchanged &= SamePhysics(report.physics, physics);
        Require(report.physicsUnchanged, "Saved player physics changed during the input route.");
        report.maximumRetryCount = Mathf.Max(report.maximumRetryCount, session.RetryCount);
        Require(report.maximumRetryCount == 0, "A manual or automatic checkpoint retry occurred; this is not a clean default route.");
        report.routeSimulationSeconds = Time.fixedTime - routeStarted;
        Require(ball.Body.position.y >= -10f && Mathf.Abs(ball.Body.position.x) < 25f, "Tutorial route left the authored play area.");
        Camera camera = Camera.main;
        bool inside = true;
        if (camera != null)
        {
            Bounds bounds = ball.GetComponent<Collider2D>().bounds;
            Vector3 min = camera.WorldToViewportPoint(bounds.min), max = camera.WorldToViewportPoint(bounds.max);
            report.minimumBallViewport = Vector2.Min(report.minimumBallViewport, new Vector2(min.x, min.y));
            report.maximumBallViewport = Vector2.Max(report.maximumBallViewport, new Vector2(max.x, max.y));
            inside = min.z > 0f && min.x >= -.01f && min.y >= -.01f && max.x <= 1.01f && max.y <= 1.01f;
            report.allBallSamplesInsideCamera &= inside;
        }
        if (Time.fixedTime < nextTrace) return;
        nextTrace = Time.fixedTime + .15f;
        report.trace.Add(new Trace {
            seconds = report.routeSimulationSeconds, phase = phase.ToString(), task = tutorial.CurrentTaskText,
            position = ball.Body.position, velocity = ball.Body.linearVelocity, readerMove = reader.MoveVector,
            cameraPosition = camera != null ? (Vector2)camera.transform.position : Vector2.zero,
            horizontalKey = input, signedCharge = ball.SignedCharge, lever = tutorial.Lever.SignedValue,
            stars = ball.Stars, tutorialStep = tutorial.StepIndex, spaceProgress = tutorial.SpaceCycleProgress,
            retryCount = session.RetryCount, bounced = tutorial.HasBounced, movedLeft = tutorial.HasMovedLeft,
            movedRight = tutorial.HasMovedRight, cycled = tutorial.HasCycledCharge,
            dragged = tutorial.HasDraggedLever, contact = tutorial.HasContactCharged, ballInsideCamera = inside
        });
        Write();
    }
    private PhysicsSnapshot Snapshot() => new PhysicsSnapshot {
        mass = ball.Body.mass, gravityScale = ball.Body.gravityScale, linearDamping = ball.Body.linearDamping,
        jump = Field<float>(movement, "jumpForce"), maximumSpeed = Field<float>(movement, "maxMoveSpeed"),
        acceleration = Field<float>(movement, "acceleration"), deceleration = Field<float>(movement, "deceleration")
    };
    private static bool SamePhysics(PhysicsSnapshot a, PhysicsSnapshot b) => Mathf.Approximately(a.mass, b.mass)
        && Mathf.Approximately(a.gravityScale, b.gravityScale) && Mathf.Approximately(a.linearDamping, b.linearDamping)
        && Mathf.Approximately(a.jump, b.jump) && Mathf.Approximately(a.maximumSpeed, b.maximumSpeed)
        && Mathf.Approximately(a.acceleration, b.acceleration) && Mathf.Approximately(a.deceleration, b.deceleration);
    private void OnGround(Vector2 position) => report.actualGroundEvents++;
    private void OnContact(ElectricBall2D charged) { if (charged == ball) report.actualContactEvents++; }
    private void OnStar() => report.actualStarEvents++;
    private void OnGoal() { report.actualGoalEvents++; Write(); }

    private IEnumerator ResultThenContinue()
    {
        yield return null;
        try
        {
            ElectricGameCompleteView view = Object.FindFirstObjectByType<ElectricGameCompleteView>();
            report.resultShown = view != null && Property<bool>(view, "IsResultOpen")
                && Property<int>(view, "CompletedStage") == 1 && Property<bool>(progression, "IsAwaitingContinue");
            MethodInfo unlocked = typeof(ElectricStageProgression).GetMethod("IsStageUnlocked", BindingFlags.Static | BindingFlags.Public);
            Require(unlocked != null, "Production IsStageUnlocked API is missing.");
            report.unlockedStage2 = (bool)unlocked.Invoke(null, new object[] { 2 });
            CheckResult("natural-tutorial-and-goal-events", tutorial.IsComplete && tutorial.HasBounced && tutorial.HasMovedLeft
                && tutorial.HasMovedRight && tutorial.HasCycledCharge && tutorial.HasDraggedLever && tutorial.HasContactCharged
                && tutorial.HasCollectedStar && report.actualGroundEvents > 0 && report.actualContactEvents > 0
                && report.actualStarEvents == 1 && report.actualGoalEvents == 1,
                "Actual input prerequisites and physical ground/contact/star/goal events all completed.");
            CheckResult("space-input-cycle-real-reader-path", report.spacePressEdges >= 3 && tutorial.SpaceCycleProgress == 3,
                "Virtual input press edges=" + report.spacePressEdges + "; tutorial observed actual +/−/0 cycle.");
            CheckResult("saved-physics-and-clean-route", report.physicsUnchanged && report.maximumRetryCount == 0,
                "Saved mass/gravity/jump/speed/acceleration/deceleration/damping remained unchanged; retries=0.");
            CheckResult("camera-keeps-ball-inside-view", report.allBallSamplesInsideCamera,
                "Collider viewport min=" + report.minimumBallViewport + ", max=" + report.maximumBallViewport + ".");
            CheckResult("natural-goal-opens-result-and-unlocks-stage2", report.resultShown && report.unlockedStage2 && Time.timeScale == 0f,
                "Natural trigger, not a ShowStageResult fixture; result is open and tutorial completion unlocks Stage 02.");
            Require(report.resultShown, "Natural tutorial goal did not open the authored result UI.");
        }
        catch (Exception error) { Finish(error); yield break; }
        yield return Capture("tutorial-result");
        if (finished) yield break;
        try
        {
            MethodInfo continueNext = progression.GetType().GetMethod("ContinueNextStage", BindingFlags.Instance | BindingFlags.Public);
            Require(continueNext != null, "Production ContinueNextStage API is missing.");
            continueNext.Invoke(progression, null);
            CheckResult("result-continue-request-accepted", SceneTransitionController.IsBusy,
                "Public ContinueNextStage accepted while the result owns the pause.");
            phase = Phase.WaitStage2;
            Write();
        }
        catch (Exception error) { Finish(error); }
    }
    private IEnumerator Capture(string label)
    {
        if (Application.isBatchMode) yield break;
        yield return new WaitForEndOfFrame();
        try
        {
            Debug.developerConsoleVisible = false;
            Texture2D picture = ScreenCapture.CaptureScreenshotAsTexture();
            Require(picture != null, "Actual ScreenCapture failed.");
            string path = Path.Combine(Path.GetDirectoryName(reportPath), Path.GetFileNameWithoutExtension(reportPath) + "-" + label + ".png");
            File.WriteAllBytes(path, picture.EncodeToPNG());
            Object.Destroy(picture);
            report.screenshots.Add(path);
            Write();
        }
        catch (Exception error) { Finish(error); }
    }
    private static T Field<T>(object target, string name)
    {
        Require(target != null, "Missing target when inspecting " + name);
        FieldInfo field = target.GetType().GetField(name, Fields);
        Require(field != null, "Missing saved field " + target.GetType().Name + "." + name);
        return (T)field.GetValue(target);
    }
    private static T Property<T>(object target, string name)
    {
        Require(target != null, "Missing property owner " + name);
        PropertyInfo property = target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
        Require(property != null, "Missing public production property " + name);
        return (T)property.GetValue(target, null);
    }
    private void CheckResult(string name, bool passed, string details)
    {
        report.checks.Add(new Check { name = name, passed = passed, details = details });
        Write();
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private void OnLog(string message, string stack, LogType type)
    {
        if (!finished && (type == LogType.Error || type == LogType.Exception || type == LogType.Assert))
        { report.errors.Add(message + "\n" + stack); Write(); }
    }
    private void Write()
    {
        report.elapsedSeconds = Time.realtimeSinceStartup - startedRealtime;
        File.WriteAllText(reportPath, JsonUtility.ToJson(report, true));
    }
    private void Finish(Exception error)
    {
        if (finished) return;
        finished = true;
        if (error != null) report.errors.Add(error.ToString());
        try { progressGuard?.Restore(); } catch (Exception restoreError) { report.errors.Add(restoreError.ToString()); }
        Application.logMessageReceived -= OnLog;
        if (movement != null) movement.LandedOnGround -= OnGround;
        if (tutorial != null && tutorial.Charger != null) tutorial.Charger.ChargedFromNeutral -= OnContact;
        if (inputInitialized)
        {
            InputSystem.RemoveDevice(keyboard);
            InputSystem.RemoveDevice(mouse);
            if (oldKeyboard != null && oldKeyboard.added) oldKeyboard.MakeCurrent();
            if (oldMouse != null && oldMouse.added) oldMouse.MakeCurrent();
            InputSystem.settings.updateMode = oldUpdateMode;
            InputSystem.settings.backgroundBehavior = oldBackground;
        }
        report.status = report.errors.Count == 0 && report.stage2Loaded && report.resultShown
            && !report.checks.Exists(c => !c.passed) ? "passed" : "failed";
        report.finishedUtc = DateTime.UtcNow.ToString("O");
        Write();
        Application.Quit(report.status == "passed" ? 0 : 1);
    }
}

