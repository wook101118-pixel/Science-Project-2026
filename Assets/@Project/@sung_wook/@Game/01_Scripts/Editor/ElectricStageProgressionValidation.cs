using System;
using System.Collections.Generic;
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
using Object = UnityEngine.Object;

namespace sung_wook
{
                                                                                     
    [InitializeOnLoad]
    public static class ElectricStageProgressionValidation
    {
        private const string Prefix = "sung_wook.ProgressionValidation.";
        private const string SceneFolder = "Assets/@Project/@sung_wook/@Game/00_Scenes/Stage/";
        private static EditorWindow gameView;
        private static Keyboard probeKeyboard, previousKeyboard;
        private static InputSettings.EditorInputBehaviorInPlayMode previousEditorInput;
        private static InputSettings.BackgroundBehavior previousBackground;
        private static InputSettings.UpdateMode previousUpdateMode;
        private static bool previousRunInBackground;

        private enum Phase
        {
            Startup, NeitherAtExit, CollectStarOnly, StarOnlyAtExit, MissingLessonAtExit,
            LessonPreparing, LessonResult, LessonOnlyAtExit, CollectQualifiedStar, QualifiedAtExit,
            AwaitNextScene, CompletionVisible, CompletionKeyboard, CompletionCapture, AwaitRestart
        }

        [Serializable]
        private sealed class Check
        {
            public int stage;
            public string name;
            public bool passed;
            public string details;
        }

        [Serializable]
        private sealed class Report
        {
            public string status = "running";
            public string mode = "Actual saved scenes, natural FixedUpdate trigger callbacks and lesson experiments; no scenes or assets saved.";
            public List<Check> checks = new List<Check>();
            public List<string> errors = new List<string>();
            public List<string> sceneLoads = new List<string>();
            public List<string> screenshots = new List<string>();
            public List<string> screenshotNotes = new List<string>();
        }

        private sealed class CanvasState
        {
            public Canvas canvas;
            public Camera camera;
            public float distance;
        }

        static ElectricStageProgressionValidation()
        {
            if (Application.isBatchMode && SessionState.GetBool(Prefix + "Active", false)) Resume();
        }

        public static void RunBatch()
        {
            if (!Application.isBatchMode)
            {
                Debug.LogWarning("Progression validation requires an explicitly launched isolated batch editor.");
                return;
            }
            Save(new Report());
            try
            {
                CheckBuildSettings();
                EditorSceneManager.OpenScene(ScenePath(2), OpenSceneMode.Single);
                SessionState.SetBool(Prefix + "Active", true);
                SessionState.SetBool(Prefix + "TrackingLoads", false);
                SessionState.SetInt(Prefix + "Stage", 2);
                SessionState.SetInt(Prefix + "PreviousPlayer", 0);
                SessionState.SetInt(Prefix + "Phase", (int)Phase.Startup);
                SessionState.SetFloat(Prefix + "Deadline", 0f);
                SessionState.SetString(Prefix + "StartupScene", "");
                SessionState.SetString(Prefix + "Started", DateTime.UtcNow.ToString("O"));
                Resume();
                ElectricBatchPlayMode.EnterAfterEditorStartup();
            }
            catch (Exception exception)
            {
                RecordError(exception.ToString());
                Finish();
            }
        }

        private static string ScenePath(int stage) => SceneFolder + "Stage_" + stage.ToString("00") + ".unity";
        private static string ReportPath => Path.Combine(Path.GetDirectoryName(Application.dataPath), "Logs/ElectricStageProgressionReport.json");
        private static string ScreenshotPath => Path.Combine(Path.GetDirectoryName(Application.dataPath), "Logs/ElectricGameCompleteScreen.png");

        private static void CheckBuildSettings()
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            var enabled = new List<string>();
            foreach (EditorBuildSettingsScene scene in scenes) if (scene.enabled) enabled.Add(scene.path);
            bool ordered = true;
            int previous = -1;
            for (int stage = 2; stage <= 5; stage++)
            {
                int index = enabled.IndexOf(ScenePath(stage));
                ordered &= index >= 0 && index > previous;
                if (stage > 2) ordered &= index == previous + 1;
                previous = index;
            }
            Add(0, "build-settings-enable-ordered-stages-2-through-5", ordered, string.Join("; ", enabled));
            Add(0, "existing-apple-wine-build-scene-is-preserved", enabled.Contains(
                "Assets/@Project/@apple_wine/@Game/Scenes/apple_wine.unity"), "Original enabled scene remains registered.");
        }

