using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

namespace sung_wook
{
                                                                                
    [InitializeOnLoad]
    public static class ElectricLearningPlayModeCheck
    {
        private const string ScenePath = "Assets/@Project/@sung_wook/@Game/Scenes/sungwook_ElectricBounce.unity";
        private const string Prefix = "sung_wook.ElectricLearning.PlayModeCheck.";
        private static EditorWindow visualGameView;

        [Serializable]
        private sealed class Check
        {
            public string name;
            public bool passed;
            public string details;
        }

        [Serializable]
        private sealed class Report
        {
            public string status = "running";
            public string gameplayStatus = "running";
            public string environmentStatus = "running";
            public string visualStatus = "not-requested";
            public string mode = "gameplay-with-optional-captures";
            public string scenePath = ScenePath;
            public float activeSeconds;
            public List<Check> checks = new List<Check>();
            public List<string> errors = new List<string>();
            public List<string> environmentalErrors = new List<string>();
            public List<string> screenshotPaths = new List<string>();
            public List<string> screenshotNotes = new List<string>();
        }

        static ElectricLearningPlayModeCheck()
        {
            if (Application.isBatchMode && SessionState.GetBool(Prefix + "Active", false)) Resume();
        }

        public static void RunBatch()
        {
            RunGameplay(ScenePath);
        }

                                                                   
        public static void RunOriginalBatch()
        {
            RunGameplay(ElectricBounceSceneInstaller.ScenePath);
        }

        private static void RunGameplay(string scenePath)
        {
            if (!Application.isBatchMode)
            {
                Debug.LogWarning("ElectricLearningPlayModeCheck requires an explicitly launched batch editor.");
                return;
            }
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            SessionState.SetBool(Prefix + "VisualOnly", false);
            SessionState.SetBool(Prefix + "Active", true);
            SessionState.SetInt(Prefix + "Phase", 0);
            SessionState.SetString(Prefix + "Started", EditorApplication.timeSinceStartup.ToString(CultureInfo.InvariantCulture));
            SessionState.SetString(Prefix + "PlayStarted", "");
            Save(new Report { scenePath = scenePath });
            CheckSavedOriginalArt(scenePath);
            Resume();
            ElectricBatchPlayMode.EnterAfterEditorStartup();
        }

        public static void RunVisualBatch()
        {
            RunVisual(ScenePath);
        }

        public static void RunOriginalVisualBatch()
        {
            RunVisual(ElectricBounceSceneInstaller.ScenePath);
        }

        private static void RunVisual(string scenePath)
        {
            if (!Application.isBatchMode)
            {
                Debug.LogWarning("RunVisualBatch requires an explicitly launched batch editor.");
                return;
            }
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            SessionState.SetBool(Prefix + "VisualOnly", true);
            SessionState.SetBool(Prefix + "Active", true);
            SessionState.SetInt(Prefix + "Phase", 20);
            SessionState.SetFloat(Prefix + "Deadline", 0f);
            SessionState.SetString(Prefix + "Started", EditorApplication.timeSinceStartup.ToString(CultureInfo.InvariantCulture));
            SessionState.SetString(Prefix + "PlayStarted", "");
            Save(new Report { scenePath = scenePath, mode = "visual-only", gameplayStatus = "not-run", visualStatus = "running" });
            CheckSavedOriginalArt(scenePath);
            Resume();
            ElectricBatchPlayMode.EnterAfterEditorStartup();
        }

        private static void Resume()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            Application.logMessageReceived -= CaptureLog;
            Application.logMessageReceived += CaptureLog;
            if (SessionState.GetBool(Prefix + "VisualOnly", false))
            {
                Type gameViewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
                if (gameViewType != null)
                {
                    visualGameView = EditorWindow.GetWindow(gameViewType);
                    SelectVerificationAspect(gameViewType);
                    visualGameView.Repaint();
                }
            }
        }

                     
                                                      
                                                                
                                             
                      
        private static void SelectVerificationAspect(Type gameViewType)
        {
            const BindingFlags instanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            const BindingFlags staticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy;
            Type sizesType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameViewSizes");
            object sizes = sizesType.GetProperty("instance", staticFlags).GetValue(null);
            object group = sizesType.GetProperty("currentGroup", instanceFlags).GetValue(sizes);
            string[] labels = (string[])group.GetType().GetMethod("GetDisplayTexts", instanceFlags).Invoke(group, null);
            for (int index = 0; index < labels.Length; index++)
            {
                if (labels[index].StartsWith("16:9", StringComparison.Ordinal))
                {
                    gameViewType.GetProperty("selectedSizeIndex", instanceFlags).SetValue(visualGameView, index);
                    return;
                }
            }
            throw new InvalidOperationException("The validation editor has no existing 16:9 Game View preset.");
        }

        private static void CaptureLog(string message, string stackTrace, LogType type)
        {
            if (type != LogType.Exception && type != LogType.Error && type != LogType.Assert) return;
            Report report = Load();
            string complete = message + "\n" + stackTrace;
                                                                                                   
            if (message.Contains("ArgumentOutOfRangeException")
                && stackTrace.Contains("UnityEditor.Search.SearchDatabase"))
                report.environmentalErrors.Add(complete);
            else report.errors.Add(complete);
            Save(report);
        }

        private static double Timestamp(string name)
        {
            double.TryParse(SessionState.GetString(Prefix + name, "0"), NumberStyles.Float,
                CultureInfo.InvariantCulture, out double value);
            return value;
        }

