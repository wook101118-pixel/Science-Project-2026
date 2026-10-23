using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace sung_wook
{
                                                                               
    [InitializeOnLoad]
    public static class ElectricStageModuleValidation
    {
        private const string Prefix = "sung_wook.StageModuleValidation.";
        private const string SceneFolder = "Assets/@Project/@sung_wook/@Game/00_Scenes/Stage/";
        private static readonly string[] SharedRectNames = { "HUD", "HDD", "Learning_Objective_Panel", "Stage_Number_Container" };
        private static readonly List<MotionProbe> motions = new List<MotionProbe>();
        private static RigidbodyConstraints2D savedConstraints;
        private static float savedGravity;
        private static Vector2 fieldForce;
        private static float motionDeadline;
        private static EditorWindow gameView;

        private enum Phase { Startup, ClosedWhilePaused, OpenWhilePaused, InterruptedToggle, Screenshot, ExperimentReady, ExperimentResult, ObserveMotion }

        [Serializable]
        private sealed class Check
        {
            public int stage;
            public string name;
            public bool passed;
            public string details;
        }

        [Serializable]
        private sealed class RectReference
        {
            public string name;
            public Vector2 anchorMin, anchorMax, pivot, position, size;
            public Vector3 scale;
        }

        [Serializable]
        private sealed class Report
        {
            public string status = "running";
            public string mode = "Saved-scene Edit Mode inspection and natural Play Mode frames; no validation edits are saved.";
            public List<Check> checks = new List<Check>();
            public List<string> errors = new List<string>();
            public List<string> skipped = new List<string>();
            public List<string> screenshots = new List<string>();
            public List<string> screenshotNotes = new List<string>();
            public List<RectReference> referenceUI = new List<RectReference>();
            public List<string> referenceTiles = new List<string>();
            public Vector3 cellSize, cellGap;
            public int cellLayout;
            public Vector2 canvasReferenceResolution;
            public int canvasScaleMode;
            public int movingPrefabCount;
        }

        private sealed class MotionProbe
        {
            public ElectricObstacleMotion2D component;
            public Vector2 start;
            public float maximumDistance;
        }

        private sealed class OverlayCanvasState
        {
            public Canvas canvas;
            public Camera camera;
            public float planeDistance;
        }

        static ElectricStageModuleValidation()
        {
            if (Application.isBatchMode && SessionState.GetBool(Prefix + "Active", false)) Resume();
        }

        public static void RunBatch()
        {
            if (!Application.isBatchMode)
            {
                Debug.LogWarning("RunBatch requires a separately launched Unity batch editor.");
                return;
            }

            Save(new Report());
            try
            {
                for (int stage = 2; stage <= 5; stage++) InspectSavedScene(stage);
                Report report = Load();
                Add(0, "moving-platform-prefabs-exist", report.movingPrefabCount > 0,
                    report.movingPrefabCount + " saved moving platform instances across Stage 2-5.");
                EditorSceneManager.OpenScene(ScenePath(2), OpenSceneMode.Single);
                SessionState.SetBool(Prefix + "Active", true);
                SessionState.SetInt(Prefix + "Stage", 2);
                SessionState.SetString(Prefix + "StartupScene", "");
                SessionState.SetInt(Prefix + "Phase", (int)Phase.Startup);
                SessionState.SetFloat(Prefix + "Deadline", 0f);
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
        private static string ProjectFolder => Path.GetDirectoryName(Application.dataPath);
        private static string ReportPath => Path.Combine(ProjectFolder, "Logs/ElectricStageModuleReport.json");

        private static void InspectSavedScene(int stage)
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath(stage), OpenSceneMode.Single);
            int missing = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                    missing += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject);
            Add(stage, "saved-scene-has-no-missing-scripts", missing == 0, missing + " missing scripts.");

            var devices = new List<Component>();
            devices.AddRange(FindAll<ContactCharger2D>(scene));
            devices.AddRange(FindAll<DistanceLauncher2D>(scene));
            devices.AddRange(FindAll<UniformField2D>(scene));
            devices.AddRange(FindAll<VoltageLauncher2D>(scene));
            var moving = FindAll<ElectricObstacleMotion2D>(scene);
            devices.AddRange(moving);
            var panel = FindAll<CollapsiblePanel>(scene);
            devices.AddRange(panel);
            bool prefabs = devices.Count > 1 && panel.Count == 1;
            bool portable = prefabs;
            var assets = new List<string>();
            var portability = new List<string>();
            foreach (Component device in devices)
            {
                string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(device.gameObject);
                prefabs &= !string.IsNullOrEmpty(path) && AssetDatabase.LoadAssetAtPath<GameObject>(path) != null;
                bool ownGeometry = PrefabGeometryIsPortable(device, path, out string explanation);
                portable &= ownGeometry;
                portability.Add(device.GetType().Name + ": " + explanation);
                assets.Add(device.GetType().Name + ": " + (string.IsNullOrEmpty(path) ? "NOT A PREFAB" : path));
            }
            Add(stage, "saved-mechanisms-and-panel-are-prefab-instances", prefabs, string.Join("; ", assets));
            Add(stage, "prefabs-own-their-geometry-and-allow-scene-binding", portable,
                string.Join("; ", portability));

            Report report = Load();
            report.movingPrefabCount += moving.Count;
            var canvases = FindAll<Canvas>(scene);
            Canvas mainCanvas = canvases.Find(candidate => candidate.name == "Canvas" && candidate.renderMode != RenderMode.WorldSpace);
            CanvasScaler scaler = mainCanvas != null ? mainCanvas.GetComponent<CanvasScaler>() : null;
            bool ui = scaler != null;
            if (stage == 2 && scaler != null)
            {
                report.canvasReferenceResolution = scaler.referenceResolution;
                report.canvasScaleMode = (int)scaler.uiScaleMode;
            }
            if (stage > 2 && scaler != null)
                ui &= (scaler.referenceResolution - report.canvasReferenceResolution).sqrMagnitude < 0.01f
                    && (int)scaler.uiScaleMode == report.canvasScaleMode;
            foreach (string name in SharedRectNames)
            {
                RectTransform rect = mainCanvas != null ? FindNamedRect(mainCanvas, name) : null;
                ui &= rect != null;
                if (rect == null) continue;
                if (stage == 2) report.referenceUI.Add(Snapshot(name, rect));
                else
                {
                    RectReference expected = report.referenceUI.Find(item => item.name == name);
                    ui &= expected != null && SameLayout(expected, rect);
                }
            }
            Save(report);
            Add(stage, "hud-uses-stage2-reference-layout", ui,
                "Canvas scaling plus HUD, HDD, objective panel and stage number anchors/offsets match the Stage 2 reference.");

            var maps = FindAll<Tilemap>(scene);
            int occupied = 0;
            var palette = new HashSet<string>();
            Grid grid = null;
            foreach (Tilemap map in maps)
            {
                if (!map.gameObject.activeInHierarchy) continue;
                if (grid == null) grid = map.GetComponentInParent<Grid>();
                foreach (Vector3Int position in map.cellBounds.allPositionsWithin)
                {
                    TileBase tile = map.GetTile(position);
                    if (tile == null) continue;
                    occupied++;
                    palette.Add(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(tile)));
                }
            }
            report = Load();
            bool geometry = grid != null && occupied > 0;
            if (stage == 2 && grid != null)
            {
                report.cellSize = grid.cellSize;
                report.cellGap = grid.cellGap;
                report.cellLayout = (int)grid.cellLayout;
                report.referenceTiles.AddRange(palette);
            }
            else if (grid != null)
            {
                geometry &= (grid.cellSize - report.cellSize).sqrMagnitude < 0.0001f
                    && (grid.cellGap - report.cellGap).sqrMagnitude < 0.0001f
                    && (int)grid.cellLayout == report.cellLayout
                    && palette.Overlaps(report.referenceTiles);
            }
            Save(report);
            Add(stage, "tilemap-uses-reference-grid-and-palette", geometry,
                occupied + " occupied active cells; stage route can vary while preserving Stage 2 cell geometry and shared tiles.");
        }

        private static void Resume()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
            if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                Type gameViewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
                Require(gameViewType != null, "This editor has no GameView type for visual verification.");
                gameView = EditorWindow.GetWindow(gameViewType);
                const BindingFlags instanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                const BindingFlags staticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy;
                Type sizesType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameViewSizes");
                object sizes = sizesType.GetProperty("instance", staticFlags).GetValue(null);
                object group = sizesType.GetProperty("currentGroup", instanceFlags).GetValue(sizes);
                string[] labels = (string[])group.GetType().GetMethod("GetDisplayTexts", instanceFlags).Invoke(group, null);
                for (int index = 0; index < labels.Length; index++)
                {
                    if (!labels[index].StartsWith("16:9", StringComparison.Ordinal)) continue;
                    gameViewType.GetProperty("selectedSizeIndex", instanceFlags).SetValue(gameView, index);
                    break;
                }
                gameView.Repaint();
            }
        }

        private static void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) RecordError(message + "\n" + stack);
        }

        private static void Tick()
        {
            if (!Application.isBatchMode || !SessionState.GetBool(Prefix + "Active", false)) return;
            try
            {
                if (gameView != null) gameView.Repaint();
                if ((DateTime.UtcNow - DateTime.Parse(SessionState.GetString(Prefix + "Started", ""))).TotalSeconds > 240)
                    throw new TimeoutException("Stage module validation exceeded 240 seconds.");
                if (!EditorApplication.isPlaying || EditorApplication.isPaused) return;
                int stage = SessionState.GetInt(Prefix + "Stage", 2);
                if (SceneManager.GetActiveScene().path != ScenePath(stage)) return;
                ElectricStageSelector selector = Object.FindFirstObjectByType<ElectricStageSelector>();
                if (selector == null || selector.GetComponent<ElectricStageSession2D>()?.IsInitialized != true) return;
                if ((Phase)SessionState.GetInt(Prefix + "Phase", 0) == Phase.Startup)
                {
                                                                                                                             
                    if (SessionState.GetString(Prefix + "StartupScene", "") != ScenePath(stage))
                    {
                        SessionState.SetString(Prefix + "StartupScene", ScenePath(stage));
                        SessionState.SetInt(Prefix + "StartupFrame", Time.frameCount);
                        return;
                    }
                    if (Time.frameCount <= SessionState.GetInt(Prefix + "StartupFrame", 0) + 1) return;
                }
                foreach (MotionProbe motion in motions)
                    if (motion.component != null)
                        motion.maximumDistance = Mathf.Max(motion.maximumDistance,
                            Vector2.Distance(motion.start, motion.component.GetComponent<Rigidbody2D>().position));
                if (Time.unscaledTime < SessionState.GetFloat(Prefix + "Deadline", 0f)) return;

                ElectricLearningController2D lesson = selector.GetComponent<ElectricLearningController2D>();
                CollapsiblePanel panel = Object.FindFirstObjectByType<CollapsiblePanel>();
                Require(panel != null, "Saved scene has no active collapsible panel.");
                LayoutElement content = Reference<LayoutElement>(panel, "content");
                Button button = Reference<Button>(panel, "toggleButton");
                RectTransform panelRect = panel.transform as RectTransform;
                switch ((Phase)SessionState.GetInt(Prefix + "Phase", 0))
                {
                    case Phase.Startup:
                        CheckStartup(stage, selector, lesson);
                        Canvas.ForceUpdateCanvases();
                        Require(content != null && button != null && panelRect != null && content.preferredHeight > 0f,
                            "Panel content, button or expanded size is invalid.");
                        SessionState.SetFloat(Prefix + "ExpandedHeight", content.preferredHeight);
                        SessionState.SetFloat(Prefix + "PanelHeight", panelRect.rect.height);
                        Time.timeScale = 0f;
                        Click(button);
                        Advance(Phase.ClosedWhilePaused, PanelWait(panel));
                        break;
                    case Phase.ClosedWhilePaused:
                        Canvas.ForceUpdateCanvases();
                        Add(stage, "close-button-collapses-at-timescale-zero", Mathf.Abs(content.preferredHeight) < 0.1f
                            && panelRect.rect.height < SessionState.GetFloat(Prefix + "PanelHeight", 0f) - 1f
                            && button.IsActive() && button.IsInteractable(),
                            "Actual button pointer-click dispatch; content=" + content.preferredHeight + ", panel=" + panelRect.rect.height + ".");
                        Click(button);
                        Advance(Phase.OpenWhilePaused, PanelWait(panel));
                        break;
                    case Phase.OpenWhilePaused:
                        Canvas.ForceUpdateCanvases();
                        Add(stage, "same-button-restores-open-layout", Mathf.Abs(content.preferredHeight
                            - SessionState.GetFloat(Prefix + "ExpandedHeight", 0f)) < 0.1f
                            && Mathf.Abs(panelRect.rect.height - SessionState.GetFloat(Prefix + "PanelHeight", 0f)) < 1f,
                            "Panel finishes its unscaled coroutine and returns to the saved expanded dimensions.");
                        Click(button);
                        Click(button);
                        Advance(Phase.InterruptedToggle, PanelWait(panel));
                        break;
                    case Phase.InterruptedToggle:
                        Add(stage, "rapid-panel-toggle-has-no-stale-coroutine", Mathf.Abs(content.preferredHeight
                            - SessionState.GetFloat(Prefix + "ExpandedHeight", 0f)) < 0.1f,
                            "A close immediately interrupted by reopen finishes at the open height.");
                        CaptureScreenshot(stage);
                        Advance(Phase.Screenshot, 0.15f);
                        break;
                    case Phase.Screenshot:
                        if (!WaitForScreenshot(stage, selector)) return;
                        Time.timeScale = 1f;
                        selector.RestartStage();
                        ArrangeExperiment(stage, selector, lesson);
                        ObserveMotions(selector);
                        Advance(Phase.ExperimentReady, stage == 2 ? 2.4f : stage == 5 ? 2.3f : 0.12f);
                        break;
                    case Phase.ExperimentReady:
                        if (stage == 3)
                        {
                            lesson.RecordBaseline();
                            lesson.SetBallCharge(2f);
                            selector.GetStageComponent<DistanceLauncher2D>(3).RequestLaunch();
                        }
                        if (stage == 5)
                        {
                            selector.Ball.Body.constraints = savedConstraints;
                            selector.Lever.SetValue(0f);
                            selector.GetStageComponent<VoltageLauncher2D>(5).RequestLaunch();
                        }
                                                                                                                           
                        if (stage == 4) CheckExperiment(stage, selector, lesson);
                        Advance(Phase.ExperimentResult, stage == 2 || stage == 4 ? 0f : 0.06f);
                        break;
                    case Phase.ExperimentResult:
                        if (stage != 4) CheckExperiment(stage, selector, lesson);
                                                                                                               
                        if (stage != 3) selector.Ball.Body.constraints = savedConstraints;
                        selector.Ball.Body.gravityScale = savedGravity;
                        selector.RestartStage();
                        Add(stage, "restart-clears-module-state", !lesson.CurrentLearningGoalSatisfied && selector.Ball.CanReceiveForce
                            && selector.Ball.Stars == 0 && !lesson.HasBaseline && !lesson.HasResult && lesson.CurrentVoltage == 0f,
                            "Restart clears lesson snapshots, voltage, stars and frozen launch state.");
                        foreach (MotionProbe motion in motions)
                        {
                            Vector2 localStart = new SerializedObject(motion.component).FindProperty("startLocalPosition").vector2Value;
                            Transform parent = motion.component.transform.parent;
                            Vector2 expected = parent != null ? parent.TransformPoint(localStart) : (Vector3)localStart;
                            Add(stage, "restart-restores-moving-platform-start", Vector2.Distance(
                                motion.component.GetComponent<Rigidbody2D>().position, expected) < 0.01f,
                                motion.component.name + " returns to its prefab-relative start before the next physics step.");
                        }
                        Advance(Phase.ObserveMotion, Mathf.Max(0.05f, motionDeadline - Time.unscaledTime));
                        break;
                    case Phase.ObserveMotion:
                        foreach (MotionProbe motion in motions)
                        {
                            Rigidbody2D body = motion.component != null ? motion.component.GetComponent<Rigidbody2D>() : null;
                            Collider2D collider = motion.component != null ? motion.component.GetComponent<Collider2D>() : null;
                            Add(stage, "moving-platform-travels-through-real-physics", body != null && body.bodyType == RigidbodyType2D.Kinematic
                                && collider != null && collider.enabled && !collider.isTrigger && motion.maximumDistance > 0.03f,
                                (motion.component != null ? motion.component.name : "Destroyed motion") + ": maximum observed displacement=" + motion.maximumDistance + ".");
                        }
                        motions.Clear();
                        if (stage == 5) { Finish(); return; }
                        SessionState.SetInt(Prefix + "Stage", stage + 1);
                        Advance(Phase.Startup, 0.1f);
                        EditorSceneManager.LoadSceneInPlayMode(ScenePath(stage + 1), new LoadSceneParameters(LoadSceneMode.Single));
                        break;
                }
            }
            catch (Exception exception)
            {
                RecordError(exception.ToString());
                Finish();
            }
        }

        private static void CheckStartup(int stage, ElectricStageSelector selector, ElectricLearningController2D lesson)
        {
            bool bound = selector.SelectedStage == stage && selector.CurrentStageRoot != null
                && selector.CurrentStageRoot.activeInHierarchy && lesson != null && selector.Lever != null
                && selector.Lever.Reader != null && selector.Ball != null
                && Object.FindObjectsByType<ElectricBall2D>(FindObjectsSortMode.None).Length == 1;
            foreach (MonoBehaviour component in selector.CurrentStageRoot.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (!(component is ContactCharger2D || component is DistanceLauncher2D
                    || component is UniformField2D || component is VoltageLauncher2D)) continue;
                bound &= Reference<ElectricBall2D>(component, "ball") == selector.Ball
                    && Reference<StageLever2D>(component, "lever") == selector.Lever;
            }
            Add(stage, "saved-modules-bind-this-scenes-player-and-lever", bound,
                "One active scene player; every electrical prefab uses that player and the existing lever.");
            ElectricStageHudView hud = Object.FindFirstObjectByType<ElectricStageHudView>();
            TMP_Text stageNumber = Reference<TMP_Text>(hud, "stageNumberText");
            TMP_Text quest = Reference<TMP_Text>(hud, "questText");
            bool measurement = stageNumber != null && stageNumber.text.Trim() == stage.ToString()
                && quest != null && quest.text.Contains("\n");
            string displayed = quest != null ? quest.text : "Missing common HUD quest text";
            if (stage == 5)
            {
                int valueStart = displayed.IndexOf("현재 ", StringComparison.Ordinal);
                int valueEnd = valueStart >= 0 ? displayed.IndexOf(" V /", valueStart, StringComparison.Ordinal) : -1;
                float displayedVoltage = float.NaN;
                measurement &= valueStart >= 0 && valueEnd > valueStart + 3
                    && float.TryParse(displayed.Substring(valueStart + 3, valueEnd - valueStart - 3),
                        NumberStyles.Float, CultureInfo.CurrentCulture, out displayedVoltage)
                    && Mathf.Abs(displayedVoltage - lesson.CurrentVoltage) <= 0.6f;
            }
            else
            {
                bool hasSample = selector.TryGetCurrentForceSample(out ElectricForceSample sample);
                measurement &= hasSample;
                if (stage == 4)
                    measurement &= displayed.Contains("E=" + sample.Field.y.ToString("0.0"))
                        && displayed.Contains("F=" + sample.Force.y.ToString("0.0"));
                else
                    measurement &= displayed.Contains("F=" + sample.ForceMagnitude.ToString("0.0"));
            }
            Add(stage, "common-hud-shows-current-stage-and-live-measurement", measurement,
                "Stage number=" + (stageNumber != null ? stageNumber.text : "missing") + "; " + displayed);
        }

        private static void ArrangeExperiment(int stage, ElectricStageSelector selector, ElectricLearningController2D lesson)
        {
            Rigidbody2D body = selector.Ball.Body;
            savedConstraints = body.constraints;
            savedGravity = body.gravityScale;
            selector.Lever.SetValue(stage == 2 || stage == 5 ? 1f : 0f);
            if (stage == 2)
            {
                var charger = selector.GetStageComponent<ContactCharger2D>(2);
                Transform contact = Reference<Transform>(charger, "contactPoint");
                Require(contact != null, "Contact prefab lost its touching anchor.");
                float electrodeHalfHeight = charger.GetComponent<Collider2D>().bounds.extents.y;
                float ballHalfHeight = selector.Ball.GetComponent<Collider2D>().bounds.extents.y;
                body.position = (Vector2)contact.position + Vector2.up * (electrodeHalfHeight + ballHalfHeight - 0.03f);
                body.constraints = RigidbodyConstraints2D.FreezeAll;
            }
            if (stage == 4)
            {
                lesson.SetBallCharge(-1f);
                var field = selector.GetStageComponent<UniformField2D>(4);
                body.position = field.GetComponent<BoxCollider2D>().bounds.center;
                body.gravityScale = 0f;
                body.linearVelocity = Vector2.zero;
                body.constraints = RigidbodyConstraints2D.FreezeRotation;
                Physics2D.SyncTransforms();
                Require(field.TryGetSample(out ElectricForceSample sample) && sample.IsInRange,
                    "Field prefab has no active region around its own center.");
                fieldForce = sample.Force;
                Add(stage, "negative-charge-force-opposes-field", sample.Field.y < 0f && sample.Force.y > 0f
                    && Vector2.Dot(sample.Field, sample.Force) < 0f,
                    "At the field center: E=" + sample.Field + ", F=" + sample.Force + ".");
            }
            if (stage == 5)
            {
                var launcher = selector.GetStageComponent<VoltageLauncher2D>(5);
                BoxCollider2D zone = Reference<BoxCollider2D>(launcher, "launchZone");
                Require(zone != null && zone.enabled && zone.isTrigger, "Voltage prefab has no active launch trigger.");
                body.position = zone.bounds.center;
                body.constraints = RigidbodyConstraints2D.FreezeAll;
            }
            Physics2D.SyncTransforms();
        }

        private static void CheckExperiment(int stage, ElectricStageSelector selector, ElectricLearningController2D lesson)
        {
            bool passed = false;
            string observed = "velocity=" + selector.Ball.Body.linearVelocity;
            if (stage == 2)
                passed = lesson.ContactChallengeSatisfied && selector.Ball.CurrentPolarity == Polarity.Positive;
            if (stage == 3)
            {
                var launcher = selector.GetStageComponent<DistanceLauncher2D>(3);
                passed = launcher.HasLaunchSample && lesson.HasBaseline && lesson.HasResult && lesson.ComparisonChallengeSatisfied
                    && selector.Ball.Body.linearVelocity.sqrMagnitude > 0.01f
                    && lesson.ResultSample.ForceMagnitude > lesson.BaselineSample.ForceMagnitude;
                observed += ", baseline force=" + lesson.BaselineSample.ForceMagnitude + ", result=" + lesson.ResultSample.ForceMagnitude;
            }
            if (stage == 4)
                passed = lesson.NegativeChallengeSatisfied && Vector2.Dot(selector.Ball.Body.linearVelocity, fieldForce) > 0.01f;
            if (stage == 5)
            {
                var launcher = selector.GetStageComponent<VoltageLauncher2D>(5);
                passed = launcher.HasLaunchSample && lesson.VoltageChallengeSatisfied && launcher.LastLaunchVoltage >= 50f
                    && selector.Ball.Body.linearVelocity.y > 0f && launcher.Voltage < 1f;
                observed += ", consumed voltage=" + launcher.LastLaunchVoltage;
            }
            Add(stage, "real-fixedupdates-complete-stage-mechanism", passed,
                observed + "; " + lesson.LearningGoalProgress + "; " + lesson.LastExplanation);
        }

        private static void ObserveMotions(ElectricStageSelector selector)
        {
            motions.Clear();
            float observationDuration = 0.5f;
            foreach (ElectricObstacleMotion2D motion in selector.CurrentStageRoot.GetComponentsInChildren<ElectricObstacleMotion2D>())
            {
                motions.Add(new MotionProbe { component = motion, start = motion.GetComponent<Rigidbody2D>().position });
                var serialized = new SerializedObject(motion);
                observationDuration = Mathf.Max(observationDuration, serialized.FindProperty("startDelay").floatValue
                    + serialized.FindProperty("startPause").floatValue + serialized.FindProperty("oneWayDuration").floatValue + 0.2f);
            }
            motionDeadline = Time.unscaledTime + observationDuration;
        }

        private static void CaptureScreenshot(int stage)
        {
            Report report = Load();
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                report.skipped.Add("Stage " + stage + " screenshot: batch editor was launched without a graphics device.");
            else
            {
                string path = ScreenshotPath(stage);
                if (File.Exists(path)) File.Delete(path);
                SessionState.SetFloat(Prefix + "CaptureStarted", (float)EditorApplication.timeSinceStartup);
                SessionState.SetInt(Prefix + "CaptureFrame", Time.renderedFrameCount);
                ScreenCapture.CaptureScreenshot(path);
                report.screenshots.Add(path);
            }
            Save(report);
        }

        private static string ScreenshotPath(int stage) => Path.Combine(ProjectFolder, "Logs/StageModules_Stage" + stage.ToString("00") + ".png");

        private static bool WaitForScreenshot(int stage, ElectricStageSelector selector)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return true;
            string path = ScreenshotPath(stage);
            if (File.Exists(path) && new FileInfo(path).Length > 0
                && Time.renderedFrameCount >= SessionState.GetInt(Prefix + "CaptureFrame", 0) + 3)
            {
                Report report = Load();
                report.screenshotNotes.Add("Stage " + stage + ": actual 16:9 Game View capture, " + Screen.width + " x " + Screen.height + ".");
                Save(report);
                return true;
            }
            if (EditorApplication.timeSinceStartup - SessionState.GetFloat(Prefix + "CaptureStarted", 0f) < 3d) return false;
            CaptureCameraAndUI(selector, path);
            Report fallbackReport = Load();
            fallbackReport.screenshotNotes.Add("Stage " + stage
                + ": camera render with overlay Canvases temporarily rendered by the scene camera; world and Canvas UI included, OnGUI excluded. All Canvas modes restored; no scene saved.");
            Save(fallbackReport);
            return true;
        }

        private static void CaptureCameraAndUI(ElectricStageSelector selector, string path)
        {
            Camera camera = Camera.main;
            Require(camera != null, "A scene camera is required for the Canvas-inclusive render fallback.");
            var converted = new List<OverlayCanvasState>();
            try
            {
                foreach (Canvas canvas in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                {
                    if (!canvas.isActiveAndEnabled || canvas.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                    converted.Add(new OverlayCanvasState { canvas = canvas, camera = canvas.worldCamera, planeDistance = canvas.planeDistance });
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = camera;
                    canvas.planeDistance = camera.nearClipPlane + 0.5f;
                }
                Canvas.ForceUpdateCanvases();
                Require(ElectricLearningVisualCheck.CaptureWorld(selector, path, 1600, 900), "Canvas-inclusive fallback produced no PNG.");
            }
            finally
            {
                foreach (OverlayCanvasState state in converted)
                {
                    if (state.canvas == null) continue;
                    state.canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                    state.canvas.worldCamera = state.camera;
                    state.canvas.planeDistance = state.planeDistance;
                }
                Canvas.ForceUpdateCanvases();
            }
        }

        private static void Click(Button button)
        {
            Require(button != null && button.IsActive() && button.IsInteractable() && EventSystem.current != null,
                "Close_Button cannot receive an actual UI click.");
            ExecuteEvents.Execute(button.gameObject, new PointerEventData(EventSystem.current)
                { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);
        }

        private static float PanelWait(CollapsiblePanel panel) => Mathf.Max(0.1f, new SerializedObject(panel).FindProperty("duration").floatValue + 0.15f);
        private static void Advance(Phase phase, float delay)
        {
            SessionState.SetInt(Prefix + "Phase", (int)phase);
            SessionState.SetFloat(Prefix + "Deadline", Time.unscaledTime + delay);
        }

        private static T Reference<T>(Component component, string field) where T : Object =>
            component != null ? new SerializedObject(component).FindProperty(field)?.objectReferenceValue as T : null;

        private static List<T> FindAll<T>(Scene scene) where T : Component
        {
            var found = new List<T>();
            foreach (GameObject root in scene.GetRootGameObjects()) found.AddRange(root.GetComponentsInChildren<T>(true));
            return found;
        }

        private static RectTransform FindNamedRect(Canvas canvas, string name)
        {
            foreach (RectTransform rect in canvas.GetComponentsInChildren<RectTransform>(true))
                if (rect.name.Trim() == name) return rect;
            return null;
        }

        private static bool PrefabGeometryIsPortable(Component instance, string path, out string explanation)
        {
            GameObject asset = !string.IsNullOrEmpty(path) ? AssetDatabase.LoadAssetAtPath<GameObject>(path) : null;
                                                                                                                        
                                                                                                
            Component source = !string.IsNullOrEmpty(path) ? PrefabUtility.GetCorrespondingObjectFromSourceAtPath(instance, path) : null;
            explanation = "portable geometry and unassigned scene participants";
            if (asset == null || source == null)
            {
                explanation = "missing asset/source at " + path;
                return false;
            }
            var serialized = new SerializedObject(source);
            if (instance is ContactCharger2D || instance is DistanceLauncher2D || instance is UniformField2D || instance is VoltageLauncher2D)
                if (serialized.FindProperty("ball").objectReferenceValue != null
                    || serialized.FindProperty("lever").objectReferenceValue != null)
                {
                    explanation = "asset retains ball/lever references: " + AssetDatabase.GetAssetPath(source);
                    return false;
                }
            string[] fields = instance is ContactCharger2D ? new[] { "restPoint", "contactPoint" }
                : instance is DistanceLauncher2D ? new[] { "source", "farPoint", "nearPoint" }
                : instance is UniformField2D ? new[] { "lowerPlate", "upperPlate" }
                : instance is VoltageLauncher2D ? new[] { "launchZone", "lowerPlate", "upperPlate" }
                : instance is CollapsiblePanel ? new[] { "content", "toggleButton" } : Array.Empty<string>();
            foreach (string field in fields)
            {
                Component component = serialized.FindProperty(field)?.objectReferenceValue as Component;
                if (component == null || (component.transform != asset.transform && !component.transform.IsChildOf(asset.transform)))
                {
                    explanation = "field " + field + " is " + (component == null ? "unassigned" : "outside " + path
                        + " (source=" + AssetDatabase.GetAssetPath(source) + ", target=" + AssetDatabase.GetAssetPath(component) + ")");
                    return false;
                }
            }
            return true;
        }

        private static RectReference Snapshot(string name, RectTransform rect) => new RectReference
        {
            name = name, anchorMin = rect.anchorMin, anchorMax = rect.anchorMax, pivot = rect.pivot,
            position = rect.anchoredPosition, size = rect.sizeDelta, scale = rect.localScale
        };

        private static bool SameLayout(RectReference expected, RectTransform rect) =>
            (expected.anchorMin - rect.anchorMin).sqrMagnitude < 0.0001f
            && (expected.anchorMax - rect.anchorMax).sqrMagnitude < 0.0001f
            && (expected.pivot - rect.pivot).sqrMagnitude < 0.0001f
            && (expected.position - rect.anchoredPosition).sqrMagnitude < 0.01f
            && (expected.size - rect.sizeDelta).sqrMagnitude < 0.01f
            && (expected.scale - rect.localScale).sqrMagnitude < 0.0001f;

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
        private static void RecordError(string message)
        {
            Report report = Load();
            report.errors.Add(message);
            Save(report);
        }
        private static void Add(int stage, string name, bool passed, string details)
        {
            Report report = Load();
            report.checks.Add(new Check { stage = stage, name = name, passed = passed, details = details });
            Save(report);
        }

        private static void Finish()
        {
            Time.timeScale = 1f;
            Report report = Load();
            foreach (string screenshot in report.screenshots)
                report.checks.Add(new Check { stage = 0, name = "play-mode-screenshot-saved", passed = File.Exists(screenshot)
                    && new FileInfo(screenshot).Length > 0, details = screenshot });
            bool passed = report.errors.Count == 0 && report.checks.Count >= 40;
            foreach (Check check in report.checks) passed &= check.passed;
            report.status = passed ? "passed" : "failed";
            Save(report);
            SessionState.SetBool(Prefix + "Active", false);
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            Debug.Log("Stage module validation: " + report.status + ". Report: " + ReportPath);
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
            EditorApplication.delayCall += () => EditorApplication.Exit(passed ? 0 : 1);
        }
    }
}