        private static void Resume()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            Type type = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            Require(type != null, "GameView is unavailable for completion-screen capture.");
            gameView = EditorWindow.GetWindow(type);
            const BindingFlags instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            const BindingFlags shared = BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy;
            Type sizesType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameViewSizes");
            object sizes = sizesType.GetProperty("instance", shared).GetValue(null);
            object group = sizesType.GetProperty("currentGroup", instance).GetValue(sizes);
            string[] labels = (string[])group.GetType().GetMethod("GetDisplayTexts", instance).Invoke(group, null);
            bool aspectSet = false;
            for (int index = 0; index < labels.Length; index++)
            {
                if (!labels[index].StartsWith("16:9", StringComparison.Ordinal)) continue;
                type.GetProperty("selectedSizeIndex", instance).SetValue(gameView, index);
                aspectSet = true;
                break;
            }
            Require(aspectSet, "The verification editor has no 16:9 GameView preset.");
            gameView.Repaint();
        }

        private static void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) RecordError(message + "\n" + stack);
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!SessionState.GetBool(Prefix + "TrackingLoads", false)) return;
            Report report = Load();
            report.sceneLoads.Add(scene.path);
            Save(report);
        }

        private static void Tick()
        {
            if (!Application.isBatchMode || !SessionState.GetBool(Prefix + "Active", false)) return;
            try
            {
                if (gameView != null) gameView.Repaint();
                if ((DateTime.UtcNow - DateTime.Parse(SessionState.GetString(Prefix + "Started", ""))).TotalSeconds > 240)
                    throw new TimeoutException("Progression verification exceeded 240 seconds.");
                if (!EditorApplication.isPlaying || EditorApplication.isPaused) return;
                Phase phase = (Phase)SessionState.GetInt(Prefix + "Phase", 0);
                int stage = SessionState.GetInt(Prefix + "Stage", 2);
                if (phase == Phase.AwaitNextScene || phase == Phase.AwaitRestart)
                {
                    AwaitScene(stage, phase == Phase.AwaitRestart);
                    return;
                }
                if (SceneManager.GetActiveScene().path != ScenePath(stage))
                    throw new InvalidOperationException("Unexpected scene during gate probe: " + SceneManager.GetActiveScene().path);
                var selector = Object.FindFirstObjectByType<ElectricStageSelector>();
                if (selector == null || selector.GetComponent<ElectricStageSession2D>()?.IsInitialized != true) return;
                if (phase == Phase.Startup)
                {
                    if (SessionState.GetString(Prefix + "StartupScene", "") != ScenePath(stage))
                    {
                        SessionState.SetString(Prefix + "StartupScene", ScenePath(stage));
                        SessionState.SetInt(Prefix + "StartupFrame", Time.frameCount);
                        return;
                    }
                    if (Time.frameCount <= SessionState.GetInt(Prefix + "StartupFrame", 0) + 1) return;
                }
                if (Time.unscaledTime < SessionState.GetFloat(Prefix + "Deadline", 0f)) return;
                ElectricLearningController2D lesson = selector.GetComponent<ElectricLearningController2D>();
                Component progression = selector.GetComponent("ElectricStageProgression");
                Require(lesson != null && progression != null, "GameManager lacks lesson or ElectricStageProgression.");
                ElectricGoal2D goal = selector.GetStageComponent<ElectricGoal2D>(stage);
                Require(goal != null, "Saved stage has no exit trigger.");
                switch (phase)
                {
                    case Phase.Startup:
                        Time.timeScale = 1f;
                        if (!SessionState.GetBool(Prefix + "TrackingLoads", false)) SessionState.SetBool(Prefix + "TrackingLoads", true);
                        Add(stage, "scene-initializes-new-player-and-clean-progress", selector.SelectedStage == stage
                            && Object.FindObjectsByType<ElectricBall2D>(FindObjectsSortMode.None).Length == 1
                            && selector.Ball.GetInstanceID() != SessionState.GetInt(Prefix + "PreviousPlayer", 0)
                            && selector.Ball.Stars == 0 && !lesson.CurrentLearningGoalSatisfied
                            && !Flag(progression, "IsTransitioning") && !Flag(progression, "IsGameComplete"),
                            "New scene player=" + selector.Ball.GetInstanceID() + ", stars=" + selector.Ball.Stars + ".");
                        SessionState.SetInt(Prefix + "ClearCount." + stage, 0);
                        int stageForListener = stage;
                        goal.OnCleared.AddListener(() => SessionState.SetInt(Prefix + "ClearCount." + stageForListener,
                            SessionState.GetInt(Prefix + "ClearCount." + stageForListener, 0) + 1));
                        ResetProbe(selector);
                        HoldAtGoal(selector);
                        Next(Phase.NeitherAtExit);
                        break;
                    case Phase.NeitherAtExit:
                        Add(stage, "exit-rejects-neither-star-nor-lesson", Closed(selector, progression)
                            && selector.Ball.Stars == 0 && !lesson.CurrentLearningGoalSatisfied, State(selector, progression));
                        ResetProbe(selector);
                        HoldAtStar(selector);
                        Next(Phase.CollectStarOnly);
                        break;
                    case Phase.CollectStarOnly:
                        Add(stage, "star-only-probe-uses-real-collection-trigger", selector.Ball.Stars >= 1
                            && !lesson.CurrentLearningGoalSatisfied, State(selector, progression));
                        HoldAtGoal(selector);
                        Next(Phase.StarOnlyAtExit);
                        break;
                    case Phase.StarOnlyAtExit:
                        Add(stage, "exit-rejects-stars-without-lesson", Closed(selector, progression)
                            && selector.Ball.Stars >= 1 && !lesson.CurrentLearningGoalSatisfied, State(selector, progression));
                        goal.ConfigureLearningRequirement(null);
                        Next(Phase.MissingLessonAtExit);
                        break;
                    case Phase.MissingLessonAtExit:
                        Add(stage, "exit-fails-closed-with-missing-learning-binding", Closed(selector, progression)
                            && selector.Ball.Stars >= 1 && Reference<ElectricLearningController2D>(goal, "learningRequirement") == null,
                            "Saved star was collected; learning binding temporarily absent during real OnTriggerStay. " + State(selector, progression));
                        goal.ConfigureLearningRequirement(lesson);
                        ResetProbe(selector);
                        PrepareLesson(selector, lesson);
                        Next(Phase.LessonPreparing, stage == 2 ? 2.4f : stage == 5 ? 2.3f : 0.12f);
                        break;
                    case Phase.LessonPreparing:
                        if (stage == 3)
                        {
                            lesson.RecordBaseline();
                            lesson.SetBallCharge(2f);
                            selector.GetStageComponent<DistanceLauncher2D>(3).RequestLaunch();
                        }
                        if (stage == 5)
                        {
                            selector.Ball.Body.constraints = RigidbodyConstraints2D.FreezeRotation;
                            selector.Lever.SetValue(0f);
                            selector.GetStageComponent<VoltageLauncher2D>(5).RequestLaunch();
                        }
                        Next(Phase.LessonResult, stage == 2 || stage == 4 ? 0f : 0.08f);
                        break;
                    case Phase.LessonResult:
                        Add(stage, "lesson-probe-completes-legitimate-experiment", lesson.CurrentLearningGoalSatisfied
                            && selector.Ball.Stars == 0, lesson.LastExplanation);
                        HoldAtGoal(selector);
                        Next(Phase.LessonOnlyAtExit);
                        break;
                    case Phase.LessonOnlyAtExit:
                        Add(stage, "exit-rejects-lesson-without-star", Closed(selector, progression)
                            && lesson.CurrentLearningGoalSatisfied && selector.Ball.Stars == 0, State(selector, progression));
                        HoldAtStar(selector);
                        Next(Phase.CollectQualifiedStar);
                        break;
                    case Phase.CollectQualifiedStar:
                        Add(stage, "qualified-probe-has-real-star-and-lesson", selector.Ball.Stars >= 1
                            && lesson.CurrentLearningGoalSatisfied, State(selector, progression));
                        HoldAtGoal(selector);
                        Next(Phase.QualifiedAtExit, 0.08f);
                        break;
                    case Phase.QualifiedAtExit:
                        Add(stage, "qualified-physics-exit-clears-exactly-once", goal.IsCleared
                            && SessionState.GetInt(Prefix + "ClearCount." + stage, 0) == 1
                            && !selector.Ball.Body.simulated && !selector.Ball.enabled, State(selector, progression));
                        Require(goal.IsCleared, "Qualified ball failed to clear the actual exit.");
                        bool mouseLeverLocked = true;
                        foreach (global::LeverMover mover in selector.Lever.GetComponentsInChildren<global::LeverMover>(true))
                            mouseLeverLocked &= !mover.enabled;
                        Add(stage, "clear-stops-mouse-lever-input", mouseLeverLocked,
                            "The mouse-driven lever is stopped while the cleared scene waits or shows completion.");
                        if (stage == 5)
                        {
                            Next(Phase.CompletionVisible, 0.65f);
                        }
                        else
                        {
                            Add(stage, "qualified-clear-starts-pending-transition", Flag(progression, "IsTransitioning")
                                && !Flag(progression, "IsGameComplete"), State(selector, progression));
                            SessionState.SetInt(Prefix + "PreviousPlayer", selector.Ball.GetInstanceID());
                            SessionState.SetFloat(Prefix + "SceneWait", Time.unscaledTime);
                            Time.timeScale = 0f;
                            Next(Phase.AwaitNextScene, 0f);
                        }
                        break;
                    case Phase.CompletionVisible:
                        GameObject screen = CompletionScreen();
                        Add(stage, "final-stage-shows-game-complete-overlay", Flag(progression, "IsGameComplete")
                            && !Flag(progression, "IsTransitioning") && screen != null && screen.activeInHierarchy
                            && !selector.Ball.Body.simulated, State(selector, progression));
                        Require(screen != null && screen.activeInHierarchy, "GameCompleteScreen did not become visible.");
                        ArmRestartKey();
                        Next(Phase.CompletionKeyboard);
                        break;
                    case Phase.CompletionKeyboard:
                        int naturalFrames = Time.frameCount - SessionState.GetInt(Prefix + "RestartKeyFrame", Time.frameCount);
                        if (naturalFrames < 2) return;
                        bool delivered = SessionState.GetBool(Prefix + "RestartKeyDelivered", false);
                        ElectricStageInput2D blockedInput = selector.GetComponent<ElectricStageInput2D>();
                        bool keyStillPressed = probeKeyboard != null && probeKeyboard.rKey.isPressed;
                        Add(stage, "r-cannot-reset-completed-game", delivered && blockedInput != null && !blockedInput.enabled
                            && Flag(progression, "IsGameComplete") && goal.IsCleared && !selector.Ball.Body.simulated
                            && selector.Ball.Stars >= 1 && CompletionScreen()?.activeInHierarchy == true,
                            "R delivered/pressed at explicit Dynamic input pump=" + delivered
                            + ", pressed at observation=" + keyStillPressed + ", natural frames=" + naturalFrames
                            + ", stage input enabled=" + (blockedInput != null && blockedInput.enabled)
                            + ", screen visible=" + (CompletionScreen()?.activeInHierarchy == true) + "; " + State(selector, progression));
                        RestoreKeyboard();
                        RequestCompletionScreenshot();
                        Next(Phase.CompletionCapture, 0.1f);
                        break;
                    case Phase.CompletionCapture:
                        if (!CompleteScreenshot(selector)) return;
                        Button restart = RestartButton();
                        Require(restart != null && restart.IsActive() && restart.IsInteractable() && EventSystem.current != null,
                            "Completion restart button is not clickable.");
                        SessionState.SetInt(Prefix + "PreviousPlayer", selector.Ball.GetInstanceID());
                        SessionState.SetFloat(Prefix + "SceneWait", Time.unscaledTime);
                        ExecuteEvents.Execute(restart.gameObject, new PointerEventData(EventSystem.current)
                            { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);
                        Next(Phase.AwaitRestart, 0f);
                        break;
                }
            }
            catch (Exception exception)
            {
                RecordError(exception.ToString());
                Finish();
            }
        }

        private static void AwaitScene(int fromStage, bool restart)
        {
            int expectedStage = restart ? 2 : fromStage + 1;
            Require(Time.unscaledTime - SessionState.GetFloat(Prefix + "SceneWait", 0f) < 10f,
                "Timed out awaiting actual scene load to Stage " + expectedStage + ".");
            if (SceneManager.GetActiveScene().path == ScenePath(fromStage)) return;
            Require(SceneManager.GetActiveScene().path == ScenePath(expectedStage), "Transition loaded the wrong scene.");
            var selector = Object.FindFirstObjectByType<ElectricStageSelector>();
            if (selector == null || selector.GetComponent<ElectricStageSession2D>()?.IsInitialized != true) return;
            int loads = Load().sceneLoads.FindAll(path => path == ScenePath(expectedStage)).Count;
            if (!restart)
            {
                Add(fromStage, "clear-loads-next-scene-once-with-unscaled-delay", loads == 1,
                    "Actual SceneManager.sceneLoaded: " + ScenePath(expectedStage) + ", count=" + loads + ", timeScale=" + Time.timeScale + ".");
                SessionState.SetInt(Prefix + "Stage", expectedStage);
                SessionState.SetString(Prefix + "StartupScene", "");
                Next(Phase.Startup, 0.05f);
                return;
            }
            ElectricLearningController2D lesson = selector.GetComponent<ElectricLearningController2D>();
            Component progression = selector.GetComponent("ElectricStageProgression");
            GameObject complete = CompletionScreen();
            Add(2, "completion-button-restarts-at-stage2-once", loads == 1 && selector.SelectedStage == 2
                && selector.Ball.GetInstanceID() != SessionState.GetInt(Prefix + "PreviousPlayer", 0)
                && selector.Ball.Stars == 0 && selector.Ball.CanReceiveForce && lesson != null && !lesson.CurrentLearningGoalSatisfied
                && Time.timeScale > 0f && progression != null && !Flag(progression, "IsGameComplete")
                && (complete == null || !complete.activeInHierarchy),
                "Actual button click loaded a fresh Stage 2 player and reset completion, stars and lesson.");
            Report report = Load();
            Add(0, "progression-scene-load-sequence-is-exact", report.sceneLoads.Count == 4
                && report.sceneLoads[0] == ScenePath(3) && report.sceneLoads[1] == ScenePath(4)
                && report.sceneLoads[2] == ScenePath(5) && report.sceneLoads[3] == ScenePath(2), string.Join(" → ", report.sceneLoads));
            Finish();
        }

        private static void ResetProbe(ElectricStageSelector selector)
        {
            selector.Ball.Body.constraints = RigidbodyConstraints2D.FreezeRotation;
            selector.Ball.Body.gravityScale = 0f;
            selector.RestartStage();
            selector.Lever.SetValue(0f);
        }

        private static void Hold(ElectricBall2D ball, Vector2 position)
        {
            Require(ball.CanReceiveForce, "Physics probe requires the saved, enabled scene player.");
            ball.Body.simulated = true;
            ball.Body.gravityScale = 0f;
            ball.Body.constraints = RigidbodyConstraints2D.FreezeAll;
            ball.Body.linearVelocity = Vector2.zero;
            ball.Body.angularVelocity = 0f;
            ball.Body.position = position;
            Physics2D.SyncTransforms();
        }

        private static void HoldAtGoal(ElectricStageSelector selector) => Hold(selector.Ball,
            selector.GetStageComponent<ElectricGoal2D>(selector.SelectedStage).GetComponent<Collider2D>().bounds.center);
        private static void HoldAtStar(ElectricStageSelector selector) => Hold(selector.Ball,
            selector.GetStageComponent<ElectricStar2D>(selector.SelectedStage).GetComponent<Collider2D>().bounds.center);

        private static void PrepareLesson(ElectricStageSelector selector, ElectricLearningController2D lesson)
        {
            int stage = selector.SelectedStage;
            if (stage == 2)
            {
                var charger = selector.GetStageComponent<ContactCharger2D>(2);
                Transform point = Reference<Transform>(charger, "contactPoint");
                float top = charger.GetComponent<Collider2D>().bounds.extents.y + selector.Ball.GetComponent<Collider2D>().bounds.extents.y;
                selector.Lever.SetValue(1f);
                Hold(selector.Ball, (Vector2)point.position + Vector2.up * (top - 0.03f));
            }
            if (stage == 4)
            {
                lesson.SetBallCharge(-1f);
                var field = selector.GetStageComponent<UniformField2D>(4);
                selector.Ball.Body.position = field.GetComponent<Collider2D>().bounds.center;
                selector.Ball.Body.gravityScale = 0f;
                selector.Ball.Body.constraints = RigidbodyConstraints2D.FreezeRotation;
                selector.Ball.Body.linearVelocity = Vector2.zero;
            }
            if (stage == 5)
            {
                selector.Lever.SetValue(1f);
                var launcher = selector.GetStageComponent<VoltageLauncher2D>(5);
                Hold(selector.Ball, Reference<BoxCollider2D>(launcher, "launchZone").bounds.center);
            }
            Physics2D.SyncTransforms();
        }

        private static bool Closed(ElectricStageSelector selector, Component progression) =>
            !selector.GetStageComponent<ElectricGoal2D>(selector.SelectedStage).IsCleared
            && selector.GetStageComponent<ElectricGoal2D>(selector.SelectedStage).GetComponent<Collider2D>()
                .OverlapPoint(selector.Ball.Body.worldCenterOfMass)
            && selector.Ball.CanReceiveForce && !Flag(progression, "IsTransitioning") && !Flag(progression, "IsGameComplete");

        private static string State(ElectricStageSelector selector, Component progression) => "stars=" + selector.Ball.Stars
            + ", lesson=" + selector.GetComponent<ElectricLearningController2D>().CurrentLearningGoalSatisfied
            + ", cleared=" + selector.GetStageComponent<ElectricGoal2D>(selector.SelectedStage).IsCleared
            + ", simulated=" + selector.Ball.Body.simulated + ", transitioning=" + Flag(progression, "IsTransitioning")
            + ", complete=" + Flag(progression, "IsGameComplete");

        private static bool Flag(Component component, string property)
        {
            PropertyInfo info = component != null ? component.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public) : null;
            Require(info != null && info.PropertyType == typeof(bool), "Missing progression public bool " + property + ".");
            return (bool)info.GetValue(component);
        }

        private static T Reference<T>(Component component, string field) where T : Object =>
            component != null ? new SerializedObject(component).FindProperty(field)?.objectReferenceValue as T : null;

        private static GameObject CompletionScreen()
        {
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                    if (child.name == "GameCompleteScreen") return child.gameObject;
            return null;
        }

        private static Button RestartButton()
        {
            GameObject screen = CompletionScreen();
            if (screen == null) return null;
            foreach (MonoBehaviour view in screen.GetComponentsInChildren<MonoBehaviour>(true))
                if (view != null && view.GetType().Name == "ElectricGameCompleteView")
                {
                    Button linked = Reference<Button>(view, "restartButton");
                    if (linked != null) return linked;
                }
            return screen.GetComponentInChildren<Button>();
        }

        private static void ArmRestartKey()
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
            InputSystem.QueueStateEvent(probeKeyboard, new KeyboardState());
            PumpDynamicInput();
            InputSystem.QueueStateEvent(probeKeyboard, new KeyboardState(Key.R));
            PumpDynamicInput();
            SessionState.SetBool(Prefix + "RestartKeyDelivered", probeKeyboard.rKey.isPressed && probeKeyboard.rKey.wasPressedThisFrame);
            SessionState.SetInt(Prefix + "RestartKeyFrame", Time.frameCount);
        }

        private static void PumpDynamicInput()
        {
            MethodInfo update = typeof(InputSystem).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Static,
                null, new[] { typeof(InputUpdateType) }, null);
            Require(update != null, "Input System has no Dynamic update dispatch for the isolated keyboard probe.");
            update.Invoke(null, new object[] { InputUpdateType.Dynamic });
        }

        private static void RestoreKeyboard()
        {
            if (probeKeyboard == null) return;
            InputSystem.RemoveDevice(probeKeyboard);
            probeKeyboard = null;
            if (previousKeyboard != null && previousKeyboard.added) previousKeyboard.MakeCurrent();
            InputSystem.settings.editorInputBehaviorInPlayMode = previousEditorInput;
            InputSystem.settings.backgroundBehavior = previousBackground;
            InputSystem.settings.updateMode = previousUpdateMode;
            Application.runInBackground = previousRunInBackground;
        }

        private static void RequestCompletionScreenshot()
        {
            Report report = Load();
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                report.screenshotNotes.Add("Completion screenshot unavailable because the batch editor has no graphics device.");
                Save(report);
                return;
            }
            if (File.Exists(ScreenshotPath)) File.Delete(ScreenshotPath);
            SessionState.SetFloat(Prefix + "CaptureStarted", (float)EditorApplication.timeSinceStartup);
            SessionState.SetInt(Prefix + "CaptureFrame", Time.renderedFrameCount);
            ScreenCapture.CaptureScreenshot(ScreenshotPath);
            report.screenshots.Add(ScreenshotPath);
            Save(report);
        }

        private static bool CompleteScreenshot(ElectricStageSelector selector)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return true;
            if (File.Exists(ScreenshotPath) && new FileInfo(ScreenshotPath).Length > 0
                && Time.renderedFrameCount >= SessionState.GetInt(Prefix + "CaptureFrame", 0) + 3)
            {
                Report report = Load();
                report.screenshotNotes.Add("Actual completion GameView capture: " + Screen.width + " x " + Screen.height + ".");
                Save(report);
                return true;
            }
            if (EditorApplication.timeSinceStartup - SessionState.GetFloat(Prefix + "CaptureStarted", 0f) < 3d) return false;
            Camera camera = Camera.main;
            Require(camera != null, "Completion capture fallback has no scene camera.");
            var canvases = new List<CanvasState>();
            try
            {
                foreach (Canvas canvas in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                {
                    if (!canvas.isActiveAndEnabled || canvas.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                    canvases.Add(new CanvasState { canvas = canvas, camera = canvas.worldCamera, distance = canvas.planeDistance });
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = camera;
                    canvas.planeDistance = camera.nearClipPlane + 0.5f;
                }
                Canvas.ForceUpdateCanvases();
                Require(ElectricLearningVisualCheck.CaptureWorld(selector, ScreenshotPath, 1600, 900), "Completion camera/Canvas render produced no PNG.");
                Report report = Load();
                report.screenshotNotes.Add("Completion capture fallback: 1600 x 900 scene camera render including temporarily camera-rendered overlay Canvases; OnGUI excluded. Canvas modes restored; no scene saved.");
                Save(report);
            }
            finally
            {
                foreach (CanvasState state in canvases)
                {
                    if (state.canvas == null) continue;
                    state.canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                    state.canvas.worldCamera = state.camera;
                    state.canvas.planeDistance = state.distance;
                }
                Canvas.ForceUpdateCanvases();
            }
            return true;
        }

        private static void Next(Phase phase, float delay = 0.12f)
        {
            SessionState.SetInt(Prefix + "Phase", (int)phase);
            SessionState.SetFloat(Prefix + "Deadline", Time.unscaledTime + delay);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
        private static Report Load() => JsonUtility.FromJson<Report>(File.ReadAllText(ReportPath));
        private static void Save(Report report)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, JsonUtility.ToJson(report, true));
        }
        private static void Add(int stage, string name, bool passed, string details)
        {
            Report report = Load();
            report.checks.Add(new Check { stage = stage, name = name, passed = passed, details = details });
            Save(report);
        }
        private static void RecordError(string message)
        {
            Report report = Load();
            report.errors.Add(message);
            Save(report);
        }

        private static void Finish()
        {
            RestoreKeyboard();
            Time.timeScale = 1f;
            Report report = Load();
            foreach (string path in report.screenshots)
                report.checks.Add(new Check { stage = 5, name = "completion-screenshot-saved", passed = File.Exists(path)
                    && new FileInfo(path).Length > 0, details = path });
            bool passed = report.errors.Count == 0 && report.checks.Count >= 40;
            foreach (Check check in report.checks) passed &= check.passed;
            report.status = passed ? "passed" : "failed";
            Save(report);
            SessionState.SetBool(Prefix + "Active", false);
            SessionState.SetBool(Prefix + "TrackingLoads", false);
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Debug.Log("Stage progression validation: " + report.status + ". Report: " + ReportPath);
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
            EditorApplication.delayCall += () => EditorApplication.Exit(passed ? 0 : 1);
        }
    }
}