        private static void Tick()
        {
            if (!Application.isBatchMode || !SessionState.GetBool(Prefix + "Active", false)) return;
            try
            {
                if (visualGameView != null) visualGameView.Repaint();
                if (EditorApplication.timeSinceStartup - Timestamp("Started") > 120d)
                {
                    AddCheck("watchdog", false, "Batch did not finish within 120 seconds.");
                    Finish();
                    return;
                }
                if (!EditorApplication.isPlaying || EditorApplication.isPaused) return;
                if (string.IsNullOrEmpty(SessionState.GetString(Prefix + "PlayStarted", "")))
                    SessionState.SetString(Prefix + "PlayStarted", EditorApplication.timeSinceStartup.ToString(CultureInfo.InvariantCulture));
                if (EditorApplication.timeSinceStartup - Timestamp("PlayStarted") > 30d)
                {
                    AddCheck("playmode-time-limit", false, "Checks exceeded 30 seconds of active Play Mode.");
                    Finish();
                    return;
                }

                var selector = UnityEngine.Object.FindFirstObjectByType<ElectricStageSelector>();
                if (selector == null || selector.Ball == null || selector.Lever == null)
                    throw new InvalidOperationException("Installed selector, ball or lever is missing.");
                var lesson = selector.GetComponent<ElectricLearningController2D>();
                var vectors = selector.GetComponent<ElectricVectorDisplay2D>();
                if (lesson == null || vectors == null)
                    throw new InvalidOperationException("Learning controller or vector display is missing.");
                ElectricBall2D ball = selector.Ball;
                Rigidbody2D body = ball.Body;
                int phase = SessionState.GetInt(Prefix + "Phase", 0);
                float deadline = SessionState.GetFloat(Prefix + "Deadline", 0f);
                if (phase != 0 && Time.fixedTime < deadline) return;

                switch (phase)
                {
                    case 20:
                        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                        {
                            NoteScreenshot("Visual verification requires a graphics device.");
                            Finish();
                            break;
                        }
                        selector.SelectStage(3);
                        selector.Lever.SetValue(0f);
                        lesson.SetBallCharge(1f);
                        NextCapturePhase(10);
                        break;

                    case 0:
                        AddCheck("single-existing-player-stack", ball.GetComponents<Player>().Length == 1
                            && ball.GetComponents<PlayerMovement>().Length == 1
                            && ball.GetComponents<PlayerController>().Length == 1
                            && ball.GetComponents<Rigidbody2D>().Length == 1
                            && ball.GetComponents<ElectricBall2D>().Length == 1
                            && UnityEngine.Object.FindObjectsByType<ElectricBall2D>(FindObjectsSortMode.None).Length == 1,
                            "One existing player, controller, movement, body and electrical adapter.");
                        AddCheck("existing-lever-reader", selector.Lever.Reader != null
                            && selector.Lever.GetComponents<StageLever2D>().Length == 1,
                            "The configured adapter still reads the existing lever.");
                        AddCheck("single-learning-and-vector-components",
                            selector.GetComponents<ElectricLearningController2D>().Length == 1
                            && selector.GetComponents<ElectricVectorDisplay2D>().Length == 1,
                            "Runtime binding does not duplicate lesson or vector components.");
                        SessionState.SetInt(Prefix + "Constraints", (int)body.constraints);
                        SessionState.SetFloat(Prefix + "Gravity", body.gravityScale);
                        CheckLessonLifecycle(selector, lesson, body);
                        CheckKeyboardCommands(selector, lesson);
                        CheckOriginalPresentation(selector);
                        selector.SelectStage(4);
                        AddCheck("negative-puzzle-starts-with-positive-comparison", ball.CurrentPolarity == Polarity.Positive,
                            "Stage 4 starts with a positive comparison; the learner explicitly selects the negative challenge.");
                        selector.Lever.SetValue(1f);
                        lesson.SetBallCharge(1f);
                        Hold(body, Stage<ElectricGoal2D>(selector, 4).transform.position);
                        ball.CollectStar();
                        Next(1);
                        break;

                    case 1:
                        AddCheck("positive-ball-cannot-clear-negative-puzzle", !Stage<ElectricGoal2D>(selector, 4).IsCleared,
                            "Positive ball with a star overlaps the actual exit trigger.");
                        selector.RestartStage();
                        selector.Lever.SetValue(1f);
                        lesson.SetBallCharge(-1f);
                        Hold(body, Stage<ElectricGoal2D>(selector, 4).transform.position);
                        ball.CollectStar();
                        Next(2);
                        break;

                    case 2:
                        AddCheck("unobserved-negative-ball-cannot-clear", !Stage<ElectricGoal2D>(selector, 4).IsCleared
                            && !lesson.NegativeChallengeSatisfied,
                            "The negative ball has not experienced upward electric force inside the plates.");
                        Hold(body, Stage<UniformField2D>(selector, 4).GetComponent<BoxCollider2D>().bounds.center);
                        Next(3);
                        break;

                    case 3:
                        ElectricForceSample negative = FieldSample(selector);
                        AddCheck("negative-force-opposes-field", negative.IsInRange && negative.SignedCharge < 0f
                            && negative.Field.y > 0f && negative.Force.y < 0f
                            && Near(negative.Force, negative.Field * negative.SignedCharge),
                            Describe(negative));
                        vectors.RefreshDisplay();
                        AddCheck("negative-vectors-match-applied-sample", vectors.IsFieldVisible && vectors.IsForceVisible
                            && Near(vectors.DisplayedField, negative.Field) && Near(vectors.DisplayedForce, negative.Force),
                            "Both arrow directions agree with the force provider.");
                        lesson.SetBallCharge(1f);
                        ElectricForceSample positive = FieldSample(selector);
                        AddCheck("positive-force-follows-same-field", positive.SignedCharge > 0f
                            && Near(positive.Field, negative.Field) && Near(positive.Force, -negative.Force), Describe(positive));
                        lesson.SetBallCharge(0f);
                        ElectricForceSample neutral = FieldSample(selector);
                        vectors.RefreshDisplay();
                        AddCheck("neutral-keeps-field-with-zero-force", Near(neutral.SignedCharge, 0f)
                            && Near(neutral.Force, Vector2.zero) && neutral.Field.sqrMagnitude > 0f
                            && vectors.IsFieldVisible && !vectors.IsForceVisible, Describe(neutral));
                        lesson.SetBallCharge(-1f);
                        ball.CollectStar();
                        body.constraints = (RigidbodyConstraints2D)SessionState.GetInt(Prefix + "Constraints", 0);
                        body.position = Stage<UniformField2D>(selector, 4).GetComponent<BoxCollider2D>().bounds.center;
                        Physics2D.SyncTransforms();
                        selector.Lever.SetValue(0f);
                        Next(4);
                        break;

                    case 4:
                        ElectricForceSample upwardForce = FieldSample(selector);
                        AddCheck("reverse-field-lifts-negative-ball", upwardForce.Field.y < 0f && upwardForce.Force.y > 0f
                            && body.linearVelocity.y > 0f && lesson.NegativeChallengeSatisfied,
                            Describe(upwardForce) + "; measured velocity=" + body.linearVelocity);
                        Hold(body, Stage<ElectricGoal2D>(selector, 4).transform.position);
                        selector.Lever.SetValue(1f);
                        Next(5);
                        break;

                    case 5:
                        AddCheck("wrong-current-field-blocks-exit", !Stage<ElectricGoal2D>(selector, 4).IsCleared,
                            "An earlier successful observation does not authorize clearing with the wrong field direction.");
                        selector.Lever.SetValue(0f);
                        Next(6);
                        break;

                    case 6:
                        AddCheck("observed-negative-ball-clears-real-exit", Stage<ElectricGoal2D>(selector, 4).IsCleared,
                            "Negative ball, collected star and downward field clear through OnTriggerStay2D.");
                        selector.RestartStage();
                        AddCheck("restart-clears-negative-milestone", !lesson.NegativeChallengeSatisfied
                            && !Stage<ElectricGoal2D>(selector, 4).IsCleared && body.simulated,
                            "Restart revokes the field milestone, resets the goal and restores simulation.");
                        body.constraints = (RigidbodyConstraints2D)SessionState.GetInt(Prefix + "Constraints", 0);
                        body.gravityScale = 0f;
                        selector.SelectStage(3);
                        lesson.SetBallCharge(1f);
                        body.position = new Vector2(-3f, 2f);
                        body.linearVelocity = Vector2.zero;
                        body.angularVelocity = 0f;
                        var root = selector.StageRoots[1].transform;
                        SessionState.SetString(Prefix + "OriginalFar", JsonUtility.ToJson(root.Find("Far Position").position));
                        SessionState.SetString(Prefix + "OriginalNear", JsonUtility.ToJson(root.Find("Near Position").position));
                        root.Find("Far Position").position = new Vector3(-5f, 2f, 0f);
                        root.Find("Near Position").position = new Vector3(-4f, 2f, 0f);
                        Stage<DistanceLauncher2D>(selector, 3).SetSourceChargeMagnitude(1f);
                        selector.Lever.SetValue(1f);
                        Physics2D.SyncTransforms();
                        Next(7);
                        break;

                    case 7:
                        ElectricForceSample near = DistanceSample(selector);
                        AddCheck("controlled-distance-trial-is-stationary", lesson.IsPreparingTrial
                            && Near(near.Distance, 1f) && Near(body.linearVelocity, Vector2.zero)
                            && near.IsInRange && !near.IsLimited, Describe(near));
                        lesson.RecordBaseline();
                        AddCheck("baseline-is-not-a-launched-result", lesson.HasBaseline && !lesson.HasResult
                            && Near(lesson.BaselineSample.Distance, 1f), "Recording the baseline records a prediction only.");
                        vectors.RefreshDisplay();
                        AddCheck("launcher-arrow-marked-as-preview", vectors.IsForcePreview
                            && vectors.IsForceVisible && Near(vectors.DisplayedForce, near.Force),
                            "No continuous force is implied before the pulse launch.");
                        selector.Lever.SetValue(0f);
                        Next(8);
                        break;

                    case 8:
                        ElectricForceSample distant = DistanceSample(selector);
                        AddCheck("double-distance-quarter-force", Near(distant.Distance, 2f)
                            && Near(distant.ForceMagnitude / lesson.BaselineSample.ForceMagnitude, 0.25f)
                            && !distant.IsLimited, Describe(distant));
                        lesson.SetBallCharge(2f);
                        ElectricForceSample doubleCharge = DistanceSample(selector);
                        AddCheck("double-ball-charge-double-force", Near(ball.ChargeMagnitude, 2f)
                            && Near(doubleCharge.ForceMagnitude / distant.ForceMagnitude, 2f)
                            && Near(doubleCharge.Field, distant.Field), Describe(doubleCharge));
                        Stage<DistanceLauncher2D>(selector, 3).SetSourceChargeMagnitude(2f);
                        ElectricForceSample doubleSource = DistanceSample(selector);
                        AddCheck("double-source-charge-double-force", Near(doubleSource.ForceMagnitude / doubleCharge.ForceMagnitude, 2f)
                            && Near(doubleSource.SourceCharge, 2f), Describe(doubleSource));
                        Stage<DistanceLauncher2D>(selector, 3).SetSourceChargeMagnitude(1f);
                        AddCheck("parameter-edits-do-not-create-result", !lesson.HasResult,
                            "The result remains empty until an actual accepted launch.");
                        SessionState.SetString(Prefix + "PredictionExplanation", lesson.LastExplanation ?? "");
                        Stage<DistanceLauncher2D>(selector, 3).RequestLaunch();
                        Next(9);
                        break;

                    case 9:
                        var launcher = Stage<DistanceLauncher2D>(selector, 3);
                        AddCheck("actual-launch-releases-preparation", !lesson.IsPreparingTrial && body.linearVelocity.x > 0f,
                            "Velocity=" + body.linearVelocity + "; constraints=" + body.constraints);
                        AddCheck("actual-launch-records-fixed-snapshot", launcher.HasLaunchSample && lesson.HasResult
                            && lesson.HasBaseline && Near(lesson.ResultSample.Distance, 2f)
                            && Near(lesson.ResultSample.SignedCharge, 2f)
                            && Near(lesson.ResultSample.Force, launcher.LastLaunchSample.Force),
                            Describe(lesson.ResultSample));
                        AddCheck("launch-produces-result-explanation", !string.IsNullOrEmpty(lesson.LastExplanation)
                            && lesson.LastExplanation != SessionState.GetString(Prefix + "PredictionExplanation", ""),
                            lesson.LastExplanation);
                        float resultDistance = lesson.ResultSample.Distance;
                        lesson.PrepareTrial();
                        AddCheck("next-trial-preserves-comparison", lesson.IsPreparingTrial && lesson.HasBaseline && lesson.HasResult
                            && Near(lesson.ResultSample.Distance, resultDistance),
                            "A new preparation keeps the previous actual result and baseline.");
                        selector.RestartStage();
                        AddCheck("restart-clears-stale-comparisons", !lesson.HasBaseline && !lesson.HasResult
                            && !launcher.HasLaunchSample && lesson.IsPreparingTrial,
                            "Restart clears old baseline/result/launch records and prepares a fresh trial.");
                        selector.SelectStage(5);
                        vectors.RefreshDisplay();
                        AddCheck("other-stage-releases-trial-and-hides-vectors", !lesson.IsPreparingTrial
                            && !lesson.HasBaseline && !lesson.HasResult
                            && !vectors.IsFieldVisible && !vectors.IsForceVisible
                            && body.constraints == (RigidbodyConstraints2D)SessionState.GetInt(Prefix + "Constraints", 0),
                            "No stage 3 constraints, snapshots or arrows leak into stage 5.");
                        if (selector.gameObject.scene.path == ElectricBounceSceneInstaller.ScenePath)
                        {
                                                                        
                            Hold(body, selector.StageRoots[3].transform.Find("Launch Zone")
                                .GetComponent<BoxCollider2D>().bounds.center);
                            selector.Lever.SetValue(1f);
                            Next(26);
                            SessionState.SetFloat(Prefix + "Deadline", Time.fixedTime + 2.2f);
                            break;
                        }
                        goto case 27;

                    case 26:
                        CheckLiveVoltageGauge(selector);
                        selector.Lever.SetValue(0f);
                        body.constraints = (RigidbodyConstraints2D)SessionState.GetInt(Prefix + "Constraints", 0);
                        goto case 27;

                    case 27:
                        body.gravityScale = SessionState.GetFloat(Prefix + "Gravity", 1f);
                        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                        {
                            NoteScreenshot("Screenshots skipped because the batch editor has no graphics device.");
                            Finish();
                            break;
                        }
                                                                                                               
                        Transform distanceRoot = selector.StageRoots[1].transform;
                        distanceRoot.Find("Far Position").position = JsonUtility.FromJson<Vector3>(
                            SessionState.GetString(Prefix + "OriginalFar", "{}"));
                        distanceRoot.Find("Near Position").position = JsonUtility.FromJson<Vector3>(
                            SessionState.GetString(Prefix + "OriginalNear", "{}"));
                        selector.SelectStage(3);
                        selector.Lever.SetValue(0f);
                        lesson.SetBallCharge(1f);
                        NextCapturePhase(10);
                        break;

                    case 10:
                        if (!WaitForRenderedFrames()) break;
                        lesson.RecordBaseline();
                        lesson.SetBallCharge(2f);
                        NextCapturePhase(11);
                        break;

                    case 11:
                        if (!WaitForRenderedFrames()) break;
                        Capture("ElectricLearningDistance.png");
                        NextCapturePhase(12);
                        break;

                    case 12:
                        if (!WaitForCapture("ElectricLearningDistance.png", selector)) break;
                        selector.SelectStage(4);
                        lesson.SetBallCharge(-1f);
                        Hold(body, Stage<UniformField2D>(selector, 4).GetComponent<BoxCollider2D>().bounds.center);
                        selector.Lever.SetValue(0f);
                        NextCapturePhase(13);
                        break;

                    case 13:
                        if (!WaitForRenderedFrames()) break;
                        Capture("ElectricLearningNegative.png");
                        NextCapturePhase(14);
                        break;

                    case 14:
                        if (!WaitForCapture("ElectricLearningNegative.png", selector)) break;
                        selector.SelectStage(2);
                        selector.Lever.SetValue(0f);
                        Hold(body, selector.SpawnPoint.position);
                        NextCapturePhase(21);
                        break;

                    case 21:
                        if (!WaitForRenderedFrames()) break;
                        Capture("ElectricLearningContact.png");
                        NextCapturePhase(22);
                        break;

                    case 22:
                        if (!WaitForCapture("ElectricLearningContact.png", selector)) break;
                        selector.SelectStage(5);
                        Hold(body, selector.StageRoots[3].transform.Find("Launch Zone").GetComponent<BoxCollider2D>().bounds.center);
                        selector.Lever.SetValue(1f);
                        SessionState.SetFloat(Prefix + "VoltageCaptureTime", Time.fixedTime + 2.2f);
                        NextCapturePhase(23);
                        break;

                    case 23:
                        if (Time.fixedTime < SessionState.GetFloat(Prefix + "VoltageCaptureTime", 0f)) break;
                        selector.Lever.SetValue(0f);
                        NextCapturePhase(24);
                        break;

                    case 24:
                        if (!WaitForRenderedFrames()) break;
                        if (SessionState.GetBool(Prefix + "VisualOnly", false)) CheckLiveVoltageGauge(selector);
                        Capture("ElectricLearningVoltage.png");
                        NextCapturePhase(25);
                        break;

                    case 25:
                        if (!WaitForCapture("ElectricLearningVoltage.png", selector)) break;
                        Finish();
                        break;
                }
            }
            catch (Exception exception)
            {
                Report report = Load();
                report.errors.Add(exception.ToString());
                Save(report);
                Finish();
            }
        }

                                                                          
        private static void CheckLessonLifecycle(ElectricStageSelector selector,
            ElectricLearningController2D lesson, Rigidbody2D body)
        {
            RigidbodyConstraints2D originalConstraints = body.constraints;
            selector.SelectStage(3);
            selector.EnsureLearningComponents();
            selector.EnsureLearningComponents();
            AddCheck("rebind-keeps-single-input-and-prepared-trial",
                selector.GetComponents<ElectricLearningInput2D>().Length == 1
                && lesson.IsPreparingTrial && body.constraints == RigidbodyConstraints2D.FreezeAll,
                "Repeated connection preserves the current experiment and one nonvisual input bridge.");

            lesson.enabled = false;
            AddCheck("disabling-lesson-releases-player",
                !lesson.IsPreparingTrial && body.constraints == originalConstraints,
                "The previous Rigidbody constraints are restored when the lesson is disabled.");
            lesson.enabled = true;
        }

                     
                                                    
                                                         
                      
