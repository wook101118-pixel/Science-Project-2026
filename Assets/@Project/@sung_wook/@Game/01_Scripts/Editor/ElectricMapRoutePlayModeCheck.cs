using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;

namespace sung_wook
{
                 
                                                    
                                                              
                                                     
                  
    [InitializeOnLoad]
    public static class ElectricMapRoutePlayModeCheck
    {
        private const string Prefix = "sung_wook.ElectricMapRoute.Check.";
        private const float StageTimeout = 45f;
        private const double BatchTimeout = 420d;
        private const float TraceInterval = 0.2f;

        private enum Phase
        {
            Contact,
            Baseline,
            ChangedCondition,
            DistanceLaunch,
            FieldLift,
            Charge,
            VoltageLaunch,
            RoutePlatform,
            Star,
            Exit
        }

        [Serializable]
        private sealed class Trace
        {
            public float seconds;
            public string phase;
            public Vector2 position;
            public Vector2 velocity;
            public float horizontalInput;
            public int stars;
            public bool learning;
            public float voltage;
        }

        [Serializable]
        private sealed class ObstacleMotionObservation
        {
            public string name;
            public Vector2 start;
            public float maximumDisplacement;
            public bool kinematicSolid;
            public float observedVerticalSpeed;
            [NonSerialized] public Vector2 previousPosition;
            [NonSerialized] public float previousSampleTime;
        }

        [Serializable]
        private sealed class RouteResult
        {
            public int stage;
            public string status = "running";
            public string details;
            public float seconds;
            public float peakY;
            public float highestBallEdge;
            public float cameraTop;
            public bool stayedWithinTopBoundary;
            public Vector2 start;
            public Vector2 finish;
            public bool learningSatisfied;
            public bool realStarEvent;
            public bool realGoalEvent;
            public bool starCollected;
            public bool goalCleared;
            public int stars;
            public float launchVoltage;
            public float baselineDistance;
            public float resultDistance;
            public string lastPhase;
            public int requiredPlatforms;
            public int landedPlatforms;
            public int solidObstacles;
            public string starPlatform;
            public float obstacleWaitSeconds;
            public bool timedObstacleDeparture;
            public bool authoredLearningView;
            public bool liveLearningViewMatched;
            public bool koreanFontAvailable;
            public bool baselineViewMatched;
            public bool feedbackAudioConfigured;
            public string displayedProgress;
            public string displayedMeasurement;
            public List<string> plannedRoute = new List<string>();
            public List<string> obstacleLayout = new List<string>();
            public List<ObstacleMotionObservation> movingObstacles = new List<ObstacleMotionObservation>();
            public List<string> supportContacts = new List<string>();
            public List<string> routeLandings = new List<string>();
            public List<Trace> trace = new List<Trace>();
        }

        [Serializable]
        private sealed class Report
        {
            public string status = "running";
            public string scenePath = ElectricBounceSceneInstaller.ScenePath;
            public string movementMethod = "Existing PlayerMovement.SetMovex; ordinary learning and lever commands";
            public string proof = "No route teleports or fake star/goal calls; real OnCollected and OnCleared events";
            public bool minimumVoltageOnly;
            public float activeSeconds;
            public List<RouteResult> routes = new List<RouteResult>();
            public List<string> errors = new List<string>();
        }

        private static Report report;
        private static RouteResult current;
        private static ElectricStageSelector selector;
        private static ElectricLearningController2D lesson;
        private static ElectricStar2D star;
        private static ElectricGoal2D goal;
        private static PlayerMovement movement;
        private static PlayerController playerController;
        private static PlayerInputReader inputReader;
        private static bool controllerWasEnabled;
        private static bool readerWasEnabled;
        private static readonly List<Collider2D> RoutePlatforms = new List<Collider2D>();
        private static readonly HashSet<Collider2D> LandedPlatforms = new HashSet<Collider2D>();
        private static readonly List<Collider2D> Obstacles = new List<Collider2D>();
        private static readonly Dictionary<Rigidbody2D, ObstacleMotionObservation> ObstacleMotion = new Dictionary<Rigidbody2D, ObstacleMotionObservation>();
        private static int routeIndex;
        private static Collider2D starPlatform;
        private static BoxCollider2D voltageZone;
        private static readonly ContactPoint2D[] Contacts = new ContactPoint2D[24];
        private static UnityAction collectedListener;
        private static UnityAction clearedListener;
        private static Phase phase;
        private static float phaseStarted;
        private static float stageStarted;
        private static float nextTrace;
        private static float nextReport;
        private static float obstacleWaitStarted;
        private static float previousPhysicsTime = -1f;
        private static Vector2 previousVelocity;
        private static float input;
        private static float maxMoveSpeed;
        private static float acceleration;
        private static float startGravity;
        private static float startMass;
        private static bool hasBegunRoute;
        private static bool finished;

