// Clone-only opt-in play validation. Excluded from ordinary releases at compilation.
#if SCIENCE_CAMPAIGN_QA
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using sung_wook;
using Object = UnityEngine.Object;

[DefaultExecutionOrder(-1000)]
public sealed class ScienceCampaignRouteProbe : MonoBehaviour
{
    [Serializable] sealed class Trace { public float time, input, charge, voltage; public string phase, task; public Vector2 position, velocity, force; public bool inField; }
    [Serializable] sealed class Route
    {
        public int stage, groundEvents, contactEvents, starEvents, goalEvents, controllerUpdates, spaceEdges, retryCount;
        public string status = "running", details, phase;
        public float seconds, peakY, highestEdge, cameraTop, sourceDisplacement, launchVoltage;
        public float mass, gravityScale, jump, speed, acceleration, deceleration, damping;
        public Vector2 start, finish;
        public bool physicsUnchanged = true, educationSatisfied, resultShown, firstLanding, secondLanding;
        public bool tutorialBounce, tutorialLeft, tutorialRight, tutorialChargeCycle;
        public bool fieldAscent, neutralDescent, negativeBrake;
        public float fieldAscentMeters, neutralDescentMeters, negativeBrakeSeconds;
        public int upperPlateContacts;
        public bool plateClearanceValid = true;
        public float maximumPlatePenetration;
        public List<string> landedSurfaces = new List<string>();
        public List<string> solidTerrain = new List<string>();
        public List<Trace> trace = new List<Trace>();
    }
    [Serializable] sealed class Report
    {
        public string status = "running", startedUtc, finishedUtc, unity, graphics;
        public string method = "Saved Windows player scenes. Virtual Keyboard A/D/Space -> enabled PlayerInputReader actions -> actual PlayerController.Update once per rendered frame. Lesson/lever public commands for experiments. Normal Unity physics, real ground/contact/star/goal events, normal result continuation. Scripted input validation, not a human playthrough.";
        public string integrity = "No player position/velocity/force/mass/gravity/jump changes, teleports, synthetic collision events, tutorial flag mutations, invisible collision fixtures, or progress bypasses. Saved spawn initializes normally once per scene. Only QA compilation defines include this code. Progress keys restored and verified.";
        public int firstStage, lastStage, continues;
        public bool alternateComparison, initialTransitionAccepted, finalComplete, progressRestored;
        public float elapsedSeconds;
        public bool recordRequested;
        public bool recordedPauseVerified;
        public int recordedFrames, recordWidth, recordHeight;
        public string recordFolder;
        public List<float> recordFrameTimes = new List<float>();
        public List<Route> routes = new List<Route>();
        public List<string> screenshots = new List<string>(), errors = new List<string>();
    }
    enum Phase { Bounce, Left, Right, Settle, Cycle, Contact, Baseline, Change, Launch, Charge, FirstPad, SecondPad, FieldRise, FieldStar, FieldFall, FieldBrake, Star, Exit, Result }
    static readonly BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    Report report;
    Route route;
    string path;
    int expectedStage, cycleFrames;
    bool loading, finished, resultHandled, inputReady;
    float started, stageStarted, phaseStarted, nextTrace, input, ascentStart = float.NaN, descentStart = float.NaN, brakeRun;
    Phase phase;
    ElectricStageSelector selector;
    ElectricStageSession2D session;
    ElectricLearningController2D lesson;
    TutorialController2D tutorial;
    PlayerMovement movement;
    PlayerController controller;
    ElectricStar2D star;
    ElectricGoal2D goal;
    Transform source;
    Collider2D fieldCeiling, fieldFloor;
    Vector3 sourceStart;
    MethodInfo controllerUpdate;
    Keyboard keyboard, previousKeyboard;
    InputSettings.UpdateMode previousUpdateMode;
    InputSettings.BackgroundBehavior previousBackground;
    readonly ContactPoint2D[] contacts = new ContactPoint2D[32];
    ScienceCampaignProgress progress;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
        if (Application.isEditor || Array.IndexOf(Environment.GetCommandLineArgs(), "--qa-science-campaign") < 0) return;
        string destination = null; int stage = 0; bool alternate = false, record = false;
        foreach (string arg in Environment.GetCommandLineArgs())
        {
            if (arg.StartsWith("--qa-report=")) destination = arg.Substring(12);
            if (arg.StartsWith("--qa-stage=")) int.TryParse(arg.Substring(11), out stage);
            if (arg == "--qa-plan-alt") alternate = true;
            if (arg == "--qa-record") record = true;
        }
        if (string.IsNullOrWhiteSpace(destination) || !Path.IsPathRooted(destination)) throw new InvalidOperationException("Absolute --qa-report path required.");
        var owner = new GameObject("Temporary Science Campaign Input QA"); DontDestroyOnLoad(owner);
        owner.AddComponent<ScienceCampaignRouteProbe>().Initialize(destination, stage, alternate, record);
    }
    void Initialize(string destination, int stage, bool alternate, bool record)
    {
        path = destination; started = Time.realtimeSinceStartup;
        progress = ScienceCampaignProgress.Capture(path + ".ProgressBackup.json");
        report = new Report { startedUtc = DateTime.UtcNow.ToString("O"), unity = Application.unityVersion,
            graphics = SystemInfo.graphicsDeviceName + " / " + SystemInfo.graphicsDeviceType,
            firstStage = stage >= 1 && stage <= 5 ? stage : 1, lastStage = stage >= 1 && stage <= 5 ? stage : 5,
            alternateComparison = alternate, recordRequested = record };
        expectedStage = report.firstStage;
        previousKeyboard = Keyboard.current; previousUpdateMode = InputSystem.settings.updateMode;
        previousBackground = InputSystem.settings.backgroundBehavior;
        InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        keyboard = InputSystem.AddDevice<Keyboard>("Science campaign scripted keyboard"); keyboard.MakeCurrent(); inputReady = true;
        Application.runInBackground = true; Application.targetFrameRate = 60; QualitySettings.vSyncCount = 0;
        Application.logMessageReceived += OnLog; Write();
        if (record && !Application.isBatchMode && SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
        {
            report.recordFolder = Path.Combine(Path.GetDirectoryName(path), "DemoFrames", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
            Directory.CreateDirectory(report.recordFolder); StartCoroutine(RecordFrames());
        }
    }
    void Update()
    {
        if (finished || report == null) return;
        try
        {
            Require(Time.realtimeSinceStartup - started < 400f, "Campaign watchdog exceeded 400 seconds.");
            if (report.errors.Count > 0) throw new InvalidOperationException("Runtime errors reported.");
            if (!loading)
            {
                Pump(false, false);
                if (!SceneTransitionController.IsAvailable || SceneTransitionController.IsBusy) return;
                report.initialTransitionAccepted = SceneTransitionController.LoadScene(ElectricStageProgression.StageScenePath(expectedStage));
                Require(report.initialTransitionAccepted, "Initial shared scene transition rejected."); loading = true; Write(); return;
            }
            if (SceneTransitionController.IsBusy) { Pump(false, false); return; }
            if (route == null)
            {
                if (SceneManager.GetActiveScene().path != ElectricStageProgression.StageScenePath(expectedStage)) { Pump(false, false); return; }
                selector = Object.FindFirstObjectByType<ElectricStageSelector>();
                session = selector != null ? selector.GetComponent<ElectricStageSession2D>() : null;
                if (session == null || !session.IsInitialized) { Pump(false, false); return; }
                tutorial = selector.GetComponent<TutorialController2D>();
                if (expectedStage == 1 && (tutorial == null || !tutorial.IsInitialized)) { Pump(false, false); return; }
                Begin();
            }
            if (phase == Phase.Result)
            {
                input = 0f; Pump(false, false);
                if (!resultHandled) { resultHandled = true; StartCoroutine(ResultThenContinue()); }
                return;
            }
            if (Time.timeScale <= 0f) { input = 0f; Pump(false, false); return; }
            bool space = Drive(); Pump(space, true);
        }
        catch (Exception error) { Finish(error); }
    }
    void FixedUpdate()
    {
        if (finished || route == null || phase == Phase.Result || SceneTransitionController.IsBusy) return;
        try
        {
            // Charge input is sampled before every real physics step, including slow recorded frames.
            // This uses the same public lever command; it never writes stored voltage or player physics.
            if (route.stage == 5 && phase == Phase.Charge)
            {
                VoltageLauncher2D voltage = selector.GetStageComponent<VoltageLauncher2D>(5);
                float remaining = 50f - voltage.Voltage;
                selector.Lever.SetValue(lesson.VoltageChallengeSatisfied || remaining <= 0f ? 0f
                    : Mathf.Clamp(remaining / (Field<float>(voltage, "chargeRate") * Time.fixedDeltaTime), .0101f, 1f));
            }
            Observe();
        }
        catch (Exception error) { Finish(error); }
    }
    void Begin()
    {
        lesson = selector.GetComponent<ElectricLearningController2D>(); movement = selector.Ball.GetComponent<PlayerMovement>();
        controller = selector.Ball.GetComponent<PlayerController>(); controllerUpdate = typeof(PlayerController).GetMethod("Update", Fields);
        Require(lesson != null && movement != null && controller != null && controllerUpdate != null, "Required player lesson/input components missing.");
        Require(selector.Ball.GetComponent<PlayerInputReader>().isActiveAndEnabled, "Saved InputActions are disabled.");
        controller.enabled = false; // Actual method dispatched exactly once after each virtual input pump.
        star = selector.GetStageComponent<ElectricStar2D>(expectedStage); goal = selector.GetStageComponent<ElectricGoal2D>(expectedStage);
        Require(star != null && goal != null && Camera.main != null, "Saved star, goal or camera missing.");
        Rigidbody2D body = selector.Ball.Body;
        route = new Route { stage = expectedStage, start = body.position, finish = body.position, peakY = body.position.y,
            cameraTop = Camera.main.transform.position.y + Camera.main.orthographicSize,
            mass = body.mass, gravityScale = body.gravityScale, damping = body.linearDamping,
            jump = Field<float>(movement, "jumpForce"), speed = Field<float>(movement, "maxMoveSpeed"),
            acceleration = Field<float>(movement, "acceleration"), deceleration = Field<float>(movement, "deceleration") };
        report.routes.Add(route); stageStarted = Time.fixedTime; nextTrace = 0f; resultHandled = false;
        foreach (GameObject root in selector.gameObject.scene.GetRootGameObjects())
            foreach (Collider2D solid in root.GetComponentsInChildren<Collider2D>())
            {
                if (!solid.enabled || solid.isTrigger || solid.GetComponent<Tilemap>() == null) continue;
                bool authored = false;
                for (Transform parent = solid.transform; parent != null; parent = parent.parent) authored |= parent.name.StartsWith("Science Lab ", StringComparison.Ordinal);
                route.solidTerrain.Add(solid.transform.parent.name + "/" + solid.name + ":" + solid.GetType().Name + ":shapes=" + solid.shapeCount);
                Require(authored, "Legacy terrain collider remains active behind the redesigned map: " + solid.name);
                if (solid is CompositeCollider2D composite) Require(composite.pathCount > 0, "Authored terrain has no actual composite paths: " + solid.transform.parent.name);
            }
        Require(route.solidTerrain.Count > 0, "No authored solid tilemap surfaces found.");
        source = null; fieldCeiling = fieldFloor = null; ascentStart = descentStart = float.NaN; brakeRun = 0f;
        Route observed = route;
        movement.LandedOnGround += _ => observed.groundEvents++;
        star.OnCollected.AddListener(() => observed.starEvents++);
        goal.OnCleared.AddListener(() => { observed.goalEvents++; if (route == observed) CompleteRoute(); });
        if (expectedStage == 2)
        {
            ContactCharger2D charger = selector.GetStageComponent<ContactCharger2D>(2);
            Require(charger != null, "Saved contact charger missing."); source = charger.transform;
            charger.ChargedFromNeutral += _ => observed.contactEvents++;
            selector.Lever.SetValue(1f);
        }
        if (expectedStage == 3)
        {
            source = Field<Transform>(selector.GetStageComponent<DistanceLauncher2D>(3), "source");
            selector.Lever.SetValue(report.alternateComparison ? 1f : 0f);
            if (report.alternateComparison) lesson.SetBallCharge(.5f);
        }
        if (source != null) sourceStart = source.position;
        if (expectedStage == 4)
        {
            selector.Lever.SetValue(0f); lesson.SetBallCharge(-1f);
            UniformField2D field = selector.GetStageComponent<UniformField2D>(4);
            fieldCeiling = Field<Transform>(field, "upperPlate").GetComponent<Collider2D>();
            fieldFloor = Field<Transform>(field, "lowerPlate").GetComponent<Collider2D>();
            Require(fieldCeiling != null && fieldCeiling.enabled && !fieldCeiling.isTrigger
                && fieldFloor != null && fieldFloor.enabled && !fieldFloor.isTrigger, "Visible field plates require real solid bounds.");
        }
        var plan = selector.GetComponent<ElectricExperimentPlan2D>();
        if (plan != null && plan.ShowPlans) Require(plan.SelectPlan(expectedStage == 3 && report.alternateComparison ? 1 : 0), "Actual experiment plan selection rejected.");
        cycleFrames = 0; input = 0f;
        Change(expectedStage == 1 ? Phase.Bounce : expectedStage == 2 ? Phase.Contact : expectedStage == 3 ? Phase.Baseline : expectedStage == 4 ? Phase.FieldRise : Phase.Charge);
        StartCoroutine(Capture("stage" + expectedStage + "-start")); Write();
        if (report.recordRequested && expectedStage == 3) StartCoroutine(RecordPausePreview());
    }
    bool Drive()
    {
        Rigidbody2D body = selector.Ball.Body; bool space = false;
        switch (phase)
        {
            case Phase.Bounce: input = 0f; if (tutorial.HasBounced) Change(Phase.Left); break;
            case Phase.Left: Steer(route.start.x - 1f); if (tutorial.HasMovedLeft) Change(Phase.Right); break;
            case Phase.Right: Steer(route.start.x + .5f); if (tutorial.HasMovedRight) Change(Phase.Settle); break;
            case Phase.Settle:
                Steer(route.start.x); if (Mathf.Abs(body.position.x - route.start.x) < .15f && Mathf.Abs(body.linearVelocity.x) < .25f) Change(Phase.Cycle); break;
            case Phase.Cycle:
                Steer(route.start.x); if (tutorial.HasCycledCharge) Change(Phase.Star); else space = cycleFrames++ % 3 == 0; break;
            case Phase.Contact:
                Steer(source.position.x);
                if (lesson.ContactChallengeSatisfied) { selector.Lever.SetValue(.5f); Change(Phase.Star); }
                break;
            case Phase.Baseline:
                input = 0f; if (Time.fixedTime - phaseStarted < .16f) break;
                lesson.RecordBaseline(); Require(lesson.HasBaseline, "Comparison baseline rejected at saved spawn.");
                if (report.alternateComparison) lesson.SetBallCharge(1f); else selector.Lever.SetValue(1f);
                Change(Phase.Change); break;
            case Phase.Change:
                input = 0f; if (Time.fixedTime - phaseStarted < .16f) break;
                lesson.RequestLaunch(); Change(Phase.Launch); break;
            case Phase.Launch:
                input = 0f; if (lesson.ComparisonChallengeSatisfied) Change(Phase.FirstPad); break;
            case Phase.Charge:
                Steer(route.start.x);
                if (lesson.VoltageChallengeSatisfied) { route.launchVoltage = lesson.LastLaunchVoltage; Change(Phase.FirstPad); break; }
                VoltageLauncher2D voltage = selector.GetStageComponent<VoltageLauncher2D>(5);
                if (voltage.Voltage < 50f) break;
                if (Field<BoxCollider2D>(voltage, "launchZone").OverlapPoint(body.worldCenterOfMass)) lesson.RequestLaunch();
                break;
            case Phase.FirstPad:
                // Enter the taller pad only when the bottom of the ball has cleared its vertical wall.
                if (route.stage == 5) Steer(body.position.y < .6f && body.position.x < -1.5f ? -1.65f : .5f);
                else Steer(1.8f);
                if (route.firstLanding) Change(Phase.SecondPad); break;
            case Phase.SecondPad:
                Steer(route.stage == 5 ? 4.5f : 5.5f);
                if (route.secondLanding && star.IsCollected) Change(Phase.Exit);
                else if (route.secondLanding) Change(Phase.Star);
                break;
            case Phase.FieldRise:
                Steer(route.start.x);
                if (lesson.HasFieldAscent && body.position.y >= -.7f) Change(Phase.FieldStar); break;
            case Phase.FieldStar:
                Steer(star.transform.position.x);
                if (star.IsCollected) { lesson.SetBallCharge(0f); Change(Phase.FieldFall); StartCoroutine(Capture("stage4-neutral")); }
                break;
            case Phase.FieldFall:
                Steer(star.transform.position.x);
                if (lesson.HasNeutralFieldDescent && body.linearVelocity.y < -.25f) { lesson.SetBallCharge(-1f); Change(Phase.FieldBrake); }
                break;
            case Phase.FieldBrake:
                Steer(star.transform.position.x);
                if (lesson.HasRestoredNegativeField) { Change(Phase.Exit); StartCoroutine(Capture("stage4-brake")); }
                break;
            case Phase.Star:
                if (route.stage == 2 && body.position.x > source.position.x + 1.25f && selector.Ball.CurrentPolarity != Polarity.Neutral) lesson.SetBallCharge(0f);
                Steer(star.transform.position.x); if (star.IsCollected) Change(Phase.Exit); break;
            case Phase.Exit: Steer(goal.transform.position.x); break;
        }
        return space;
    }
    void Steer(float target)
    {
        float error = target - selector.Ball.Body.position.x, velocity = selector.Ball.Body.linearVelocity.x;
        float cap = Mathf.Min(route.speed, Mathf.Sqrt(2f * route.acceleration * Mathf.Abs(error)));
        float desired = Mathf.Clamp(error * 2.5f - velocity * .45f, -cap, cap);
        input = Mathf.Abs(desired - velocity) > .15f ? Mathf.Sign(desired - velocity) : 0f;
        if (Mathf.Abs(error) < .08f && Mathf.Abs(velocity) < .15f) input = 0f;
    }
    void Pump(bool space, bool dispatch)
    {
        if (!inputReady) return;
        var keys = new List<Key>();
        if (dispatch && input < -.1f) keys.Add(Key.A); else if (dispatch && input > .1f) keys.Add(Key.D);
        if (dispatch && space) keys.Add(Key.Space);
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys.ToArray())); InputSystem.Update(); keyboard.MakeCurrent();
        if (dispatch && controller != null)
        {
            if (keyboard.spaceKey.wasPressedThisFrame) route.spaceEdges++;
            controllerUpdate.Invoke(controller, null); route.controllerUpdates++;
        }
    }
    void Change(Phase value) { phase = value; phaseStarted = Time.fixedTime; if (route != null) route.phase = value.ToString(); Write(); }
    void Observe()
    {
        Rigidbody2D body = selector.Ball.Body;
        route.seconds = Time.fixedTime - stageStarted; route.finish = body.position; route.peakY = Mathf.Max(route.peakY, body.position.y);
        route.highestEdge = Mathf.Max(route.highestEdge, selector.Ball.GetComponent<Collider2D>().bounds.max.y);
        route.retryCount = session.RetryCount; route.educationSatisfied = lesson.CurrentLearningGoalSatisfied;
        route.physicsUnchanged &= body.mass == route.mass && body.gravityScale == route.gravityScale && body.linearDamping == route.damping
            && Field<float>(movement, "jumpForce") == route.jump && Field<float>(movement, "maxMoveSpeed") == route.speed
            && Field<float>(movement, "acceleration") == route.acceleration && Field<float>(movement, "deceleration") == route.deceleration;
        Require(route.physicsUnchanged, "Saved player physics changed during route.");
        Require(body.position.y > -9f && Mathf.Abs(body.position.x) < 20f && route.seconds < 85f, "Route fell outside level or exceeded 85 simulation seconds.");
        if (source != null) { route.sourceDisplacement = Mathf.Max(route.sourceDisplacement, Vector3.Distance(source.position, sourceStart)); Require(route.sourceDisplacement < .0001f, "Fixed electrode moved."); }
        int count = body.GetContacts(contacts);
        for (int i = 0; i < count; i++)
        {
            ContactPoint2D contact = contacts[i];
            Collider2D other = contact.collider.attachedRigidbody == body ? contact.otherCollider : contact.collider;
            if (other != null && other == fieldCeiling) route.upperPlateContacts++;
            if (other == null || !other.CompareTag("Ground") || Mathf.Abs(contact.normal.y) < .6f || body.position.y < contact.point.y + .25f) continue;
            if (!route.landedSurfaces.Contains(other.name)) route.landedSurfaces.Add(other.name);
            if (route.stage == 3)
            {
                if (Mathf.Abs(contact.point.y + 2.2f) < .12f && contact.point.x >= -.05f && contact.point.x <= 3.65f) route.firstLanding = true;
                if (Mathf.Abs(contact.point.y + 1.5f) < .12f && contact.point.x >= 3.95f && contact.point.x <= 7.05f) route.secondLanding = true;
            }
            if (route.stage == 5)
            {
                if (Mathf.Abs(contact.point.y) < .12f && contact.point.x >= -1.05f && contact.point.x <= 2.05f) route.firstLanding = true;
                if (Mathf.Abs(contact.point.y - 1f) < .12f && contact.point.x >= 2.95f && contact.point.x <= 6.05f) route.secondLanding = true;
            }
        }
        ElectricForceSample sample = default;
        if (route.stage == 4)
        {
            ObserveField(body, out sample);
            Bounds bounds = selector.Ball.GetComponent<Collider2D>().bounds;
            if (bounds.min.x >= fieldCeiling.bounds.min.x && bounds.max.x <= fieldCeiling.bounds.max.x)
            {
                float penetration = Mathf.Max(0f, bounds.max.y - fieldCeiling.bounds.min.y, fieldFloor.bounds.max.y - bounds.min.y);
                route.maximumPlatePenetration = Mathf.Max(route.maximumPlatePenetration, penetration);
                route.plateClearanceValid &= penetration <= .06f;
                Require(route.plateClearanceValid, "Ball crossed a visible electric-field plate.");
            }
        }
        if (route.stage == 1)
        {
            route.tutorialBounce = tutorial.HasBounced; route.tutorialLeft = tutorial.HasMovedLeft;
            route.tutorialRight = tutorial.HasMovedRight; route.tutorialChargeCycle = tutorial.HasCycledCharge;
        }
        if (Time.fixedTime < nextTrace) return; nextTrace = Time.fixedTime + .12f;
        route.trace.Add(new Trace { time = route.seconds, input = input, phase = phase.ToString(), task = lesson.CurrentTaskText,
            position = body.position, velocity = body.linearVelocity, force = sample.Force, inField = sample.IsInRange,
            charge = selector.Ball.SignedCharge, voltage = lesson.CurrentVoltage }); Write();
    }
    void ObserveField(Rigidbody2D body, out ElectricForceSample sample)
    {
        bool inField = lesson.TryGetCurrentSample(out sample) && sample.IsInRange && sample.Field.y < -.01f;
        bool rise = inField && sample.SignedCharge < 0 && sample.Force.y > .01f && body.linearVelocity.y > .25f;
        if (rise) { if (float.IsNaN(ascentStart)) ascentStart = body.position.y; route.fieldAscentMeters = Mathf.Max(route.fieldAscentMeters, body.position.y - ascentStart); } else ascentStart = float.NaN;
        route.fieldAscent |= route.fieldAscentMeters >= 1f;
        bool fall = route.fieldAscent && star.IsCollected && inField && selector.Ball.CurrentPolarity == Polarity.Neutral && Mathf.Abs(sample.Force.y) < .001f && body.linearVelocity.y < -.25f;
        if (fall) { if (float.IsNaN(descentStart)) descentStart = body.position.y; route.neutralDescentMeters = Mathf.Max(route.neutralDescentMeters, descentStart - body.position.y); } else descentStart = float.NaN;
        route.neutralDescent |= route.neutralDescentMeters >= 1f;
        bool braking = route.neutralDescent && inField && sample.SignedCharge < 0 && sample.Force.y > .01f && body.linearVelocity.y < -.25f;
        brakeRun = braking ? brakeRun + Time.fixedDeltaTime : 0f;
        route.negativeBrakeSeconds = Mathf.Max(route.negativeBrakeSeconds, brakeRun); route.negativeBrake |= route.negativeBrakeSeconds >= .0999f;
    }
    void CompleteRoute()
    {
        try
        {
            Observe();
            bool passed = route.starEvents > 0 && route.goalEvents > 0 && route.educationSatisfied && route.retryCount == 0
                && route.physicsUnchanged && route.highestEdge <= route.cameraTop + .05f;
            if (route.stage == 1) passed &= route.tutorialBounce && route.tutorialLeft && route.tutorialRight && route.tutorialChargeCycle && route.spaceEdges >= 3;
            if (route.stage == 2) passed &= route.contactEvents > 0;
            if (route.stage == 3 || route.stage == 5) passed &= route.firstLanding && route.secondLanding;
            if (route.stage == 4) passed &= route.fieldAscent && route.neutralDescent && route.negativeBrake && lesson.HasRestoredNegativeField && route.plateClearanceValid;
            if (route.stage == 5) { route.launchVoltage = lesson.LastLaunchVoltage; passed &= Mathf.Abs(route.launchVoltage - 50f) <= .02f; }
            route.status = passed ? "passed" : "failed";
            route.details = "Actual saved terrain landings, star/goal callbacks and lesson completion; original physics; no retries; ball inside camera top.";
            Require(passed, "Natural exit reached but one or more route evidence checks failed.");
            Change(Phase.Result);
        }
        catch (Exception error) { Finish(error); }
    }
    System.Collections.IEnumerator ResultThenContinue()
    {
        yield return new WaitForSecondsRealtime(.25f);
        ElectricStageProgression progression = selector.GetComponent<ElectricStageProgression>();
        route.resultShown = progression != null && (progression.IsAwaitingContinue || progression.IsGameComplete);
        if (!route.resultShown) { Finish(new InvalidOperationException("Real goal did not open saved result screen.")); yield break; }
        yield return Capture("stage" + route.stage + "-result");
        if (report.recordRequested) yield return new WaitForSecondsRealtime(route.stage == 5 ? 2.75f : 2.25f);
        if (route.stage == report.lastStage)
        {
            if (route.stage == 5) report.finalComplete = progression.IsGameComplete;
            Finish(null); yield break;
        }
        expectedStage = route.stage + 1; route = null; input = 0f;
        progression.ContinueNextStage(); report.continues++; Write();
    }
    System.Collections.IEnumerator RecordPausePreview()
    {
        yield return new WaitForSecondsRealtime(.2f);
        PauseMenuController pause = Object.FindFirstObjectByType<PauseMenuController>();
        if (pause == null) { Finish(new InvalidOperationException("Saved pause menu is missing from the recorded stage.")); yield break; }
        pause.Pause();
        if (!pause.IsPaused || Time.timeScale != 0f) { Finish(new InvalidOperationException("Saved pause command did not pause gameplay.")); yield break; }
        yield return new WaitForSecondsRealtime(.25f);
        yield return Capture("stage3-pause");
        yield return new WaitForSecondsRealtime(1.75f);
        pause.Resume();
        report.recordedPauseVerified = !pause.IsPaused && Time.timeScale == 1f;
        if (!report.recordedPauseVerified) { Finish(new InvalidOperationException("Saved resume command did not restore gameplay.")); yield break; }
        Write();
    }
    System.Collections.IEnumerator Capture(string label)
    {
        if (Application.isBatchMode || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) yield break;
        yield return new WaitForEndOfFrame();
        Texture2D pixels = ScreenCapture.CaptureScreenshotAsTexture();
        if (pixels == null) yield break;
        string imagePath = Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path) + "-" + label + ".png");
        File.WriteAllBytes(imagePath, pixels.EncodeToPNG()); Object.Destroy(pixels); report.screenshots.Add(imagePath); Write();
    }
    System.Collections.IEnumerator RecordFrames()
    {
        float next = Time.realtimeSinceStartup;
        while (!finished)
        {
            yield return new WaitForEndOfFrame();
            if (finished || Time.realtimeSinceStartup < next) continue;
            float now = Time.realtimeSinceStartup;
            Texture2D pixels = ScreenCapture.CaptureScreenshotAsTexture();
            if (pixels == null) throw new InvalidOperationException("Actual-frame recording failed.");
            report.recordWidth = pixels.width; report.recordHeight = pixels.height;
            string file = Path.Combine(report.recordFolder, report.recordedFrames.ToString("D5") + ".png");
            File.WriteAllBytes(file, pixels.EncodeToPNG()); Object.Destroy(pixels);
            report.recordedFrames++; report.recordFrameTimes.Add(now - started);
            next = now + 1f / 12f;
        }
    }
    static T Field<T>(object value, string name)
    {
        if (value == null) throw new InvalidOperationException("Missing object for " + name);
        FieldInfo field = value.GetType().GetField(name, Fields); if (field == null) throw new MissingFieldException(value.GetType().Name, name);
        return (T)field.GetValue(value);
    }
    static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    void OnLog(string message, string stack, LogType type)
    { if (!finished && report != null && (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)) { report.errors.Add(message + "\n" + stack); Write(); } }
    void Write()
    { if (report == null) return; report.elapsedSeconds = Time.realtimeSinceStartup - started; Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, JsonUtility.ToJson(report, true)); }
    void Finish(Exception error)
    {
        if (finished) return; finished = true;
        if (error != null) { report.errors.Add(error.ToString()); if (route != null) { route.status = "failed"; route.details = error.Message; } }
        input = 0f;
        try { Pump(false, false); progress.Restore(); report.progressRestored = true; } catch (Exception restoreError) { report.errors.Add(restoreError.ToString()); }
        bool passed = report.errors.Count == 0 && report.initialTransitionAccepted && report.progressRestored
            && report.routes.Count == report.lastStage - report.firstStage + 1 && (report.lastStage != 5 || report.finalComplete);
        foreach (Route result in report.routes) passed &= result.status == "passed" && result.resultShown;
        report.status = passed ? "passed" : "failed"; report.finishedUtc = DateTime.UtcNow.ToString("O"); Write();
        Application.logMessageReceived -= OnLog;
        if (inputReady) { InputSystem.RemoveDevice(keyboard); if (previousKeyboard != null && previousKeyboard.added) previousKeyboard.MakeCurrent(); InputSystem.settings.updateMode = previousUpdateMode; InputSystem.settings.backgroundBehavior = previousBackground; inputReady = false; }
        Application.Quit(passed ? 0 : 1);
    }
}