        private static void CheckKeyboardCommands(ElectricStageSelector selector,
            ElectricLearningController2D lesson)
        {
            ElectricLearningInput2D input = selector.GetComponent<ElectricLearningInput2D>();
            if (input == null) throw new InvalidOperationException("Nonvisual learning input is missing.");
            Keyboard previousKeyboard = Keyboard.current;
            InputSettings.UpdateMode previousUpdateMode = InputSystem.settings.updateMode;
            InputSettings.EditorInputBehaviorInPlayMode previousEditorBehavior =
                InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSettings.BackgroundBehavior previousBackgroundBehavior = InputSystem.settings.backgroundBehavior;
            bool previouslyRunInBackground = Application.runInBackground;
            Keyboard keyboard = null;
            try
            {
                                                                 
                InputSystem.settings.editorInputBehaviorInPlayMode =
                    InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
                Application.runInBackground = true;
                keyboard = InputSystem.AddDevice<Keyboard>();
                keyboard.MakeCurrent();

                CheckPlayerStateKeyboard(selector, lesson, keyboard);
                CheckPlayerChargeDisplay(selector, keyboard);

                selector.SelectStage(3);
                selector.Lever.SetValue(0f);
                DistanceLauncher2D launcher = Stage<DistanceLauncher2D>(selector, 3);
                launcher.SendMessage("FixedUpdate", SendMessageOptions.RequireReceiver);
                PressKey(keyboard, input, Key.B);
                AddCheck("keyboard-b-records-baseline", lesson.IsPreparingTrial && lesson.HasBaseline
                    && !lesson.HasResult, "B saves the fixed-position baseline without generating a launch result.");

                PressKey(keyboard, input, Key.Z);
                bool halfCharge = Near(selector.Ball.SignedCharge, 0.5f);
                PressKey(keyboard, input, Key.X);
                bool unitCharge = Near(selector.Ball.SignedCharge, 1f);
                PressKey(keyboard, input, Key.C);
                AddCheck("keyboard-z-x-c-select-comparison-charge", halfCharge && unitCharge
                    && Near(selector.Ball.SignedCharge, 2f), "Stage 3 keys select q = +0.5, +1 and +2.");
                PressKey(keyboard, input, Key.N);
                bool doubleSource = Near(launcher.SourceChargeMagnitude, 2f);
                PressKey(keyboard, input, Key.V);
                AddCheck("keyboard-v-n-select-source-charge", doubleSource
                    && Near(launcher.SourceChargeMagnitude, 1f), "V/N independently select Q = 1/2.");

                lesson.RequestLaunch();
                launcher.SendMessage("FixedUpdate", SendMessageOptions.RequireReceiver);
                PressKey(keyboard, input, Key.X);
                PressKey(keyboard, input, Key.N);
                AddCheck("keyboard-charge-edits-blocked-in-flight", !lesson.IsPreparingTrial
                    && lesson.HasResult && Near(selector.Ball.SignedCharge, 2f)
                    && Near(launcher.SourceChargeMagnitude, 1f),
                    "Charge keys do not change the conditions after releasing the prepared trial.");
                ElectricForceSample result = lesson.ResultSample;
                PressKey(keyboard, input, Key.T);
                AddCheck("keyboard-t-prepares-and-preserves-records", lesson.IsPreparingTrial
                    && lesson.HasBaseline && lesson.HasResult
                    && Near(lesson.ResultSample.Force, result.Force)
                    && Near(selector.Ball.Body.position, selector.SpawnPoint.position),
                    "T returns to the fixed origin while retaining the baseline and actual launch record.");

                lesson.enabled = false;
                PressKey(keyboard, input, Key.X);
                PressKey(keyboard, input, Key.T);
                AddCheck("disabled-lesson-ignores-learning-keys", !lesson.IsPreparingTrial
                    && Near(selector.Ball.SignedCharge, 2f),
                    "The input bridge neither changes charge nor freezes the player when its lesson is disabled.");
                lesson.enabled = true;

                selector.SelectStage(4);
                PressKey(keyboard, input, Key.X);
                bool negative = selector.Ball.CurrentPolarity == Polarity.Negative;
                PressKey(keyboard, input, Key.C);
                bool neutral = selector.Ball.CurrentPolarity == Polarity.Neutral;
                PressKey(keyboard, input, Key.Z);
                AddCheck("keyboard-z-x-c-select-field-polarity", negative && neutral
                    && selector.Ball.CurrentPolarity == Polarity.Positive,
                    "Stage 4 uses Z/X/C for positive, negative and neutral without a generated HUD.");

                selector.SelectStage(2);
                PressKey(keyboard, input, Key.C);
                PressKey(keyboard, input, Key.B);
                AddCheck("keyboard-learning-keys-do-not-bypass-contact", !lesson.HasBaseline
                    && !lesson.ContactChallengeSatisfied && selector.Ball.CurrentPolarity == Polarity.Neutral,
                    "Learning shortcuts cannot replace Stage 2's required physical contact.");
            }
            finally
            {
                lesson.enabled = true;
                if (keyboard != null) InputSystem.RemoveDevice(keyboard);
                if (previousKeyboard != null && previousKeyboard.added) previousKeyboard.MakeCurrent();
                InputSystem.settings.updateMode = previousUpdateMode;
                InputSystem.settings.editorInputBehaviorInPlayMode = previousEditorBehavior;
                InputSystem.settings.backgroundBehavior = previousBackgroundBehavior;
                Application.runInBackground = previouslyRunInBackground;
            }
        }

                                                                           
        private static void CheckPlayerStateKeyboard(ElectricStageSelector selector,
            ElectricLearningController2D lesson, Keyboard keyboard)
        {
            selector.SelectStage(2);
            Player player = selector.Ball.Player;
            PlayerController controller = player.GetComponent<PlayerController>();
            PlayerInputReader reader = player.GetComponent<PlayerInputReader>();
            ChangeVisual visual = player.GetComponentInChildren<ChangeVisual>(true);
            Rigidbody2D body = selector.Ball.Body;
            Vector2 position = body.position;
            Vector2 velocity = body.linearVelocity;
            int notifications = 0;
            Action<PlayerStateType> onChanged = state => notifications++;
            player.StateChanged += onChanged;
            try
            {
                PressKey(keyboard, controller, Key.Space);
                bool positive = player.CurrentState == PlayerStateType.Positive
                    && visual.CurrentVisualState == PlayerStateType.Positive
                    && Mathf.Approximately(selector.Ball.SignedCharge, 1f);
                PressKey(keyboard, controller, Key.Space);
                bool negative = player.CurrentState == PlayerStateType.Negative
                    && visual.CurrentVisualState == PlayerStateType.Negative
                    && Mathf.Approximately(selector.Ball.SignedCharge, -1f);
                PressKey(keyboard, controller, Key.Space);
                AddCheck("space-cycles-existing-state-and-visual-once", positive && negative
                    && player.CurrentState == PlayerStateType.Neutral
                    && visual.CurrentVisualState == PlayerStateType.Neutral
                    && Mathf.Approximately(selector.Ball.SignedCharge, 0f) && notifications == 3,
                    "Three Space presses cycle physical charge +1 / -1 / 0, with one notification per press.");
                AddCheck("space-updates-charge-without-forging-contact", Near(body.position, position)
                    && Near(body.linearVelocity, velocity) && Mathf.Approximately(selector.Ball.ChargeMagnitude, 0f)
                    && !lesson.ContactChallengeSatisfied && !lesson.CanClearGoal(selector.Ball),
                    "Space selects real charge while preserving current motion and does not count as electrode contact.");

                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Space));
                UpdateGameInput();
                                                                       