        static ElectricMapRoutePlayModeCheck()
        {
            if (Application.isBatchMode && SessionState.GetBool(Prefix + "Active", false))
                Resume();
        }

                                                                              
        public static void RunBatch()
        {
            StartBatch(false);
        }

                                                                      
        public static void RunMinimumVoltageBatch()
        {
            StartBatch(true);
        }

        private static void StartBatch(bool minimumVoltageOnly)
        {
            if (!Application.isBatchMode)
            {
                Debug.LogWarning("ElectricMapRoutePlayModeCheck runs only in an explicitly launched batch editor.");
                return;
            }

            EditorSceneManager.OpenScene(ElectricBounceSceneInstaller.ScenePath, OpenSceneMode.Single);
            SessionState.SetBool(Prefix + "Active", true);
            SessionState.SetBool(Prefix + "MinimumVoltageOnly", minimumVoltageOnly);
            SessionState.SetString(Prefix + "Started", Now());
            SessionState.SetString(Prefix + "PlayStarted", "");
            SessionState.SetString(Prefix + "Errors", JsonUtility.ToJson(new Report()));
            report = null;
            finished = false;
            Resume();
            ElectricBatchPlayMode.EnterAfterEditorStartup();
        }

        private static string Now() => EditorApplication.timeSinceStartup.ToString(CultureInfo.InvariantCulture);

        private static double Timestamp(string name)
        {
            double.TryParse(SessionState.GetString(Prefix + name, "0"), NumberStyles.Float,
                CultureInfo.InvariantCulture, out double value);
            return value;
        }

