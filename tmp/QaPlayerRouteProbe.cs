// Temporary standalone-only route validation. Copy into the isolated build's Assets folder.
// Inert unless BOTH --qa-player-routes and --qa-report=<absolute json path> are supplied.
// Optional --qa-route-stage=2..5 runs only that stage for fault isolation.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using sung_wook;

[DefaultExecutionOrder(-1000)]
public sealed class QaPlayerRouteProbe : MonoBehaviour
{
    private const string StageFolder = "Assets/@Project/@sung_wook/@Game/00_Scenes/Stage/";
    private const float TargetVoltage = 50f;
    private const float RouteTimeout = 65f;

    [Serializable] private sealed class Trace
    {
        public float time, input, voltage, signedCharge;
        public string phase;
        public Vector2 position, velocity;
        public Vector2 electricForce;
        public int stars;
        public bool learning, inField;
    }
    [Serializable] private sealed class Terrain
    {
        public string name, type, compositeOperation;
        public int shapeCount;
        public Vector3 center, size;
    }
    [Serializable] private sealed class Route
    {
        public int stage;
        public string status = "running", details, phase;
        public float seconds, peakY, highestBallEdge, cameraTop, launchVoltage;
        public float mass, gravityScale, jumpForce, maxSpeed, acceleration, deceleration;
        public int retryCount;
        public Vector2 start, finish;
        public bool actualChargeEvent, realStarEvent, realGoalEvent, learningSatisfied, stayedWithinTopBoundary;
        public bool realFirstLanding, realSecondLanding, physicsSettingsUnchanged = true;
        public bool fieldAscentObserved, neutralFieldDescentObserved, negativeFieldBrakeObserved;
        public float fieldAscentMeters, neutralFieldDescentMeters, negativeFieldBrakeSeconds;
        public int missingSolidTerrainShapes;
        public List<Terrain> terrain = new List<Terrain>();
        public List<string> contacts = new List<string>();
        public List<Trace> trace = new List<Trace>();
    }
    [Serializable] private sealed class Report
    {
        public string status = "running", startedUtc, finishedUtc, unity, platform, graphicsDevice, graphicsApi;
        public int processId, firstStage, lastStage;
        public bool developmentBuild, firstTransitionAccepted, finalGameComplete;
        public string method = "Windows standalone: real saved scenes and default player physics. Digital A/D-equivalent (-1,0,+1) PlayerMovement.SetMovex commands plus public lesson/lever commands, supplied before each physics step. Actual contact/star/goal callbacks and normal asynchronous campaign transitions. This is scripted API-input route validation, not a human mouse/keyboard playthrough.";
        public string integrity = "Only the normal RestartStage API initializes each scene once. No route teleports, direct position/velocity/force/gravity/jump/mass changes, fake callbacks, or invisible collision fixtures. No PlayerPrefs mutations by this probe; no assets or colors changed.";
        public float targetStage5Voltage = TargetVoltage, elapsedSeconds;
        public List<Route> routes = new List<Route>();
        public List<string> errors = new List<string>();
    }
    private enum Phase { Contact, Baseline, ChangeDistance, DistanceLaunch, FieldLift, Charge, VoltageLaunch, Route, Star, Exit }

