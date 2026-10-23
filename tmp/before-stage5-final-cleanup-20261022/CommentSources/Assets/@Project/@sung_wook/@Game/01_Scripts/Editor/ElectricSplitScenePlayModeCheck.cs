using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace sung_wook
{
    /// <summary>저장된 네 분리 씬을 실제로 로드해 연결·입력·학습 사건·재시작을 검사한다. 씬을 저장하지 않는다.</summary>
    [InitializeOnLoad]
    public static class ElectricSplitScenePlayModeCheck
    {
        private const string DirectoryPath = "Assets/@Project/@sung_wook/@Game/00_Scenes/Stage/";
        private const string Prefix = "sung_wook.SplitSceneCheck.";
        private static RigidbodyConstraints2D constraints;

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
            public List<Check> checks = new List<Check>();
            public List<string> errors = new List<string>();
        }

        static ElectricSplitScenePlayModeCheck()
        {
            if (Application.isBatchMode && SessionState.GetBool(Prefix + "Active", false)) Resume();
        }

        public static void RunBatch()
        {
            if (!Application.isBatchMode) return;
            EditorSceneManager.OpenScene(ScenePath(2), OpenSceneMode.Single);
            Save(new Report());
            SessionState.SetBool(Prefix + "Active", true);
            SessionState.SetInt(Prefix + "Stage", 2);
            SessionState.SetInt(Prefix + "Phase", 0);
            SessionState.SetString(Prefix + "Started", DateTime.UtcNow.ToString("O"));
            Resume();
            ElectricBatchPlayMode.EnterAfterEditorStartup();
        }

        private static string ScenePath(int stage) => DirectoryPath + "Stage_" + stage.ToString("00") + ".unity";
        private static string ReportPath => Path.Combine(Path.GetDirectoryName(Application.dataPath),
            "Logs/ElectricSplitScenePlayModeReport.json");

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
            Report report = Load();
            report.errors.Add(message + "\n" + stack);
            Save(report);
        }

        private static void Tick()
        {
            if (!Application.isBatchMode || !SessionState.GetBool(Prefix + "Active", false)) return;
            try
            {
                if ((DateTime.UtcNow - DateTime.Parse(SessionState.GetString(Prefix + "Started", ""))).TotalSeconds > 120)
                    throw new TimeoutException("Split-scene Play Mode verification timed out.");
                if (!EditorApplication.isPlaying || EditorApplication.isPaused) return;
                int stage = SessionState.GetInt(Prefix + "Stage", 2);
                if (SceneManager.GetActiveScene().path != ScenePath(stage)) return;
                var selector = UnityEngine.Object.FindFirstObjectByType<ElectricStageSelector>();
                if (selector == null || selector.GetComponent<ElectricStageSession2D>()?.IsInitialized != true) return;
                if (Time.time < SessionState.GetFloat(Prefix + "Deadline", 0f)) return;
                var lesson = selector.GetComponent<ElectricLearningController2D>();
                int phase = SessionState.GetInt(Prefix + "Phase", 0);
                if (phase == 0)
                {
                    CheckStartup(selector, lesson, stage);
                    CheckInputs(selector, lesson, stage);
                    ArrangeExperiment(selector, lesson, stage);
                    SessionState.SetInt(Prefix + "Phase", 1);
                    SessionState.SetFloat(Prefix + "Deadline", Time.time + (stage == 5 ? 2.2f : 0.15f));
                }
                else if (phase == 1)
                {
                    if (stage == 3)
                    {
                        WithKeyboard(keyboard => Press(keyboard, selector.GetComponent<ElectricLearningInput2D>(), Key.B));
                        lesson.SetBallCharge(2f);
                        WithKeyboard(keyboard => Press(keyboard, selector.GetStageComponent<DistanceLauncher2D>(3), Key.E));
                    }
                    if (stage == 5)
                    {
                        selector.Ball.Body.constraints = constraints;
                        WithKeyboard(keyboard => Press(keyboard, selector.GetStageComponent<VoltageLauncher2D>(5), Key.E));
                    }
                    SessionState.SetInt(Prefix + "Phase", 2);
                    SessionState.SetFloat(Prefix + "Deadline", Time.time + 0.08f);
                }
                else
                {
                    CheckExperiment(selector, lesson, stage);
                    if (stage == 2) selector.Ball.Body.constraints = constraints;
                    CheckRestart(selector, lesson, stage);
                    if (stage == 5)
                    {
                        Finish();
                        return;
                    }
                    SessionState.SetInt(Prefix + "Stage", stage + 1);
                    SessionState.SetInt(Prefix + "Phase", 0);
                    SessionState.SetFloat(Prefix + "Deadline", Time.time + 0.1f);
                    EditorSceneManager.LoadSceneInPlayMode(ScenePath(stage + 1), new LoadSceneParameters(LoadSceneMode.Single));
                }
            }
            catch (Exception exception)
            {
                OnLog(exception.ToString(), "", LogType.Exception);
                Finish();
            }
        }

        private static void CheckStartup(ElectricStageSelector selector, ElectricLearningController2D lesson, int stage)
        {
            var bootstrap = selector.GetComponent<ElectricStageSceneBootstrap>();
            int roots = 0;
            foreach (GameObject root in selector.StageRoots) if (root != null) roots++;
            Add(stage, "saved-scene-starts-own-stage", bootstrap != null && bootstrap.StageNumber == stage
                && selector.SelectedStage == stage && roots == 1 && selector.CurrentStageRoot.activeInHierarchy,
                "One saved bootstrap binds only this scene's stage.");
            Add(stage, "single-existing-player-and-input-stack", selector.Ball != null && selector.Lever.Reader != null
                && UnityEngine.Object.FindObjectsByType<Player>(FindObjectsSortMode.None).Length == 1
                && selector.Ball.GetComponents<PlayerController>().Length == 1
                && selector.Ball.GetComponents<PlayerInputReader>().Length == 1
                && selector.Ball.GetComponents<PlayerMovement>().Length == 1
                && selector.GetComponents<ElectricStageInput2D>().Length == 1
                && selector.GetComponents<ElectricLearningInput2D>().Length == 1,
                "Existing player, mouse lever and individual input responsibilities are reused.");
            bool bindings = true;
            foreach (MonoBehaviour component in selector.CurrentStageRoot.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component == null) { bindings = false; continue; }
                if (!(component is ContactCharger2D || component is DistanceLauncher2D
                    || component is UniformField2D || component is VoltageLauncher2D)) continue;
                var serialized = new SerializedObject(component);
                bindings &= serialized.FindProperty("ball").objectReferenceValue == selector.Ball
                    && serialized.FindProperty("lever").objectReferenceValue == selector.Lever;
            }
            ElectricGoal2D goal = selector.GetStageComponent<ElectricGoal2D>(stage);
            bindings &= goal != null && new SerializedObject(goal).FindProperty("learningRequirement").objectReferenceValue == lesson;
            Add(stage, "devices-and-goal-bind-current-session", bindings, "Saved device geometry and events are retained; lesson gate is connected.");
            Add(stage, "initial-polarity-and-trial", selector.Ball.CurrentPolarity == (stage == 2 ? Polarity.Neutral : Polarity.Positive)
                && lesson.IsPreparingTrial == (stage == 3) && !lesson.CurrentLearningGoalSatisfied,
                "Stage 2 starts neutral; Stage 3 is held for a baseline; other stages start positive.");
            ElectricLearningStatusView view = selector.CurrentStageRoot.GetComponentInChildren<ElectricLearningStatusView>(true);
            view.RefreshDisplay();
            Text hint = view.transform.Find("Common Controls").GetComponent<Text>();
            Add(stage, "saved-ui-uses-scene-controls", hint.text.Contains("Space") && !hint.text.Contains("2~5")
                && new SerializedObject(view).FindProperty("selector").objectReferenceValue == selector,
                "Existing state UI is connected and the retired number-key instruction is absent.");
        }

        private static void CheckInputs(ElectricStageSelector selector, ElectricLearningController2D lesson, int stage)
        {
            selector.Ball.CollectStar();
            if (stage == 3) lesson.RecordBaseline();
            bool baseline = lesson.HasBaseline;
            WithKeyboard(keyboard =>
            {
                foreach (Key key in new[] { Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5,
                    Key.Numpad2, Key.Numpad3, Key.Numpad4, Key.Numpad5 })
                    Press(keyboard, selector.GetComponent<ElectricStageInput2D>(), key);
                Add(stage, "number-keys-preserve-stage-and-progress", selector.SelectedStage == stage
                    && selector.Ball.Stars == 1 && lesson.HasBaseline == baseline,
                    "Digit and numpad 2-5 neither select a stage nor restart the current experiment.");
                selector.Ball.Player.ChangeState(PlayerStateType.Neutral);
                Press(keyboard, selector.Ball.GetComponent<PlayerController>(), Key.Space);
                bool positive = selector.Ball.CurrentPolarity == Polarity.Positive;
                Press(keyboard, selector.Ball.GetComponent<PlayerController>(), Key.Space);
                Add(stage, "space-still-cycles-player-state", positive && selector.Ball.CurrentPolarity == Polarity.Negative,
                    "Space remains owned by the existing player input/controller.");
            });
            selector.RestartStage();
        }

        private static void ArrangeExperiment(ElectricStageSelector selector, ElectricLearningController2D lesson, int stage)
        {
            constraints = selector.Ball.Body.constraints;
            selector.Lever.SetValue(stage == 2 || stage == 5 ? 1f : 0f);
            if (stage == 2)
            {
                var charger = selector.GetStageComponent<ContactCharger2D>(2);
                Transform point = new SerializedObject(charger).FindProperty("contactPoint").objectReferenceValue as Transform;
                selector.Ball.Body.position = (Vector2)point.position + Vector2.up * 0.6f;
                selector.Ball.Body.constraints = RigidbodyConstraints2D.FreezeAll;
            }
            if (stage == 4) lesson.SetBallCharge(-1f);
            if (stage == 5)
            {
                var launcher = selector.GetStageComponent<VoltageLauncher2D>(5);
                var zone = new SerializedObject(launcher).FindProperty("launchZone").objectReferenceValue as BoxCollider2D;
                selector.Ball.Body.position = zone.bounds.center;
                selector.Ball.Body.constraints = RigidbodyConstraints2D.FreezeAll;
            }
        }

        private static void CheckExperiment(ElectricStageSelector selector, ElectricLearningController2D lesson, int stage)
        {
            bool passed = false;
            if (stage == 2) passed = lesson.ContactChallengeSatisfied && selector.Ball.CurrentPolarity == Polarity.Positive;
            if (stage == 3) passed = lesson.HasBaseline && lesson.HasResult && lesson.ComparisonChallengeSatisfied
                && selector.Ball.Body.linearVelocity.sqrMagnitude > 0.01f;
            if (stage == 4) passed = lesson.NegativeChallengeSatisfied && selector.TryGetCurrentForceSample(out ElectricForceSample sample)
                && sample.Field.y < 0f && sample.Force.y > 0f;
            if (stage == 5) passed = lesson.VoltageChallengeSatisfied && lesson.LastLaunchVoltage >= 50f
                && selector.Ball.Body.linearVelocity.y > 0f;
            Add(stage, "actual-experiment-reaches-learning-goal", passed, lesson.LearningGoalProgress + " / " + lesson.LastExplanation);
        }

        private static void CheckRestart(ElectricStageSelector selector, ElectricLearningController2D lesson, int stage)
        {
            selector.Ball.CollectStar();
            WithKeyboard(keyboard => Press(keyboard, selector.GetComponent<ElectricStageInput2D>(), Key.R));
            selector.TryGetSpawnPosition(out Vector2 spawn);
            Add(stage, "r-resets-only-current-session", selector.SelectedStage == stage
                && selector.Ball.Stars == 0 && selector.Ball.CanReceiveForce && !lesson.CurrentLearningGoalSatisfied
                && !lesson.HasBaseline && !lesson.HasResult && lesson.CurrentVoltage == 0f
                && lesson.IsPreparingTrial == (stage == 3)
                && Vector2.Distance(selector.Ball.Body.position, spawn) < 0.001f,
                "R restores the captured scene spawn, player, lesson, stars and launcher state without loading another scene.");
        }

        private static void WithKeyboard(Action<Keyboard> action)
        {
            Keyboard previous = Keyboard.current;
            var editorBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            var background = InputSystem.settings.backgroundBehavior;
            var mode = InputSystem.settings.updateMode;
            bool runInBackground = Application.runInBackground;
            Keyboard keyboard = null;
            try
            {
                InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
                Application.runInBackground = true;
                keyboard = InputSystem.AddDevice<Keyboard>();
                keyboard.MakeCurrent();
                action(keyboard);
            }
            finally
            {
                if (keyboard != null) InputSystem.RemoveDevice(keyboard);
                if (previous != null && previous.added) previous.MakeCurrent();
                InputSystem.settings.editorInputBehaviorInPlayMode = editorBehavior;
                InputSystem.settings.backgroundBehavior = background;
                InputSystem.settings.updateMode = mode;
                Application.runInBackground = runInBackground;
            }
        }

        private static void Press(Keyboard keyboard, MonoBehaviour target, Key key)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            UpdateInput();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
            UpdateInput();
            if (!keyboard[key].wasPressedThisFrame) throw new InvalidOperationException("Keyboard dispatch failed: " + key);
            target.SendMessage("Update", SendMessageOptions.RequireReceiver);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            UpdateInput();
        }

        private static void UpdateInput()
        {
            typeof(InputSystem).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Static,
                null, new[] { typeof(InputUpdateType) }, null).Invoke(null, new object[] { InputUpdateType.Dynamic });
        }

        private static void Add(int stage, string name, bool passed, string details)
        {
            Report report = Load();
            report.checks.Add(new Check { stage = stage, name = name, passed = passed, details = details });
            Save(report);
        }

        private static Report Load() => JsonUtility.FromJson<Report>(File.ReadAllText(ReportPath));
        private static void Save(Report report)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, JsonUtility.ToJson(report, true));
        }

        private static void Finish()
        {
            Report report = Load();
            bool passed = report.errors.Count == 0 && report.checks.Count == 36;
            foreach (Check check in report.checks) passed &= check.passed;
            report.status = passed ? "passed" : "failed";
            Save(report);
            SessionState.SetBool(Prefix + "Active", false);
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            EditorApplication.isPlaying = false;
            EditorApplication.delayCall += () => EditorApplication.Exit(passed ? 0 : 1);
        }
    }
}
