using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace sung_wook
{
    /// <summary>
    /// 저장된 분리 씬을 실제 입력 명령과 물리 이동만으로 완주하는 배치 검사.
    /// 초기 전하 선택 뒤 위치·속도·힘·중력·충돌체를 직접 바꾸지 않고 실제 수집/출구 이벤트를 관찰한다.
    /// </summary>
    [InitializeOnLoad]
    public static class Stages34KeyboardRouteValidation
    {
        private const string Prefix = "sung_wook.Stages34KeyboardRoute.";
        private const string Folder = "Assets/@Project/@sung_wook/@Game/00_Scenes/Stage/";
        private const float RouteTimeout = 50f;

        [Serializable] private sealed class Trace
        {
            public float time;
            public string phase;
            public Vector2 position, velocity;
            public float input, voltage;
            public int stars;
            public bool learning;
        }

        [Serializable] private sealed class Route
        {
            public int stage;
            public string status = "running", details, phase;
            public float seconds, peakY, highestBallEdge, cameraTop, launchVoltage;
            public Vector2 start, finish;
            public bool realStarEvent, realGoalEvent, learningSatisfied, stayedWithinTopBoundary;
            public bool realFirstLanding, realSecondLanding;
            public List<string> terrain = new List<string>();
            public List<string> contacts = new List<string>();
            public List<Trace> trace = new List<Trace>();
        }

        [Serializable] private sealed class Report
        {
            public string status = "running";
            public string movement = "Digital A/D commands only (-1, 0, +1) through PlayerMovement.SetMovex plus public lesson/lever commands; unchanged saved physics and terrain; no route teleports or fake trigger callbacks.";
            public float targetVoltage;
            public List<Route> routes = new List<Route>();
            public List<string> errors = new List<string>();
        }

        private enum Phase { Baseline, ChangeDistance, DistanceLaunch, FieldLift, Charge, VoltageLaunch, Route, Star, Exit }
        private static Report report;
        private static Route current;
        private static ElectricStageSelector selector;
        private static ElectricLearningController2D lesson;
        private static PlayerMovement movement;
        private static ElectricStar2D star;
        private static ElectricGoal2D goal;
        private static BoxCollider2D voltageZone;
        private static Collider2D middleFallingStone;
        private static float normalBounceRise;
        private static bool middleStonePassed;
        private static readonly List<Collider2D> shelves = new List<Collider2D>();
        private static readonly ContactPoint2D[] contacts = new ContactPoint2D[32];
        private static Phase phase;
        private static int waypoint;
        private static float started, phaseStarted, previousPhysics = -1f, nextTrace, input, acceleration, maxSpeed;
        private static float initialGravity, initialMass, targetVoltage, voltagePerPhysicsStep;
        private static bool finished, waypointLanded;

        static Stages34KeyboardRouteValidation()
        {
            if (Application.isBatchMode && SessionState.GetBool(Prefix + "Active", false)) Resume();
        }

        public static void RunBatch() => StartBatch(3, 100f);
        
        

        private static void StartBatch(int firstStage, float voltage)
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Launch this route check in a separate batch editor.");
            report = new Report { targetVoltage = voltage };
            SessionState.SetBool(Prefix + "Active", true);
            SessionState.SetInt(Prefix + "Stage", firstStage);
            SessionState.SetInt(Prefix + "FirstStage", firstStage);
            SessionState.SetFloat(Prefix + "Voltage", voltage);
            SessionState.SetString(Prefix + "Started", DateTime.UtcNow.ToString("O"));
            WriteReport();
            EditorSceneManager.OpenScene(ScenePath(firstStage), OpenSceneMode.Single);
            Resume();
            ElectricBatchPlayMode.EnterAfterEditorStartup();
        }

        private static string ScenePath(int stage) => Folder + "Stage_" + stage.ToString("00") + ".unity";
        private static string ReportPath => Path.Combine(Path.GetDirectoryName(Application.dataPath),
            "Logs/Stages34KeyboardRouteReport.json");

        private static void Resume()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
        }

        private static void OnLog(string message, string stack, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            EnsureReport();
            report.errors.Add(message + "\n" + stack);
            WriteReport();
        }

        private static void EnsureReport()
        {
            if (report == null) report = JsonUtility.FromJson<Report>(File.ReadAllText(ReportPath));
        }

        private static void Tick()
        {
            if (!Application.isBatchMode || !SessionState.GetBool(Prefix + "Active", false) || finished) return;
            try
            {
                EnsureReport();
                if ((DateTime.UtcNow - DateTime.Parse(SessionState.GetString(Prefix + "Started", ""))).TotalSeconds > 300)
                    throw new TimeoutException("Split-scene route watchdog exceeded 300 seconds.");
                if (!EditorApplication.isPlaying || EditorApplication.isPaused) return;
                int stage = SessionState.GetInt(Prefix + "Stage", 3);
                if (SceneManager.GetActiveScene().path != ScenePath(stage)) return;
                if (current == null)
                {
                    selector = UnityEngine.Object.FindFirstObjectByType<ElectricStageSelector>();
                    if (selector == null || selector.GetComponent<ElectricStageSession2D>()?.IsInitialized != true) return;
                    Begin(stage);
                }
                if (Mathf.Approximately(previousPhysics, Time.fixedTime)) return;
                previousPhysics = Time.fixedTime;
                Rigidbody2D body = selector.Ball.Body;
                current.peakY = Mathf.Max(current.peakY, body.position.y);
                current.highestBallEdge = Mathf.Max(current.highestBallEdge, selector.Ball.GetComponent<Collider2D>().bounds.max.y);
                ObserveContacts();
                if (!Mathf.Approximately(body.mass, initialMass) || !Mathf.Approximately(body.gravityScale, initialGravity))
                    throw new InvalidOperationException("Player mass/gravity changed during route.");
                if (goal.IsCleared)
                {
                    Complete(current.realStarEvent && current.realGoalEvent && lesson.CurrentLearningGoalSatisfied,
                        "Actual star and exit callbacks completed using horizontal input and lesson commands.");
                    return;
                }
                if (body.position.y < -9f || Mathf.Abs(body.position.x) > 25f || Time.fixedTime - started > RouteTimeout)
                {
                    Complete(false, "Route fell outside map or exceeded 50 simulation seconds.");
                    return;
                }
                Drive();
                RecordTrace();
            }
            catch (Exception exception)
            {
                EnsureReport();
                report.errors.Add(exception.ToString());
                if (current != null) current.details = exception.Message;
                Finish();
            }
        }

        private static void Begin(int stage)
        {
            lesson = selector.GetComponent<ElectricLearningController2D>();
            movement = selector.Ball.GetComponent<PlayerMovement>();
            selector.Ball.GetComponent<PlayerController>().enabled = false;
            selector.Ball.GetComponent<PlayerInputReader>().enabled = false;
            // Initialization uses the normal scene restart and charge-selection commands exactly once.
            selector.RestartStage();
            if (stage == 4) lesson.SetBallCharge(-1f);
            selector.Lever.SetValue(stage == 5 ? 1f : 0f);
            targetVoltage = SessionState.GetFloat(Prefix + "Voltage", 100f);
            star = selector.GetStageComponent<ElectricStar2D>(stage);
            goal = selector.GetStageComponent<ElectricGoal2D>(stage);
            if (star == null || goal == null) throw new InvalidOperationException("Saved star or exit missing.");
            current = new Route { stage = stage, start = selector.Ball.Body.position, peakY = selector.Ball.Body.position.y,
                cameraTop = Camera.main.transform.position.y + Camera.main.orthographicSize };
            report.routes.Add(current);
            Route observed = current;
            star.OnCollected.AddListener(() => observed.realStarEvent = true);
            goal.OnCleared.AddListener(() => observed.realGoalEvent = true);
            var settings = new SerializedObject(movement);
            acceleration = settings.FindProperty("acceleration").floatValue;
            maxSpeed = settings.FindProperty("maxMoveSpeed").floatValue;
            if (maxSpeed <= 0f) maxSpeed = settings.FindProperty("moveSpeed").floatValue;
            initialMass = selector.Ball.Body.mass;
            initialGravity = selector.Ball.Body.gravityScale;
            float bounceSpeed = settings.FindProperty("jumpForce").floatValue / initialMass;
            normalBounceRise = bounceSpeed * bounceSpeed / (2f * Mathf.Abs(Physics2D.gravity.y) * initialGravity);
            middleFallingStone = null;
            middleStonePassed = false;
            shelves.Clear();
            foreach (GameObject root in selector.gameObject.scene.GetRootGameObjects())
                foreach (Collider2D collider in root.GetComponentsInChildren<Collider2D>())
                {
                    if (!collider.enabled || collider.isTrigger || collider.attachedRigidbody == selector.Ball.Body) continue;
                    if (collider.name == "Obstacle Blue Falling Stone 02") middleFallingStone = collider;
                    current.terrain.Add(collider.name + " center=" + collider.bounds.center.ToString("F2") + " size=" + collider.bounds.size.ToString("F2"));
                    if (collider.name.StartsWith("Route ", StringComparison.Ordinal) && !collider.name.Contains("Marker")) shelves.Add(collider);
                }
            shelves.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            if (stage != 5 && shelves.Count == 0) throw new InvalidOperationException("Saved route shelves missing.");
            if (stage == 5)
            {
                var launcher = selector.GetStageComponent<VoltageLauncher2D>(5);
                var launcherSettings = new SerializedObject(launcher);
                voltageZone = launcherSettings.FindProperty("launchZone").objectReferenceValue as BoxCollider2D;
                voltagePerPhysicsStep = launcherSettings.FindProperty("chargeRate").floatValue * Time.fixedDeltaTime;
                if (voltagePerPhysicsStep <= 0f) throw new InvalidOperationException("Saved voltage charge rate must be positive.");
            }
            waypoint = 0;
            waypointLanded = false;
            started = Time.fixedTime;
            phaseStarted = started;
            nextTrace = started;
            previousPhysics = -1f;
            phase = stage == 3 ? Phase.Baseline : stage == 4 ? Phase.FieldLift : Phase.Charge;
            SetInput(0f);
            WriteReport();
        }

        private static void Drive()
        {
            Rigidbody2D body = selector.Ball.Body;
            switch (phase)
            {
                case Phase.Baseline:
                    if (Time.fixedTime - phaseStarted < 0.12f) return;
                    lesson.RecordBaseline();
                    if (!lesson.HasBaseline) throw new InvalidOperationException("Baseline rejected at saved spawn.");
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
                    var launcher = selector.GetStageComponent<VoltageLauncher2D>(5);
                    float remainingVoltage = targetVoltage - launcher.Voltage;
                    if (remainingVoltage > 0f)
                    {
                        // Use the normal lever to finish at the requested voltage, including its 0.01 dead zone.
                        selector.Lever.SetValue(Mathf.Clamp(remainingVoltage / voltagePerPhysicsStep, 0.0101f, 1f));
                        break;
                    }
                    selector.Lever.SetValue(0f);
                    if (voltageZone == null || !voltageZone.OverlapPoint(body.worldCenterOfMass)) break;
                    lesson.RequestLaunch();
                    Change(Phase.VoltageLaunch);
                    break;
                case Phase.VoltageLaunch:
                    Steer(current.start.x);
                    if (lesson.VoltageChallengeSatisfied)
                    {
                        current.launchVoltage = lesson.LastLaunchVoltage;
                        if (Mathf.Abs(current.launchVoltage - targetVoltage) > 0.01f)
                            throw new InvalidOperationException("Actual launch voltage differed from the requested voltage by more than 0.01 V.");
                        Change(Phase.Route);
                    }
                    break;
                case Phase.Route:
                    if (current.stage == 5)
                    {
                        // The first landing is y=2, the second is y=3, with a one-unit gap at x=2..3.
                        // Wait for a real landing and automatic bounce before crossing to the higher platform.
                        if (waypoint == 0 && !current.realFirstLanding)
                        {
                            Steer(body.position.y < 2.6f && body.position.x < -2.5f ? -2.6f : -0.5f);
                            break;
                        }
                        waypoint = 1;
                        Steer(4.8f);
                        if (current.realSecondLanding && Mathf.Abs(body.position.x - 4.8f) < 0.55f)
                        {
                            Change(Phase.Star);
                        }
                        break;
                    }
                    Collider2D shelf = shelves[waypoint];
                    if (current.stage == 3 && waypoint == 2 && !middleStonePassed)
                    {
                        // Stay on the second shelf while the visible falling stone blocks a full-height bounce.
                        // Start moving right near the beginning of an automatic bounce once the stone is safely above it.
                        Steer(4.6f);
                        float ballRadius = selector.Ball.GetComponent<Collider2D>().bounds.extents.y;
                        float safeStoneBottom = shelves[1].bounds.max.y + ballRadius * 2f + normalBounceRise + 0.1f;
                        bool stoneSafe = middleFallingStone != null && middleFallingStone.bounds.min.y > safeStoneBottom
                            && (middleFallingStone.attachedRigidbody == null || middleFallingStone.attachedRigidbody.linearVelocity.y >= -0.01f);
                        bool earlyBounce = body.linearVelocity.y > 4.5f
                            && body.position.y < shelves[1].bounds.max.y + ballRadius + 1.2f;
                        if (!stoneSafe || !earlyBounce) break;
                        middleStonePassed = true;
                    }
                    float targetX = shelf.bounds.center.x;
                    Steer(targetX);
                    bool near = Mathf.Abs(body.position.x - targetX) < shelf.bounds.extents.x * 0.65f;
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

        private static void Steer(float targetX)
        {
            Rigidbody2D body = selector.Ball.Body;
            float error = targetX - body.position.x;
            float velocity = body.linearVelocity.x;
            float speedLimit = Mathf.Min(maxSpeed, Mathf.Sqrt(2f * acceleration * Mathf.Abs(error)));
            float desired = Mathf.Clamp(error * 2.5f - velocity * 0.45f, -speedLimit, speedLimit);
            // Select the key that moves the saved horizontal velocity toward the desired velocity.
            // This is digital acceleration/braking, with no direct velocity or force changes.
            float velocityError = desired - velocity;
            float value = Mathf.Abs(velocityError) > 0.15f ? Mathf.Sign(velocityError) : 0f;
            if (Mathf.Abs(error) < 0.08f && Mathf.Abs(velocity) < 0.15f) value = 0f;
            SetInput(value);
        }

        private static void SetInput(float value)
        {
            // Press left/right or release both: the exact values produced by the default A/D bindings.
            // A small release band lets the saved deceleration stop the body near a shelf center.
            input = Mathf.Abs(value) < 0.05f ? 0f : Mathf.Sign(value);
            movement.SetMovex(input);
        }

        private static void Change(Phase next)
        {
            phase = next;
            phaseStarted = Time.fixedTime;
        }

        private static void ObserveContacts()
        {
            int count = selector.Ball.Body.GetContacts(contacts);
            for (int i = 0; i < count; i++)
            {
                Collider2D other = contacts[i].collider.attachedRigidbody == selector.Ball.Body
                    ? contacts[i].otherCollider : contacts[i].collider;
                if (other != null && !current.contacts.Contains(other.name)) current.contacts.Add(other.name);
                if (phase == Phase.Route && waypoint < shelves.Count && other == shelves[waypoint]
                    && Mathf.Abs(contacts[i].normal.y) > 0.6f
                    && selector.Ball.Body.position.y > contacts[i].point.y + 0.25f)
                    waypointLanded = true;
                if (current.stage != 5 || phase != Phase.Route || other == null || !other.CompareTag("Ground")) continue;
                Vector2 point = contacts[i].point;
                if (Mathf.Abs(contacts[i].normal.y) < 0.6f || selector.Ball.Body.position.y < point.y + 0.25f) continue;
                if (Mathf.Abs(point.y - 2f) < 0.1f && point.x >= -2.05f && point.x <= 2.05f)
                    current.realFirstLanding = true;
                if (Mathf.Abs(point.y - 3f) < 0.1f && point.x >= 2.95f && point.x <= 7.05f)
                    current.realSecondLanding = true;
            }
        }

        private static void RecordTrace()
        {
            if (Time.fixedTime < nextTrace) return;
            nextTrace = Time.fixedTime + 0.15f;
            current.seconds = Time.fixedTime - started;
            current.finish = selector.Ball.Body.position;
            current.phase = phase + (phase == Phase.Route ? " " + waypoint : "");
            if (current.stage == 3 && phase == Phase.Route && waypoint == 2 && !middleStonePassed)
                current.phase += " Wait for Stone02";
            current.trace.Add(new Trace { time = current.seconds, phase = current.phase,
                position = current.finish, velocity = selector.Ball.Body.linearVelocity, input = input,
                voltage = lesson.CurrentVoltage, stars = selector.Ball.Stars, learning = lesson.CurrentLearningGoalSatisfied });
            WriteReport();
        }

        private static void Complete(bool success, string details)
        {
            if (current.stage == 5)
            {
                success &= current.realFirstLanding && current.realSecondLanding
                    && Mathf.Abs(lesson.LastLaunchVoltage - targetVoltage) <= 0.01f;
                details += " Stage 5 requires observed contacts on both authored landing surfaces and the requested voltage within 0.01 V.";
            }
            current.stayedWithinTopBoundary = current.highestBallEdge <= current.cameraTop + 0.01f;
            success &= current.stayedWithinTopBoundary;
            if (!current.stayedWithinTopBoundary)
                details += " Player collider exceeded the saved camera top boundary.";
            current.status = success ? "passed" : "failed";
            current.details = details;
            current.seconds = Time.fixedTime - started;
            current.finish = selector.Ball.Body.position;
            current.learningSatisfied = lesson.CurrentLearningGoalSatisfied;
            current.launchVoltage = lesson.LastLaunchVoltage;
            SetInput(0f);
            WriteReport();
            int next = current.stage + 1;
            current = null;
            if (next > 4) { Finish(); return; }
            SessionState.SetInt(Prefix + "Stage", next);
            EditorSceneManager.LoadSceneInPlayMode(ScenePath(next), new LoadSceneParameters(LoadSceneMode.Single));
        }

        private static void WriteReport()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, JsonUtility.ToJson(report, true));
        }

        private static void Finish()
        {
            finished = true;
            bool passed = report.errors.Count == 0 && report.routes.Count == 5 - SessionState.GetInt(Prefix + "FirstStage", 3);
            foreach (Route route in report.routes) passed &= route.status == "passed";
            report.status = passed ? "passed" : "failed";
            WriteReport();
            SessionState.SetBool(Prefix + "Active", false);
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            EditorApplication.Exit(passed ? 0 : 1);
        }
    }
}