public sealed class ScienceCampaignProgress
{
    [Serializable] sealed class Entry { public string key; public bool existed, isFloat; public int integer; public float number; }
    [Serializable] sealed class Snapshot { public bool restored, verified; public List<Entry> entries = new List<Entry>(); }
    string path; Snapshot snapshot;
    public static ScienceCampaignProgress Capture(string path)
    {
        var result = new ScienceCampaignProgress { path = path, snapshot = new Snapshot() };
        result.Add(ElectricStageProgression.UnlockKey, false);
        for (int stage = 1; stage <= 5; stage++)
        {
            string mastery = "ElectricBounce.Mastery.Stage" + stage;
            result.Add(mastery + ".Flags", false); result.Add(mastery + ".FewestRetries", false); result.Add(mastery + ".BestVoltage", true);
            result.Add("ElectricBounce.Plan.Stage" + stage + ".Flags", false);
        }
        result.Add("ElectricBounce.Plan.Stage3.FixedCharges.Flags", false); result.Save(); return result;
    }
    void Add(string key, bool isFloat) { snapshot.entries.Add(new Entry { key = key, isFloat = isFloat, existed = PlayerPrefs.HasKey(key), integer = isFloat ? 0 : PlayerPrefs.GetInt(key, 0), number = isFloat ? PlayerPrefs.GetFloat(key, 0f) : 0f }); }
    public void Restore()
    {
        if (snapshot.restored && snapshot.verified) return;
        foreach (Entry entry in snapshot.entries) { if (!entry.existed) PlayerPrefs.DeleteKey(entry.key); else if (entry.isFloat) PlayerPrefs.SetFloat(entry.key, entry.number); else PlayerPrefs.SetInt(entry.key, entry.integer); }
        PlayerPrefs.Save(); snapshot.restored = true; snapshot.verified = true;
        foreach (Entry entry in snapshot.entries) snapshot.verified &= PlayerPrefs.HasKey(entry.key) == entry.existed && (!entry.existed || (entry.isFloat ? PlayerPrefs.GetFloat(entry.key) == entry.number : PlayerPrefs.GetInt(entry.key) == entry.integer));
        Save(); if (!snapshot.verified) throw new InvalidOperationException("Original progress restoration failed.");
    }
    void Save() { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, JsonUtility.ToJson(snapshot, true)); }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Recover()
    {
        if (Application.isEditor || Array.IndexOf(Environment.GetCommandLineArgs(), "--qa-science-restore-progress") < 0) return;
        string path = null; foreach (string arg in Environment.GetCommandLineArgs()) if (arg.StartsWith("--qa-progress-backup=")) path = arg.Substring(21);
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)) throw new InvalidOperationException("Absolute progress backup required.");
        new ScienceCampaignProgress { path = path, snapshot = JsonUtility.FromJson<Snapshot>(File.ReadAllText(path)) }.Restore(); Application.Quit(0);
    }
}
#endif