    private Report report;
    private Route current;
    private string reportPath;
    private ElectricStageSelector selector;
    private ElectricLearningController2D lesson;
    private PlayerMovement movement;
    private ElectricStar2D star;
    private ElectricGoal2D goal;
    private BoxCollider2D voltageZone;
    private Collider2D middleFallingStone;
    private readonly List<Collider2D> shelves = new List<Collider2D>();
    private readonly ContactPoint2D[] contactBuffer = new ContactPoint2D[32];
    private Phase phase;
    private int waypoint, expectedStage;
    private float startedRealtime, stageStarted, phaseStarted, nextTrace, input, normalBounceRise, voltagePerPhysicsStep;
    private bool transitionRequested, finished, middleStonePassed, waypointLanded, awaitingCompletion;
    private bool stage4NeutralSelected, stage4NegativeRestored;
    private float fieldAscentStartY = float.NaN, neutralDescentStartY = float.NaN, negativeBrakeRun;
    private ElectricForceSample fieldSample;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Boot()
    {
        if (Application.isEditor) return;
        bool optedIn = false;
        string path = null;
        int onlyStage = 0;
        foreach (string arg in Environment.GetCommandLineArgs())
        {
            if (arg == "--qa-player-routes") optedIn = true;
            if (arg.StartsWith("--qa-report=", StringComparison.Ordinal)) path = arg.Substring("--qa-report=".Length);
            if (arg.StartsWith("--qa-route-stage=", StringComparison.Ordinal)) int.TryParse(arg.Substring("--qa-route-stage=".Length), out onlyStage);
        }
        if (!optedIn || string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)) return;
        var root = new GameObject("Temporary Standalone Player Routes QA");
        DontDestroyOnLoad(root);
        root.AddComponent<QaPlayerRouteProbe>().Initialize(path, onlyStage);
    }

    private void Initialize(string path, int onlyStage)
    {
        reportPath = path;
        startedRealtime = Time.realtimeSinceStartup;
        report = new Report {
            startedUtc = DateTime.UtcNow.ToString("O"), unity = Application.unityVersion,
            platform = Application.platform.ToString(), graphicsDevice = SystemInfo.graphicsDeviceName,
            graphicsApi = SystemInfo.graphicsDeviceType.ToString(), developmentBuild = Debug.isDebugBuild,
            processId = System.Diagnostics.Process.GetCurrentProcess().Id,
            firstStage = onlyStage >= 2 && onlyStage <= 5 ? onlyStage : 2,
            lastStage = onlyStage >= 2 && onlyStage <= 5 ? onlyStage : 5
        };
        expectedStage = report.firstStage;
        Application.runInBackground = true;
        Application.targetFrameRate = 60;
        QualitySettings.vSyncCount = 0;
        Application.logMessageReceived += OnLog;
        Write();
    }

    private void Update()
    {
        if (finished || report == null) return;
        try
        {
            if (Time.realtimeSinceStartup - startedRealtime > 300f) throw new TimeoutException("Standalone route watchdog exceeded 300 real seconds.");
            if (!transitionRequested)
            {
                if (!SceneTransitionController.IsAvailable || SceneTransitionController.IsBusy) return;
                report.firstTransitionAccepted = SceneTransitionController.LoadScene(ScenePath(expectedStage));
                if (!report.firstTransitionAccepted) throw new InvalidOperationException("Shared initial asynchronous scene transition was rejected.");
                transitionRequested = true;
                Write();
                return;
            }
            if (awaitingCompletion)
            {
                ElectricStageProgression progression = selector != null ? selector.GetComponent<ElectricStageProgression>() : null;
                if (progression != null && progression.IsGameComplete)
                {
                    report.finalGameComplete = true;
                    Finish();
                }
                return;
            }
            if (current != null || SceneTransitionController.IsBusy || SceneManager.GetActiveScene().path != ScenePath(expectedStage)) return;
            selector = UnityEngine.Object.FindFirstObjectByType<ElectricStageSelector>();
            if (selector == null || selector.SelectedStage != expectedStage || selector.GetComponent<ElectricStageSession2D>()?.IsInitialized != true) return;
            Begin(expectedStage);
        }
        catch (Exception error) { Fail(error); }
    }

    private void FixedUpdate()
    {
        if (finished || current == null || report == null || SceneTransitionController.IsBusy) return;
        try
        {
            Rigidbody2D body = selector.Ball.Body;
            Observe();
            bool unchanged = Mathf.Approximately(body.mass, current.mass) && Mathf.Approximately(body.gravityScale, current.gravityScale)
                && Mathf.Approximately(ReadFloat(movement, "jumpForce"), current.jumpForce)
                && Mathf.Approximately(ReadFloat(movement, "acceleration"), current.acceleration)
                && Mathf.Approximately(ReadFloat(movement, "deceleration"), current.deceleration)
                && Mathf.Approximately(SavedMaxSpeed(movement), current.maxSpeed);
            current.physicsSettingsUnchanged &= unchanged;
            if (!unchanged) throw new InvalidOperationException("Saved player mass, gravity, jump, speed, or acceleration changed during route.");
            if (goal.IsCleared) return; // The natural callback synchronously records the route before progression loads the next scene.
            if (body.position.y < -9f || Mathf.Abs(body.position.x) > 25f || Time.fixedTime - stageStarted > RouteTimeout)
                throw new InvalidOperationException("Route fell outside map or exceeded 65 simulation seconds.");
            Drive();
            RecordTrace();
        }
        catch (Exception error) { Fail(error); }
    }

    private void Begin(int stage)
    {
        lesson = selector.GetComponent<ElectricLearningController2D>();
        movement = selector.Ball.GetComponent<PlayerMovement>();
        selector.Ball.GetComponent<PlayerController>().enabled = false;
        selector.Ball.GetComponent<PlayerInputReader>().enabled = false;
        selector.RestartStage(); // Normal saved-spawn initialization; no route-position edits.
        if (stage == 4) lesson.SetBallCharge(-1f);
        selector.Lever.SetValue(stage == 2 || stage == 5 ? 1f : 0f);
        star = selector.GetStageComponent<ElectricStar2D>(stage);
        goal = selector.GetStageComponent<ElectricGoal2D>(stage);
        if (lesson == null || movement == null || star == null || goal == null || Camera.main == null)
            throw new InvalidOperationException("Required saved player, lesson, star, goal, or camera is missing.");
        Rigidbody2D body = selector.Ball.Body;
        current = new Route {
            stage = stage, start = body.position, finish = body.position, peakY = body.position.y,
            cameraTop = Camera.main.transform.position.y + Camera.main.orthographicSize,
            mass = body.mass, gravityScale = body.gravityScale, jumpForce = ReadFloat(movement, "jumpForce"),
            acceleration = ReadFloat(movement, "acceleration"), deceleration = ReadFloat(movement, "deceleration"),
            maxSpeed = SavedMaxSpeed(movement)
        };
        report.routes.Add(current);
        Route observed = current;
        star.OnCollected.AddListener(() => { observed.realStarEvent = true; Write(); });
        goal.OnCleared.AddListener(() => {
            observed.realGoalEvent = true;
            if (current == observed) CompleteCurrent();
        });
        if (stage == 2)
        {
            ContactCharger2D charger = selector.GetStageComponent<ContactCharger2D>(2);
            if (charger == null) throw new InvalidOperationException("Stage 2 saved contact charger is missing.");
            charger.ChargedFromNeutral += _ => { observed.actualChargeEvent = true; Write(); };
        }
        float bounceSpeed = current.jumpForce / current.mass;
        normalBounceRise = bounceSpeed * bounceSpeed / (2f * Mathf.Abs(Physics2D.gravity.y) * current.gravityScale);
        shelves.Clear();
        middleFallingStone = null;
        middleStonePassed = false;
        foreach (GameObject root in selector.gameObject.scene.GetRootGameObjects())
            foreach (Collider2D collider in root.GetComponentsInChildren<Collider2D>())
            {
                if (!collider.enabled || collider.isTrigger || collider.attachedRigidbody == body) continue;
                if (collider.name == "Obstacle Blue Falling Stone 02") middleFallingStone = collider;
                var terrain = new Terrain { name = collider.name, type = collider.GetType().Name,
                    compositeOperation = collider.compositeOperation.ToString(), shapeCount = collider.shapeCount,
                    center = collider.bounds.center, size = collider.bounds.size };
                current.terrain.Add(terrain);
                if (terrain.type == "TilemapCollider2D" && terrain.compositeOperation == "None" && terrain.shapeCount == 0)
                    current.missingSolidTerrainShapes++;
                if (collider.name.StartsWith("Route ", StringComparison.Ordinal) && !collider.name.Contains("Marker")) shelves.Add(collider);
            }
        shelves.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        if (stage == 3 || stage == 4)
        {
            if (shelves.Count == 0) throw new InvalidOperationException("Saved route shelves are missing.");
            if (current.missingSolidTerrainShapes != 0) throw new InvalidOperationException("Active standalone solid terrain has no collision shapes.");
        }
        if (stage == 5)
        {
            VoltageLauncher2D launcher = selector.GetStageComponent<VoltageLauncher2D>(5);
            voltageZone = ReadField<BoxCollider2D>(launcher, "launchZone");
            voltagePerPhysicsStep = ReadFloat(launcher, "chargeRate") * Time.fixedDeltaTime;
            if (voltageZone == null || voltagePerPhysicsStep <= 0f) throw new InvalidOperationException("Saved Stage 5 launch zone/rate is invalid.");
        }
        waypoint = 0;
        waypointLanded = false;
        stage4NeutralSelected = stage4NegativeRestored = false;
        fieldAscentStartY = neutralDescentStartY = float.NaN;
        negativeBrakeRun = 0f;
        fieldSample = default;
        stageStarted = phaseStarted = nextTrace = Time.fixedTime;
        phase = stage == 2 ? Phase.Contact : stage == 3 ? Phase.Baseline : stage == 4 ? Phase.FieldLift : Phase.Charge;
        SetInput(0f);
        Write();
    }

    private void Drive()
    {
        Rigidbody2D body = selector.Ball.Body;
        if (current.stage == 4)
        {
            // Keep the authored ascent and upper-star route. Only after its real collection,
            // remove upward electrical lift to descend through the now-visible return field.
            if (phase == Phase.Route && waypoint >= 2 && star.IsCollected && !stage4NeutralSelected)
            {
                lesson.SetBallCharge(0f);
                stage4NeutralSelected = true;
            }
            // Reapply negative charge in the lower return approach. Its real upward force
            // brakes the fall before the exit; no position, velocity, or force writes occur.
            if (phase == Phase.Exit && stage4NeutralSelected && !stage4NegativeRestored
                && current.neutralFieldDescentMeters >= 1f && body.position.x <= 2.2f
                && body.position.y <= -1.5f && body.linearVelocity.y < -0.25f)
            {
                lesson.SetBallCharge(-1f);
                stage4NegativeRestored = true;
            }
        }
        switch (phase)
        {
            case Phase.Contact:
                if (!lesson.ContactChallengeSatisfied)
                {
                    selector.Lever.SetValue(1f);
                    Steer(current.start.x);
                }
                else
                {
                    selector.Lever.SetValue(0f);
                    Change(Phase.Star);
                }
                break;
            case Phase.Baseline:
                if (Time.fixedTime - phaseStarted < 0.12f) return;
                lesson.RecordBaseline();
                if (!lesson.HasBaseline) throw new InvalidOperationException("Stage 3 baseline was rejected at saved spawn.");
                selector.Lever.SetValue(0.95f);
                Change(Phase.ChangeDistance);
                break;
            case Phase.ChangeDistance:
                if (Time.fixedTime - phaseStarted < 0.12f) return;
                lesson.RequestLaunch();
                Change(Phase.DistanceLaunch);
                break;
            case Phase.DistanceLaunch:
                if (lesson.ComparisonChallengeSatisfied) Change(Phase.Route);
                break;
            case Phase.FieldLift:
                Steer(current.start.x);
                if (lesson.NegativeChallengeSatisfied && body.position.y >= 0.8f) Change(Phase.Route);
                break;
            case Phase.Charge:
                Steer(current.start.x);
                VoltageLauncher2D launcher = selector.GetStageComponent<VoltageLauncher2D>(5);
                float remainingVoltage = TargetVoltage - launcher.Voltage;
                if (remainingVoltage > 0f)
                {
                    selector.Lever.SetValue(Mathf.Clamp(remainingVoltage / voltagePerPhysicsStep, 0.0101f, 1f));
                    break;
                }
                selector.Lever.SetValue(0f);
                if (!voltageZone.OverlapPoint(body.worldCenterOfMass)) break;
                lesson.RequestLaunch();
                Change(Phase.VoltageLaunch);
                break;
            case Phase.VoltageLaunch:
                Steer(current.start.x);
                if (lesson.VoltageChallengeSatisfied)
                {
                    current.launchVoltage = lesson.LastLaunchVoltage;
                    if (Mathf.Abs(current.launchVoltage - TargetVoltage) > 0.01f)
                        throw new InvalidOperationException("Stage 5 actual launch differed from 50 V by more than 0.01 V.");
                    Change(Phase.Route);
                }
                break;
            case Phase.Route:
                if (current.stage == 5)
                {
                    if (waypoint == 0 && !current.realFirstLanding)
                    {
                        Steer(body.position.y < 2.6f && body.position.x < -2.5f ? -2.6f : -0.5f);
                        break;
                    }
                    waypoint = 1;
                    Steer(4.8f);
                    if (current.realSecondLanding && Mathf.Abs(body.position.x - 4.8f) < 0.55f) Change(Phase.Star);
                    break;
                }
                Collider2D shelf = shelves[waypoint];
                if (current.stage == 3 && waypoint == 2 && !middleStonePassed)
                {
                    Steer(4.6f);
                    float ballRadius = selector.Ball.GetComponent<Collider2D>().bounds.extents.y;
                    float safeStoneBottom = shelves[1].bounds.max.y + ballRadius * 2f + normalBounceRise + 0.1f;
                    bool stoneSafe = middleFallingStone != null && middleFallingStone.bounds.min.y > safeStoneBottom
                        && (middleFallingStone.attachedRigidbody == null || middleFallingStone.attachedRigidbody.linearVelocity.y >= -0.01f);
                    bool earlyBounce = body.linearVelocity.y > 4.5f && body.position.y < shelves[1].bounds.max.y + ballRadius + 1.2f;
                    if (!stoneSafe || !earlyBounce) break;
                    middleStonePassed = true;
                }
                Steer(shelf.bounds.center.x);
                bool near = Mathf.Abs(body.position.x - shelf.bounds.center.x) < shelf.bounds.extents.x * 0.65f;
                bool onTop = body.position.y >= shelf.bounds.max.y && body.position.y < shelf.bounds.max.y + 1f;
                bool starShelf = Mathf.Abs(shelf.bounds.center.x - star.transform.position.x) < 0.5f;
                if (waypointLanded && near && onTop && (!starShelf || star.IsCollected))
                {
                    waypoint++;
                    waypointLanded = false;
                    if (waypoint >= shelves.Count) Change(Phase.Star);
                }
                break;
            case Phase.Star:
                Steer(star.transform.position.x);
                if (star.IsCollected) Change(Phase.Exit);
                break;
            case Phase.Exit:
                Steer(goal.transform.position.x);
                break;
        }
    }

    private void Steer(float targetX)
    {
        Rigidbody2D body = selector.Ball.Body;
        float error = targetX - body.position.x, velocity = body.linearVelocity.x;
        float speedLimit = Mathf.Min(current.maxSpeed, Mathf.Sqrt(2f * current.acceleration * Mathf.Abs(error)));
        float desired = Mathf.Clamp(error * 2.5f - velocity * 0.45f, -speedLimit, speedLimit);
        float velocityError = desired - velocity;
        float value = Mathf.Abs(velocityError) > 0.15f ? Mathf.Sign(velocityError) : 0f;
        if (Mathf.Abs(error) < 0.08f && Mathf.Abs(velocity) < 0.15f) value = 0f;
        SetInput(value);
    }
    private void SetInput(float value)
    {
        input = Mathf.Abs(value) < 0.05f ? 0f : Mathf.Sign(value);
        if (movement != null) movement.SetMovex(input);
    }
    private void Change(Phase next) { phase = next; phaseStarted = Time.fixedTime; }

    private void Observe()
    {
        Rigidbody2D body = selector.Ball.Body;
        current.peakY = Mathf.Max(current.peakY, body.position.y);
        current.highestBallEdge = Mathf.Max(current.highestBallEdge, selector.Ball.GetComponent<Collider2D>().bounds.max.y);
        current.seconds = Time.fixedTime - stageStarted;
        current.finish = body.position;
        current.learningSatisfied = lesson.CurrentLearningGoalSatisfied;
        current.retryCount = selector.GetComponent<ElectricStageSession2D>().RetryCount;
        if (current.stage == 4) ObserveFieldTraversal(body);
        int count = body.GetContacts(contactBuffer);
        for (int i = 0; i < count; i++)
        {
            ContactPoint2D contact = contactBuffer[i];
            Collider2D other = contact.collider.attachedRigidbody == body ? contact.otherCollider : contact.collider;
            if (other != null && !current.contacts.Contains(other.name)) current.contacts.Add(other.name);
            bool top = Mathf.Abs(contact.normal.y) > 0.6f && body.position.y > contact.point.y + 0.25f;
            if (phase == Phase.Route && waypoint < shelves.Count && other == shelves[waypoint] && top) waypointLanded = true;
            if (current.stage != 5 || phase != Phase.Route || other == null || !other.CompareTag("Ground") || !top) continue;
            if (Mathf.Abs(contact.point.y - 2f) < 0.1f && contact.point.x >= -2.05f && contact.point.x <= 2.05f) current.realFirstLanding = true;
            if (Mathf.Abs(contact.point.y - 3f) < 0.1f && contact.point.x >= 2.95f && contact.point.x <= 7.05f) current.realSecondLanding = true;
        }
    }
    private void ObserveFieldTraversal(Rigidbody2D body)
    {
        bool sampled = lesson.TryGetCurrentSample(out fieldSample);
        bool downwardField = sampled && fieldSample.IsInRange && fieldSample.Field.y < -0.01f;
        bool ascending = downwardField && fieldSample.SignedCharge < 0f && fieldSample.Force.y > 0.01f
            && body.linearVelocity.y > 0.25f;
        if (ascending)
        {
            if (float.IsNaN(fieldAscentStartY)) fieldAscentStartY = body.position.y;
            current.fieldAscentMeters = Mathf.Max(current.fieldAscentMeters, body.position.y - fieldAscentStartY);
            current.fieldAscentObserved |= current.fieldAscentMeters >= 1f;
        }
        else fieldAscentStartY = float.NaN;

        bool neutralDescent = current.fieldAscentObserved && star.IsCollected && downwardField
            && selector.Ball.CurrentPolarity == Polarity.Neutral && Mathf.Abs(fieldSample.Force.y) < 0.001f
            && body.linearVelocity.y < -0.25f;
        if (neutralDescent)
        {
            if (float.IsNaN(neutralDescentStartY)) neutralDescentStartY = body.position.y;
            current.neutralFieldDescentMeters = Mathf.Max(current.neutralFieldDescentMeters, neutralDescentStartY - body.position.y);
            current.neutralFieldDescentObserved |= current.neutralFieldDescentMeters >= 1f;
        }
        else neutralDescentStartY = float.NaN;

        bool negativeBrake = current.neutralFieldDescentObserved && downwardField
            && fieldSample.SignedCharge < 0f && fieldSample.Force.y > 0.01f && body.linearVelocity.y < -0.25f;
        negativeBrakeRun = negativeBrake ? negativeBrakeRun + Time.fixedDeltaTime : 0f;
        current.negativeFieldBrakeSeconds = Mathf.Max(current.negativeFieldBrakeSeconds, negativeBrakeRun);
        current.negativeFieldBrakeObserved |= current.negativeFieldBrakeSeconds >= 0.1f - 0.0001f;
    }
    private void RecordTrace()
    {
        if (Time.fixedTime < nextTrace) return;
        nextTrace = Time.fixedTime + 0.15f;
        current.phase = phase + (phase == Phase.Route ? " " + waypoint : "");
        if (current.stage == 3 && phase == Phase.Route && waypoint == 2 && !middleStonePassed) current.phase += " Wait for Stone02";
        current.trace.Add(new Trace { time = current.seconds, phase = current.phase, input = input,
            position = current.finish, velocity = selector.Ball.Body.linearVelocity,
            voltage = lesson.CurrentVoltage, signedCharge = selector.Ball.SignedCharge,
            stars = selector.Ball.Stars, learning = current.learningSatisfied,
            electricForce = current.stage == 4 ? fieldSample.Force : Vector2.zero,
            inField = current.stage == 4 && fieldSample.IsInRange });
        Write();
    }

    private void CompleteCurrent()
    {
        Observe();
        current.launchVoltage = lesson.LastLaunchVoltage;
        current.stayedWithinTopBoundary = current.highestBallEdge <= current.cameraTop + 0.01f;
        bool success = current.realStarEvent && current.realGoalEvent && current.learningSatisfied
            && current.stayedWithinTopBoundary && current.physicsSettingsUnchanged && current.missingSolidTerrainShapes == 0
            && current.retryCount == 0;
        if (current.stage == 2) success &= current.actualChargeEvent;
        if (current.stage == 4) success &= current.fieldAscentObserved && current.neutralFieldDescentObserved
            && current.negativeFieldBrakeObserved && lesson.HasFieldAscent && lesson.HasNeutralFieldDescent
            && lesson.HasRestoredNegativeField;
        if (current.stage == 5) success &= current.realFirstLanding && current.realSecondLanding
            && Mathf.Abs(current.launchVoltage - TargetVoltage) <= 0.01f;
        current.status = success ? "passed" : "failed";
        current.details = "Natural collection and exit callbacks observed; unchanged saved physics and authored collision surfaces."
            + (current.stage == 4 ? " Real negative-charge field ascent >=1 m, post-star neutral field descent >=1 m, and restored upward electric force >=0.1 s observed on the authored return route." : "")
            + (current.stage == 5 ? " Actual launch must be 50 V +/-0.01 V and both saved platforms must receive real top contacts." : "");
        SetInput(0f);
        int completedStage = current.stage;
        current = null;
        Write();
        if (!success) { Finish(); return; }
        if (completedStage == report.lastStage)
        {
            if (completedStage == 5) awaitingCompletion = true;
            else Finish();
        }
        else expectedStage = completedStage + 1; // Progression, not this probe, owns the next shared asynchronous load.
    }

    private static T ReadField<T>(object target, string name)
    {
        if (target == null) throw new InvalidOperationException("Cannot inspect a missing saved component: " + name);
        FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (field == null) throw new MissingFieldException(target.GetType().Name, name);
        return (T)field.GetValue(target);
    }
    private static float ReadFloat(object target, string name) => ReadField<float>(target, name);
    private static float SavedMaxSpeed(PlayerMovement value)
    {
        float limit = ReadFloat(value, "maxMoveSpeed");
        return limit > 0f ? limit : ReadFloat(value, "moveSpeed");
    }
    private static string ScenePath(int stage) => StageFolder + "Stage_" + stage.ToString("00") + ".unity";
    private void OnLog(string message, string stack, LogType type)
    {
        if (finished || report == null || (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)) return;
        report.errors.Add(SceneManager.GetActiveScene().name + ": " + message + "\n" + stack);
        Write();
    }
    private void Fail(Exception error)
    {
        report.errors.Add(error.ToString());
        if (current != null) { current.status = "failed"; current.details = error.Message; }
        Finish();
    }
    private void Write()
    {
        report.elapsedSeconds = Time.realtimeSinceStartup - startedRealtime;
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
        File.WriteAllText(reportPath, JsonUtility.ToJson(report, true));
    }
    private void Finish()
    {
        if (finished) return;
        finished = true;
        bool passed = report.errors.Count == 0 && report.firstTransitionAccepted
            && report.routes.Count == report.lastStage - report.firstStage + 1
            && (report.lastStage != 5 || report.finalGameComplete);
        foreach (Route route in report.routes) passed &= route.status == "passed";
        report.status = passed ? "passed" : "failed";
        report.finishedUtc = DateTime.UtcNow.ToString("O");
        SetInput(0f);
        Write();
        Application.logMessageReceived -= OnLog;
        Application.Quit(passed ? 0 : 1);
    }
}