                controller.SendMessage("Update", SendMessageOptions.RequireReceiver);
                int notificationsAfterPress = notifications;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Space));
                UpdateGameInput();
                controller.SendMessage("Update", SendMessageOptions.RequireReceiver);
                AddCheck("space-hold-does-not-repeat-or-double-handle", notificationsAfterPress == 4
                    && notifications == 4 && player.CurrentState == PlayerStateType.Positive,
                    "A held Space key does not repeat; Player.Update does not process the same key again. "
                    + $"Notifications after press={notificationsAfterPress}, after hold={notifications}, state={player.CurrentState}.");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                UpdateGameInput();

                reader.enabled = false;
                PressKey(keyboard, controller, Key.Space);
                AddCheck("disabled-player-input-ignores-space", notifications == 4
                    && player.CurrentState == PlayerStateType.Positive,
                    $"Disabled input: notifications={notifications}, state={player.CurrentState}.");
            }
            finally
            {
                reader.enabled = true;
                player.StateChanged -= onChanged;
                selector.RestartStage();
            }
        }

                                                                                     
        private static void CheckPlayerChargeDisplay(ElectricStageSelector selector, Keyboard keyboard)
        {
            string[] expected = { "현재 전하 0 중성", "현재 전하 + 양전하", "현재 전하 − 음전하" };
            for (int stageNumber = 2; stageNumber <= 5; stageNumber++)
            {
                selector.SelectStage(stageNumber);
                ElectricLearningStatusView view = selector.StageRoots[stageNumber - 2]
                    .GetComponentInChildren<ElectricLearningStatusView>(true);
                if (view == null) throw new InvalidOperationException("Saved charge UI missing in stage " + stageNumber);
                Text label = new SerializedObject(view).FindProperty("chargeText").objectReferenceValue as Text;
                Text hint = view.transform.Find("Common Controls")?.GetComponent<Text>();
                bool passed = label != null && hint != null && hint.text.Contains("Space")
                    && hint.text.Contains("0 → + → −") && label.gameObject.activeInHierarchy;
                                                      
                selector.Ball.SetChargeMagnitude(0f);
                selector.Ball.Player.ChangeState(PlayerStateType.Neutral);
                for (int state = 0; state < expected.Length; state++)
                {
                    view.RefreshDisplay();
                    passed &= label != null && label.text.StartsWith(expected[state], StringComparison.Ordinal)
                        && label.preferredWidth <= label.rectTransform.rect.width
                        && label.preferredHeight <= label.rectTransform.rect.height;
                    PressKey(keyboard, selector.Ball.Player.GetComponent<PlayerController>(), Key.Space);
                }
                passed &= hint != null && hint.preferredWidth <= hint.rectTransform.rect.width
                    && hint.preferredHeight <= hint.rectTransform.rect.height;
                AddCheck("saved-charge-ui-and-space-hint-stage-" + stageNumber, passed,
                    "Saved Text follows neutral / positive / negative Space transitions even at q=0; both text rows fit their authored bounds.");
            }
            selector.RestartStage();
        }

        private static void PressKey(Keyboard keyboard, MonoBehaviour input, Key key)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            UpdateGameInput();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
            UpdateGameInput();
                                                               
            bool isCurrent = Keyboard.current == keyboard;
            bool isPressed = keyboard[key].isPressed;
            bool pressedThisFrame = keyboard[key].wasPressedThisFrame;
            if (!isCurrent || !isPressed || !pressedThisFrame)
                throw new InvalidOperationException("Virtual keyboard dispatch failed: key=" + key
                    + "; current=" + isCurrent + "; pressed=" + isPressed
                    + "; wasPressedThisFrame=" + pressedThisFrame + "; enabled=" + keyboard.enabled
                    + "; update=" + InputState.currentUpdateType);
            input.SendMessage("Update", SendMessageOptions.RequireReceiver);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            UpdateGameInput();
        }

                     
                                                          
                                                               
                                                                      
                      
        private static void UpdateGameInput()
        {
            MethodInfo update = typeof(InputSystem).GetMethod("Update", BindingFlags.Static | BindingFlags.NonPublic,
                null, new[] { typeof(InputUpdateType) }, null);
            if (update == null)
                throw new InvalidOperationException("Installed Input System has no typed update method for this batch fixture.");
            update.Invoke(null, new object[] { InputUpdateType.Dynamic });
        }

                     
                                                         
                                                      
                      
        private static void CheckSavedOriginalArt(string scenePath)
        {
            if (scenePath != ElectricBounceSceneInstaller.ScenePath) return;
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var expectedGroups = new HashSet<string>
            {
                "Scene Art - Shared Environment", "Scene Art - Stage 02", "Scene Art - Stage 03",
                "Scene Art - Stage 04", "Scene Art - Stage 05"
            };
            int missingScripts = 0;
            bool editableGroups = true;
            VoltageLauncher2D voltage = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (voltage == null) voltage = root.GetComponentInChildren<VoltageLauncher2D>(true);
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                {
                    missingScripts += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject);
                    if (!expectedGroups.Remove(child.name)) continue;
                    editableGroups &= child.gameObject.hideFlags == HideFlags.None
                        && child.GetComponentsInChildren<SpriteRenderer>(true).Length > 0;
                }
            }
            Slider gauge = GetVoltageGauge(voltage);
            Canvas canvas = gauge != null ? gauge.GetComponentInParent<Canvas>(true) : null;
            bool nativeGauge = gauge != null && gauge.gameObject.scene == scene
                && canvas != null && canvas.renderMode == RenderMode.WorldSpace
                && gauge.direction == Slider.Direction.BottomToTop && !gauge.interactable
                && gauge.fillRect != null && gauge.fillRect.GetComponent<Image>() != null;
            AddCheck("original-art-and-gauge-exist-before-play", !Application.isPlaying
                && expectedGroups.Count == 0 && editableGroups && missingScripts == 0 && nativeGauge,
                "Saved scene art groups missing=" + expectedGroups.Count + "; missing scripts=" + missingScripts
                + "; editable groups=" + editableGroups + "; linked native world-space gauge=" + nativeGauge);
        }

                                                                     
        private static void CheckLiveVoltageGauge(ElectricStageSelector selector)
        {
            if (selector.gameObject.scene.path != ElectricBounceSceneInstaller.ScenePath) return;
            VoltageLauncher2D voltage = Stage<VoltageLauncher2D>(selector, 5);
            Slider gauge = GetVoltageGauge(voltage);
            bool followsVoltage = gauge != null && !gauge.interactable
                && gauge.direction == Slider.Direction.BottomToTop
                && voltage.Voltage >= 50f && voltage.NormalizedVoltage < 1f
                && Mathf.Abs(gauge.value - voltage.NormalizedVoltage) < 0.001f
                && gauge.fillRect != null && Mathf.Abs(gauge.fillRect.anchorMax.y - gauge.value) < 0.001f;
            AddCheck("saved-voltage-gauge-follows-live-charge", followsVoltage,
                "Voltage=" + voltage.Voltage + "; normalized=" + voltage.NormalizedVoltage
                + "; slider=" + (gauge != null ? gauge.value.ToString(CultureInfo.InvariantCulture) : "missing"));
        }

        private static Slider GetVoltageGauge(VoltageLauncher2D voltage)
        {
            if (voltage == null) return null;
            return new SerializedObject(voltage).FindProperty("voltageGauge").objectReferenceValue as Slider;
        }

                                                                            
        private static void CheckOriginalPresentation(ElectricStageSelector selector)
        {
            bool noGeneratedDecorations = true;
            bool noDesignBehaviours = true;
            bool backgroundVisible = false;
            bool previousCourseDisabled = false;
            bool startFloorPresent = false;
            foreach (GameObject root in selector.gameObject.scene.GetRootGameObjects())
            {
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                {
                    noGeneratedDecorations &= !child.name.StartsWith("Lab / ", StringComparison.Ordinal);
                    if (child.name == "Platform - Previous Shared Course (Disabled)")
                        previousCourseDisabled = !child.gameObject.activeSelf;
                    if (child.name == "Start Floor")
                    {
                        var collider = child.GetComponent<BoxCollider2D>();
                        startFloorPresent = collider != null && collider.enabled && !collider.isTrigger
                            && child.gameObject.activeInHierarchy && child.CompareTag("Ground");
                    }
                }
                foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (behaviour == null) continue;
                    string type = behaviour.GetType().Name;
                    noDesignBehaviours &= type != "ElectricStagePresentation2D" && type != "ElectricLearningHud";
                }
                foreach (TilemapRenderer renderer in root.GetComponentsInChildren<TilemapRenderer>(true))
                {
                    if (renderer.name == "Background")
                        backgroundVisible |= renderer.enabled && renderer.gameObject.activeInHierarchy;
                }
            }
            AddCheck("generated-design-and-hud-are-absent", noGeneratedDecorations && noDesignBehaviours,
                "The scene contains no runtime lab decorations or removed design/HUD components.");
            if (selector.gameObject.scene.path == ElectricBounceSceneInstaller.ScenePath)
                AddCheck("authored-course-replaces-shared-obstacles",
                    backgroundVisible && previousCourseDisabled && startFloorPresent,
                    "Background=" + backgroundVisible + "; old shared collisions disabled="
                    + previousCourseDisabled + "; saved Ground start floor=" + startFloorPresent);
        }

        private static T Stage<T>(ElectricStageSelector selector, int stageNumber) where T : Component
        {
            T result = selector.StageRoots[stageNumber - 2].GetComponentInChildren<T>(true);
            if (result == null) throw new InvalidOperationException(typeof(T).Name + " missing from stage " + stageNumber);
            return result;
        }

        private static ElectricForceSample FieldSample(ElectricStageSelector selector)
        {
            if (!Stage<UniformField2D>(selector, 4).TryGetSample(out ElectricForceSample sample))
                throw new InvalidOperationException("Uniform field sampling failed.");
            return sample;
        }

        private static ElectricForceSample DistanceSample(ElectricStageSelector selector)
        {
            if (!Stage<DistanceLauncher2D>(selector, 3).TryGetSample(out ElectricForceSample sample))
                throw new InvalidOperationException("Distance launcher sampling failed.");
            return sample;
        }

        private static void Hold(Rigidbody2D body, Vector2 position)
        {
                                                                                   
            body.simulated = true;
            body.gravityScale = 0f;
            body.constraints = RigidbodyConstraints2D.FreezeAll;
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
            body.position = position;
            Physics2D.SyncTransforms();
        }

        private static bool Near(float a, float b) => Mathf.Abs(a - b) < 0.02f;
        private static bool Near(Vector2 a, Vector2 b) => (a - b).sqrMagnitude < 0.0004f;

        private static string Describe(ElectricForceSample sample) => "q=" + sample.SignedCharge
            + "; Q=" + sample.SourceCharge + "; r=" + sample.Distance + "; E=" + sample.Field
            + "; F=" + sample.Force + "; limited=" + sample.IsLimited + "; inRange=" + sample.IsInRange;

        private static void Next(int phase)
        {
            SessionState.SetInt(Prefix + "Phase", phase);
            SessionState.SetFloat(Prefix + "Deadline", Time.fixedTime + 0.08f);
        }

        private static void AddCheck(string name, bool passed, string details)
        {
            Report report = Load();
            report.checks.Add(new Check { name = name, passed = passed, details = details });
            Save(report);
        }

        private static void NextCapturePhase(int phase)
        {
            SessionState.SetInt(Prefix + "Phase", phase);
            SessionState.SetFloat(Prefix + "Deadline", 0f);
            SessionState.SetInt(Prefix + "CaptureRenderFrame", Time.renderedFrameCount);
            SessionState.SetString(Prefix + "CapturePhaseStarted",
                EditorApplication.timeSinceStartup.ToString(CultureInfo.InvariantCulture));
        }

        private static bool WaitForRenderedFrames()
        {
            if (Time.renderedFrameCount >= SessionState.GetInt(Prefix + "CaptureRenderFrame", 0) + 3)
                return true;
            if (EditorApplication.timeSinceStartup - Timestamp("CapturePhaseStarted") > 5d)
            {
                NoteScreenshot("Visual capture skipped: the graphics-enabled batch did not render three new frames within five seconds.");
                Finish();
            }
            return false;
        }

        private static string ScreenshotPath(string name)
        {
            string folder = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Logs");
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, name);
        }

        private static void Capture(string name)
        {
            if (name == "ElectricLearningContact.png")
            {
                AddCheck("game-view-uses-16-by-9", Screen.width * 9 == Screen.height * 16,
                    "Actual Play render size: " + Screen.width + " x " + Screen.height);
            }
                                                                                              
            SessionState.SetString(Prefix + "CaptureUtc", DateTime.UtcNow.AddSeconds(-1d).Ticks.ToString(CultureInfo.InvariantCulture));
            ScreenCapture.CaptureScreenshot(ScreenshotPath(name));
        }

        private static bool WaitForCapture(string name, ElectricStageSelector selector)
        {
            if (!WaitForRenderedFrames()) return false;
            string path = ScreenshotPath(name);
            long.TryParse(SessionState.GetString(Prefix + "CaptureUtc", "0"), out long capturedAt);
            if (File.Exists(path) && new FileInfo(path).Length > 0
                && File.GetLastWriteTimeUtc(path).Ticks >= capturedAt)
            {
                Report report = Load();
                report.screenshotPaths.Add(path);
                Save(report);
                return true;
            }
            if (SessionState.GetBool(Prefix + "VisualOnly", false)
                && EditorApplication.timeSinceStartup - Timestamp("CapturePhaseStarted") > 2d
                && ElectricLearningVisualCheck.CaptureWorld(selector, path, 1600, 900))
            {
                Report report = Load();
                report.screenshotPaths.Add(path);
                report.screenshotNotes.Add("Camera fallback used for " + name
                    + ": world and sprite arrows only; OnGUI arrow text labels are excluded.");
                Save(report);
                return true;
            }
            if (EditorApplication.timeSinceStartup - Timestamp("CapturePhaseStarted") > 5d)
            {
                NoteScreenshot("Screenshot was requested but no fresh image file appeared: " + path);
                Finish();
            }
            return false;
        }

        private static void NoteScreenshot(string message)
        {
            Report report = Load();
            report.screenshotNotes.Add(message);
            Save(report);
        }

        private static Report Load()
        {
            string json = SessionState.GetString(Prefix + "Report", "");
            return string.IsNullOrEmpty(json) ? new Report() : JsonUtility.FromJson<Report>(json);
        }

        private static void Save(Report report) => SessionState.SetString(Prefix + "Report", JsonUtility.ToJson(report));

        private static void Finish()
        {
            if (!Application.isBatchMode) return;
            Report report = Load();
            bool visualOnly = SessionState.GetBool(Prefix + "VisualOnly", false);
            bool gameplayPassed = report.errors.Count == 0 && (visualOnly || report.checks.Count >= 25);
            foreach (Check check in report.checks) gameplayPassed &= check.passed;
            bool environmentPassed = report.environmentalErrors.Count == 0;
            report.gameplayStatus = visualOnly ? "not-run" : gameplayPassed ? "passed" : "failed";
            report.environmentStatus = environmentPassed ? "passed" : "failed";
            report.visualStatus = report.screenshotPaths.Count >= 4 ? "captured" : "unavailable";
            report.status = gameplayPassed && environmentPassed && (!visualOnly || report.screenshotPaths.Count >= 4)
                ? "passed" : "failed";
            double playStarted = Timestamp("PlayStarted");
            report.activeSeconds = playStarted > 0d ? (float)(EditorApplication.timeSinceStartup - playStarted) : 0f;
            string folder = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Logs");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, visualOnly ? "ElectricLearningVisualReport.json"
                : "ElectricLearningPlayModeReport.json"), JsonUtility.ToJson(report, true));
            SessionState.SetBool(Prefix + "Active", false);
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= CaptureLog;
            EditorApplication.Exit(report.status == "passed" ? 0 : 1);
        }
    }
}