        private static void Resume()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            Application.logMessageReceived -= CaptureLog;
            Application.logMessageReceived += CaptureLog;
        }

                                                             
        private static void CaptureLog(string message, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)
                return;
            string text = message + "\n" + stackTrace;
            if (report != null)
                report.errors.Add(text);
            else
            {
                Report pending = JsonUtility.FromJson<Report>(SessionState.GetString(Prefix + "Errors", "{}"));
                if (pending.errors == null) pending.errors = new List<string>();
                pending.errors.Add(text);
                SessionState.SetString(Prefix + "Errors", JsonUtility.ToJson(pending));
            }
        }

        private static void Tick()
        {
            if (!Application.isBatchMode || !SessionState.GetBool(Prefix + "Active", false) || finished)
                return;
            try
            {
                if (EditorApplication.timeSinceStartup - Timestamp("Started") > BatchTimeout)
                {
                    EnsureReport();
                    report.errors.Add("Batch watchdog exceeded 420 seconds.");
                    if (current != null && current.status == "running") CompleteRoute(false, "Batch timeout.");
                    Finish();
                    return;
                }
                if (!EditorApplication.isPlaying || EditorApplication.isPaused)
                    return;
                if (report == null)
                    InitializePlay();
                if (finished || current == null)
                    return;

                                                         
                if (Mathf.Approximately(previousPhysicsTime, Time.fixedTime))
                    return;
                previousPhysicsTime = Time.fixedTime;
                Rigidbody2D body = selector.Ball.Body;
                current.peakY = Mathf.Max(current.peakY, body.position.y);
                current.highestBallEdge = Mathf.Max(current.highestBallEdge,
                    selector.Ball.GetComponent<Collider2D>().bounds.max.y);
                ObserveSupports(body);
                ObserveObstacleMotion();

                if (!Mathf.Approximately(body.gravityScale, startGravity)
                    || !Mathf.Approximately(body.mass, startMass))
                    throw new InvalidOperationException("Player gravity or mass changed during the route.");
                if (goal.IsCleared)
                {
                    CompleteRoute(current.realStarEvent && current.realGoalEvent
                        && star.IsCollected && selector.Ball.Stars >= 1 && lesson.CurrentLearningGoalSatisfied
                        && LandedPlatforms.Count == RoutePlatforms.Count,
                        "Actual exit callback completed after the physical route.");
                    NextStage();
                    return;
                }
                if (body.position.y < -9f || Mathf.Abs(body.position.x) > 25f
                    || Time.fixedTime - stageStarted > StageTimeout)
                {
                    RecordTrace(true);
                    CompleteRoute(false, "Route fell outside the map or exceeded 45 simulation seconds.");
                    NextStage();
                    return;
                }

                DriveLessonAndRoute();
                RecordTrace(false);
                previousVelocity = body.linearVelocity;
            }
            catch (Exception exception)
            {
                EnsureReport();
                report.errors.Add(exception.ToString());
                if (current != null && current.status == "running") CompleteRoute(false, exception.Message);
                Finish();
            }
        }

        private static void EnsureReport()
        {
            if (report != null) return;
            report = JsonUtility.FromJson<Report>(SessionState.GetString(Prefix + "Errors", "{}"));
            if (report.routes == null) report.routes = new List<RouteResult>();
            if (report.errors == null) report.errors = new List<string>();
        }

        private static void InitializePlay()
        {
            EnsureReport();
            report.minimumVoltageOnly = SessionState.GetBool(Prefix + "MinimumVoltageOnly", false);
            SessionState.SetString(Prefix + "PlayStarted", Now());
            selector = UnityEngine.Object.FindFirstObjectByType<ElectricStageSelector>();
            if (selector == null || selector.Ball == null || selector.Lever == null)
                throw new InvalidOperationException("Original scene player/lever/selector binding is missing.");
            lesson = selector.GetComponent<ElectricLearningController2D>();
            movement = selector.Ball.GetComponent<PlayerMovement>();
            playerController = selector.Ball.GetComponent<PlayerController>();
            inputReader = selector.Ball.GetComponent<PlayerInputReader>();
            if (lesson == null || movement == null || playerController == null || inputReader == null)
                throw new InvalidOperationException("Existing player or lesson components are missing.");

                                                         
                                                           
            controllerWasEnabled = playerController.enabled;
            readerWasEnabled = inputReader.enabled;
            playerController.enabled = false;
            inputReader.enabled = false;
            var serializedMovement = new SerializedObject(movement);
            maxMoveSpeed = serializedMovement.FindProperty("maxMoveSpeed").floatValue;
            if (maxMoveSpeed <= 0f) maxMoveSpeed = serializedMovement.FindProperty("moveSpeed").floatValue;
            acceleration = serializedMovement.FindProperty("acceleration").floatValue;
            startGravity = selector.Ball.Body.gravityScale;
            startMass = selector.Ball.Body.mass;
            if (maxMoveSpeed <= 0f || acceleration <= 0f)
                throw new InvalidOperationException("Existing player movement settings cannot navigate a route.");
            BeginStage(report.minimumVoltageOnly ? 5 : 2);
        }

        private static void BeginStage(int stageNumber)
        {
            RemoveListeners();
                                                                  
            selector.SelectStage(stageNumber);
            selector.Lever.SetValue(stageNumber == 2 || stageNumber == 5 ? 1f : 0f);
            if (stageNumber == 4) lesson.SetBallCharge(-1f);                        
            star = selector.GetStageComponent<ElectricStar2D>(stageNumber);
            goal = selector.GetStageComponent<ElectricGoal2D>(stageNumber);
            if (star == null || goal == null) throw new InvalidOperationException("Stage star or exit is missing.");
            FindPlatforms(stageNumber);
            stageStarted = Time.fixedTime;
            phaseStarted = Time.fixedTime;
            nextTrace = Time.fixedTime;
            nextReport = Time.fixedTime;
            obstacleWaitStarted = -1f;
            LandedPlatforms.Clear();
            routeIndex = 0;
            hasBegunRoute = false;
            previousVelocity = Vector2.zero;
            previousPhysicsTime = -1f;
            current = new RouteResult
            {
                stage = stageNumber,
                start = selector.Ball.Body.position,
                peakY = selector.Ball.Body.position.y,
                highestBallEdge = selector.Ball.GetComponent<Collider2D>().bounds.max.y,
                cameraTop = Camera.main.transform.position.y + Camera.main.orthographicSize,
                requiredPlatforms = RoutePlatforms.Count,
                solidObstacles = Obstacles.Count,
                starPlatform = starPlatform != null ? starPlatform.name : ""
            };
            foreach (Collider2D platform in RoutePlatforms)
                current.plannedRoute.Add(DescribeCollider(platform));
            foreach (Collider2D obstacle in Obstacles)
            {
                current.obstacleLayout.Add(DescribeCollider(obstacle));
                if (obstacle.GetComponent<ElectricObstacleMotion2D>() == null) continue;
                Rigidbody2D obstacleBody = obstacle.attachedRigidbody;
                if (obstacleBody == null)
                    throw new InvalidOperationException("Moving obstacle has no Rigidbody2D: " + obstacle.name);
                var observation = new ObstacleMotionObservation
                {
                    name = obstacle.name,
                    start = obstacleBody.position,
                    kinematicSolid = obstacleBody.bodyType == RigidbodyType2D.Kinematic && !obstacle.isTrigger,
                    previousPosition = obstacleBody.position,
                    previousSampleTime = Time.fixedTime
                };
                ObstacleMotion.Add(obstacleBody, observation);
                current.movingObstacles.Add(observation);
            }
            report.routes.Add(current);
            RouteResult captured = current;
            collectedListener = () => captured.realStarEvent = true;
            clearedListener = () => captured.realGoalEvent = true;
            star.OnCollected.AddListener(collectedListener);
            goal.OnCleared.AddListener(clearedListener);
            phase = stageNumber == 2 ? Phase.Contact : stageNumber == 3 ? Phase.Baseline
                : stageNumber == 4 ? Phase.FieldLift : Phase.Charge;
            SetInput(0f);
            RecordTrace(true);
        }

                     
                                                         
                                                        
                                                      
                      
        private static void FindPlatforms(int stageNumber)
        {
            Transform root = selector.StageRoots[stageNumber - 2].transform;
            Transform layout = null;
            foreach (Transform child in root)
                if (child.name.StartsWith("Map Layout - Stage", StringComparison.Ordinal)) layout = child;
            if (layout == null) throw new InvalidOperationException("Saved Map Layout group is missing in Stage " + stageNumber);
            RoutePlatforms.Clear();
            Obstacles.Clear();
            ObstacleMotion.Clear();
            foreach (Collider2D collider in layout.GetComponentsInChildren<Collider2D>())
            {
                if (collider.name.StartsWith("Route ", StringComparison.Ordinal))
                {
                    if (collider.isTrigger || !collider.enabled || !collider.CompareTag("Ground"))
                        throw new InvalidOperationException("Saved route platform must be an enabled, solid Ground collider: " + collider.name);
                    RoutePlatforms.Add(collider);
                }
                else if (collider.name.StartsWith("Obstacle ", StringComparison.Ordinal))
                {
                    if (collider.isTrigger || !collider.enabled)
                        throw new InvalidOperationException("Saved obstacle must be a solid enabled collider: " + collider.name);
                    Obstacles.Add(collider);
                }
            }
            RoutePlatforms.Sort((left, right) => string.CompareOrdinal(left.name, right.name));
            if (RoutePlatforms.Count < 2)
                throw new InvalidOperationException("Expected at least two numbered saved route platforms, found " + RoutePlatforms.Count);
                                                          
                                                               
            starPlatform = null;
            float closestStarDistance = float.PositiveInfinity;
            foreach (Collider2D platform in RoutePlatforms)
            {
                float distance = Mathf.Abs(platform.bounds.center.x - star.transform.position.x)
                    + Mathf.Abs(platform.bounds.max.y - star.transform.position.y) * 0.1f;
                if (distance >= closestStarDistance) continue;
                closestStarDistance = distance;
                starPlatform = platform;
            }
            voltageZone = stageNumber == 5 ? root.Find("Launch Zone")?.GetComponent<BoxCollider2D>() : null;
            if (stageNumber == 5 && voltageZone == null)
                throw new InvalidOperationException("Original voltage launch zone is missing.");
        }

        private static string DescribeCollider(Collider2D collider)
        {
            Bounds bounds = collider.bounds;
            return collider.name + " center=" + bounds.center.ToString("F2") + " size=" + bounds.size.ToString("F2");
        }

                                                                             
        private static void ObserveObstacleMotion()
        {
            foreach (KeyValuePair<Rigidbody2D, ObstacleMotionObservation> pair in ObstacleMotion)
            {
                pair.Value.maximumDisplacement = Mathf.Max(pair.Value.maximumDisplacement,
                    Vector2.Distance(pair.Key.position, pair.Value.start));
                float elapsed = Time.fixedTime - pair.Value.previousSampleTime;
                if (elapsed > 0f)
                    pair.Value.observedVerticalSpeed = (pair.Key.position.y - pair.Value.previousPosition.y) / elapsed;
                pair.Value.previousPosition = pair.Key.position;
                pair.Value.previousSampleTime = Time.fixedTime;
            }
        }

        private static void DriveLessonAndRoute()
        {
            Rigidbody2D body = selector.Ball.Body;
            float elapsed = Time.fixedTime - phaseStarted;
            switch (phase)
            {
                case Phase.Contact:
                    Steer(selector.SpawnPoint.position.x);
                    if (lesson.ContactChallengeSatisfied) ChangePhase(Phase.RoutePlatform);
                    break;
                case Phase.Baseline:
                    SetInput(0f);
                    if (elapsed < 0.1f) break;                                    
                    lesson.RecordBaseline();
                    if (!lesson.HasBaseline) throw new InvalidOperationException("Prepared baseline was not accepted.");
                    current.baselineViewMatched = ValidateLearningView(false);
                    current.baselineDistance = lesson.BaselineSample.Distance;
                    selector.Lever.SetValue(0.95f);
                    ChangePhase(Phase.ChangedCondition);
                    break;
                case Phase.ChangedCondition:
                    if (elapsed < 0.1f) break;
                    lesson.RequestLaunch();
                    ChangePhase(Phase.DistanceLaunch);
                    break;
                case Phase.DistanceLaunch:
                    if (!lesson.ComparisonChallengeSatisfied) break;
                    current.resultDistance = lesson.ResultSample.Distance;
                    ChangePhase(Phase.RoutePlatform);
                    break;
                case Phase.FieldLift:
                    Steer(selector.SpawnPoint.position.x);
                    if (lesson.NegativeChallengeSatisfied && body.position.y >= 0.8f)
                        ChangePhase(Phase.RoutePlatform);
                    break;
                case Phase.Charge:
                    Steer(selector.SpawnPoint.position.x);
                    var launcher = selector.GetStageComponent<VoltageLauncher2D>(5);
                    float targetVoltage = report.minimumVoltageOnly ? 50f : 55f;
                    if (launcher.Voltage < targetVoltage) break;
                                                                 
                                                               
                    if (report.minimumVoltageOnly) selector.Lever.SetValue(0f);
                    if (!voltageZone.OverlapPoint(body.worldCenterOfMass)) break;
                    selector.Lever.SetValue(0f);
                    lesson.RequestLaunch();
                    ChangePhase(Phase.VoltageLaunch);
                    break;
                case Phase.VoltageLaunch:
                    Steer(selector.SpawnPoint.position.x);
                    if (!lesson.VoltageChallengeSatisfied) break;
                    current.launchVoltage = lesson.LastLaunchVoltage;
                    ChangePhase(Phase.RoutePlatform);
                    break;
                case Phase.RoutePlatform:
                    hasBegunRoute = true;
                    Collider2D target = RoutePlatforms[routeIndex];
                    Steer(target.bounds.center.x);
                    if (!LandedPlatforms.Contains(target)) break;
                    if (target == starPlatform && !star.IsCollected)
                    {
                        Steer(star.transform.position.x);
                        break;
                    }
                    if (current.stage == 3 && routeIndex == 1 && !ReadyToLeaveFallingStonePlatform(target))
                        break;
                    if (routeIndex + 1 >= RoutePlatforms.Count)
                    {
                        ChangePhase(Phase.Star);
                        break;
                    }
                                                             
                                                            
                                                                
                    routeIndex++;
                    Steer(RoutePlatforms[routeIndex].bounds.center.x);
                    RecordTrace(true);
                    break;
                case Phase.Star:
                    Steer(star.transform.position.x);
                    if (star.IsCollected && current.realStarEvent) ChangePhase(Phase.Exit);
                    break;
                case Phase.Exit:
                    Steer(goal.transform.position.x);
                    break;
            }
        }

                     
                                                          
                                                                
                      
        private static bool ReadyToLeaveFallingStonePlatform(Collider2D platform)
        {
            Rigidbody2D nearestStone = null;
            float nearestDistance = float.PositiveInfinity;
            foreach (Rigidbody2D stone in ObstacleMotion.Keys)
            {
                float distance = Mathf.Abs(stone.position.x - platform.bounds.center.x);
                if (distance >= nearestDistance) continue;
                nearestDistance = distance;
                nearestStone = stone;
            }
            if (nearestStone == null) throw new InvalidOperationException("The falling-stone route is missing its moving obstacle.");

            Collider2D ballCollider = selector.Ball.GetComponent<Collider2D>();
            float radius = ballCollider.bounds.extents.y;
            float safeX = platform.bounds.min.x + radius * 0.5f;
            Steer(safeX);
            if (obstacleWaitStarted < 0f) obstacleWaitStarted = Time.fixedTime;
            current.obstacleWaitSeconds = Time.fixedTime - obstacleWaitStarted;

            Collider2D stoneCollider = nearestStone.GetComponent<Collider2D>();
                                                               
            float clearHeight = platform.bounds.max.y + radius * 2f + 2.5f;
            bool stoneClear = stoneCollider.bounds.min.y >= clearHeight
                && ObstacleMotion[nearestStone].observedVerticalSpeed >= -0.05f
                && ObstacleMotion[nearestStone].maximumDisplacement > 0.5f;
            Rigidbody2D body = selector.Ball.Body;
            bool newBounce = body.linearVelocity.y > 5.5f
                && body.position.y < platform.bounds.max.y + radius + 0.8f;
            if (!stoneClear || !newBounce) return false;
            current.timedObstacleDeparture = true;
            return true;
        }

                                                                      
        private static void Steer(float targetX)
        {
            Rigidbody2D body = selector.Ball.Body;
            float error = targetX - body.position.x;
            float brakingSpeed = Mathf.Sqrt(2f * acceleration * Mathf.Abs(error));
            float desiredSpeed = Mathf.Sign(error) * Mathf.Min(maxMoveSpeed, brakingSpeed);
            desiredSpeed = Mathf.Clamp(error * 2.5f - body.linearVelocity.x * 0.45f, -Mathf.Abs(desiredSpeed), Mathf.Abs(desiredSpeed));
            float value = desiredSpeed / maxMoveSpeed;
                                                                
            if (Mathf.Abs(value) < 0.012f && Mathf.Abs(body.linearVelocity.x) > 0.04f)
                value = -Mathf.Sign(body.linearVelocity.x) * 0.012f;
            SetInput(value);
        }

        private static void SetInput(float value)
        {
            input = Mathf.Clamp(value, -1f, 1f);
            movement.SetMovex(input);
        }

        private static void ChangePhase(Phase next)
        {
            phase = next;
            phaseStarted = Time.fixedTime;
            RecordTrace(true);
        }

                     
                                                       
                                                              
                                                   
                      
        private static void ObserveSupports(Rigidbody2D body)
        {
            int count = body.GetContacts(Contacts);
            for (int i = 0; i < count; i++)
            {
                ContactPoint2D contact = Contacts[i];
                Collider2D support = contact.collider != null && contact.collider.attachedRigidbody != body
                    ? contact.collider : contact.otherCollider;
                if (support == null || support.attachedRigidbody == body || !support.CompareTag("Ground")) continue;
                if (Mathf.Abs(contact.normal.y) < 0.6f || body.position.y < support.bounds.max.y) continue;
                string name = support.name;
                if (!current.supportContacts.Contains(name)) current.supportContacts.Add(name);
                if (RoutePlatforms.Contains(support)) RegisterLanding(support, "contact");
            }
            if (!hasBegunRoute || body.linearVelocity.y < 1f || previousVelocity.y > 0.1f) return;
            foreach (Collider2D platform in RoutePlatforms)
                if (!LandedPlatforms.Contains(platform) && NearSupportTop(platform))
                    RegisterLanding(platform, "observed bounce");
        }

        private static bool NearSupportTop(Collider2D platform)
        {
            Vector2 position = selector.Ball.Body.position;
            Bounds bounds = platform.bounds;
            return position.x >= bounds.min.x - 0.1f && position.x <= bounds.max.x + 0.1f
                && position.y >= bounds.max.y && position.y < bounds.max.y + 0.9f;
        }

        private static void RegisterLanding(Collider2D platform, string evidence)
        {
            if (!LandedPlatforms.Add(platform)) return;
            current.routeLandings.Add(platform.name + ": " + evidence
                + " at " + (Time.fixedTime - stageStarted).ToString("0.00", CultureInfo.InvariantCulture) + " s");
        }

        private static void RecordTrace(bool force)
        {
            if (current == null || (!force && Time.fixedTime < nextTrace)) return;
            nextTrace = Time.fixedTime + TraceInterval;
            current.trace.Add(new Trace
            {
                seconds = Time.fixedTime - stageStarted,
                phase = phase == Phase.RoutePlatform ? phase + " " + (routeIndex + 1) : phase.ToString(),
                position = selector.Ball.Body.position,
                velocity = selector.Ball.Body.linearVelocity,
                horizontalInput = input,
                stars = selector.Ball.Stars,
                learning = lesson.CurrentLearningGoalSatisfied,
                voltage = current.stage == 5 ? lesson.CurrentVoltage : 0f
            });
                                                              
            current.seconds = Time.fixedTime - stageStarted;
            current.finish = selector.Ball.Body.position;
            current.lastPhase = phase == Phase.RoutePlatform ? phase + " " + (routeIndex + 1) : phase.ToString();
            current.landedPlatforms = LandedPlatforms.Count;
            if (force || Time.fixedTime >= nextReport)
            {
                nextReport = Time.fixedTime + 2f;
                WriteReport();
            }
        }

        private static void CompleteRoute(bool passed, string details)
        {
                                                              
            bool viewMatched = ValidateLearningView(true);
            passed &= viewMatched;
                                                          
            current.stayedWithinTopBoundary = current.highestBallEdge <= current.cameraTop + 0.01f;
            if (current.stage == 5) passed &= current.stayedWithinTopBoundary;
            if (current.stage == 3)
            {
                passed &= current.movingObstacles.Count == 3;
                foreach (ObstacleMotionObservation observation in current.movingObstacles)
                    passed &= observation.kinematicSolid && observation.maximumDisplacement >= 0.2f;
            }
            current.status = passed ? "passed" : "failed";
            current.details = details;
            current.seconds = Time.fixedTime - stageStarted;
            current.finish = selector.Ball.Body.position;
            current.learningSatisfied = lesson.CurrentLearningGoalSatisfied;
            current.starCollected = star.IsCollected;
            current.goalCleared = goal.IsCleared;
            current.stars = selector.Ball.Stars;
            current.launchVoltage = current.stage == 5 ? lesson.LastLaunchVoltage : 0f;
            current.lastPhase = phase.ToString();
            current.landedPlatforms = LandedPlatforms.Count;
            SetInput(0f);
            WriteReport();
        }

                     
                                                          
                                             
                      
        private static bool ValidateLearningView(bool final)
        {
            GameObject root = selector.StageRoots[current.stage - 2];
            ElectricLearningStatusView[] views = root.GetComponentsInChildren<ElectricLearningStatusView>(true);
            current.authoredLearningView = views.Length == 1 && views[0].gameObject.scene == root.scene;
            if (!current.authoredLearningView) return false;

            ElectricLearningStatusView view = views[0];
            view.RefreshDisplay();
            SerializedObject data = new SerializedObject(view);
            var progress = data.FindProperty("progressText").objectReferenceValue as UnityEngine.UI.Text;
            var measurement = data.FindProperty("measurementText").objectReferenceValue as UnityEngine.UI.Text;
            var charge = data.FindProperty("chargeText").objectReferenceValue as UnityEngine.UI.Text;
            bool hasLabels = progress != null && measurement != null && charge != null;
            current.koreanFontAvailable = hasLabels && progress.font != null
                && progress.font.HasCharacter('한') && progress.font.HasCharacter('전');
            AudioSource source = view.GetComponent<AudioSource>();
            current.feedbackAudioConfigured = source != null && !source.playOnAwake && source.volume <= 0.3f;
            foreach (string key in new[] { "recordSound", "collectSound", "completeSound" })
            {
                var clip = data.FindProperty(key).objectReferenceValue as AudioClip;
                current.feedbackAudioConfigured &= clip != null && clip.length > 0f && clip.samples > 0;
            }
            if (!hasLabels) return false;

            current.displayedProgress = progress.text;
            current.displayedMeasurement = measurement.text;
            bool learned = lesson.CurrentLearningGoalSatisfied;
            bool hasStar = selector.Ball.Stars > 0;
            bool matches = PairMatches(data, "learningPending", "learningComplete", learned)
                && PairMatches(data, "starPending", "starComplete", hasStar)
                && PairMatches(data, "exitLocked", "exitReady", learned && hasStar);
            var clearedBadge = data.FindProperty("clearedBadge").objectReferenceValue as GameObject;
            matches &= clearedBadge != null && clearedBadge.activeSelf == goal.IsCleared;
            matches &= measurement.text.Length > 0 && charge.text.Contains("공의 전하");
            if (final) matches &= progress.text.Contains("실험 성공") && goal.IsCleared;
            else matches &= lesson.HasBaseline && measurement.text.Contains("기준 r");
            current.liveLearningViewMatched = matches && current.koreanFontAvailable && current.feedbackAudioConfigured;
            return current.liveLearningViewMatched;
        }

        private static bool PairMatches(SerializedObject data, string pendingName, string completeName, bool value)
        {
            var pending = data.FindProperty(pendingName).objectReferenceValue as GameObject;
            var complete = data.FindProperty(completeName).objectReferenceValue as GameObject;
            return pending != null && complete != null
                && pending.activeSelf == !value && complete.activeSelf == value;
        }

        private static void NextStage()
        {
            if (current.stage >= 5) Finish();
            else BeginStage(current.stage + 1);
        }

        private static void RemoveListeners()
        {
            if (star != null && collectedListener != null) star.OnCollected.RemoveListener(collectedListener);
            if (goal != null && clearedListener != null) goal.OnCleared.RemoveListener(clearedListener);
            collectedListener = null;
            clearedListener = null;
        }

        private static void WriteReport()
        {
            string directory = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Logs");
            Directory.CreateDirectory(directory);
            string fileName = report.minimumVoltageOnly ? "ElectricMapMinimumVoltageReport.json" : "ElectricMapRoutePlayModeReport.json";
            File.WriteAllText(Path.Combine(directory, fileName), JsonUtility.ToJson(report, true));
        }

        private static void Finish()
        {
            if (!Application.isBatchMode || finished) return;
            finished = true;
            EnsureReport();
            bool passed = report.errors.Count == 0 && report.routes.Count == (report.minimumVoltageOnly ? 1 : 4);
            foreach (RouteResult result in report.routes) passed &= result.status == "passed";
            if (report.minimumVoltageOnly && report.routes.Count == 1)
                passed &= report.routes[0].launchVoltage >= 50f && report.routes[0].launchVoltage <= 50.5f;
            report.status = passed ? "passed" : "failed";
            double playStarted = Timestamp("PlayStarted");
            report.activeSeconds = playStarted > 0d ? (float)(EditorApplication.timeSinceStartup - playStarted) : 0f;
            WriteReport();
            RemoveListeners();
            if (movement != null) movement.SetMovex(0f);
            if (selector != null) selector.Lever.UseLeverInput();
            if (inputReader != null) inputReader.enabled = readerWasEnabled;
            if (playerController != null) playerController.enabled = controllerWasEnabled;
            SessionState.SetBool(Prefix + "Active", false);
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= CaptureLog;
            EditorApplication.Exit(passed ? 0 : 1);
        }
    }
}
