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
using Object = UnityEngine.Object;

namespace sung_wook
{
    // Temporary focused harness. Copy only into an isolated project's Assets/Editor.
    // Fixture poses/gravity are intentionally controlled; the independent route checks
    // exercise the authored courses without these fixture changes. No source assets save.
    [InitializeOnLoad]
    public static class SpaceActualPhysicsCheck
    {
        private const string Prefix = "sung_wook.SpaceActualPhysicsCheck.";
        private const string ScenePath = "Assets/@Project/@sung_wook/@Game/00_Scenes/Stage/Stage_02.unity";
        private const string ReportPath = "C:/Users/wook1/GitBub-Unity/Science-Project-2026/Logs/SpaceActualPhysicsValidation.json";
        private const string PlusPath = "Assets/@Project/@sung_wook/@Game/05_Texture/Plus.aseprite";
        private const string MinusPath = "Assets/@Project/@sung_wook/@Game/05_Texture/Minus.aseprite";
        private const string NeutralGuid = "a86470a33a6bf42c4b3595704624658b";
        private const float Distance = 2f;
        private const double CoulombConstant = 8.9875517923e9;
        private static readonly Vector2 SourcePose = new Vector2(0f, 6f);
        private static readonly MethodInfo ControllerUpdate = typeof(PlayerController).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);

        [Serializable] private sealed class Check
        {
            public string name, details;
            public bool passed;
        }

        [Serializable] private sealed class Sample
        {
            public string polarity;
            public float signedCharge, chargeMagnitude, sourceCharge, distanceMeters;
            public Vector2 appliedForceNewtons;
            public float expectedForceNewtons, fieldNewtonsPerCoulomb;
            public bool usesSI, inRange, limited;
        }

        [Serializable] private sealed class Report
        {
            public string status = "running";
            public string scene = ScenePath;
            public string method = "Saved Stage_02; actual virtual Keyboard events -> PlayerInputReader -> PlayerController.Update; normal LateUpdate sprite fade and normal FixedUpdate force application. Controlled separated fixture is distinct from route tests. No assets or scenes saved.";
            public List<Check> checks = new List<Check>();
            public List<Sample> samples = new List<Sample>();
            public List<string> errors = new List<string>();
            public int contactEvents, starEvents, goalEvents;
            public float activeSeconds;
        }

        private enum Phase { Startup, PositiveFrames, NegativeFrames, NeutralFrames }
        private static Report report;
        private static Phase phase;
        private static ElectricStageSelector selector;
        private static ElectricLearningController2D lesson;
        private static ElectricBall2D ball;
        private static PlayerMovement movement;
        private static PlayerController controller;
        private static PlayerInputReader reader;
        private static ChangePlayerVisual visual;
        private static ContactCharger2D charger;
        private static ElectricStar2D star;
        private static ElectricGoal2D goal;
        private static SpriteRenderer overlay, body;
        private static Sprite positive, negative;
        private static Rigidbody2D electrodeBody;
        private static Keyboard keyboard, previousKeyboard;
        private static InputSettings.UpdateMode previousUpdateMode;
        private static InputSettings.BackgroundBehavior previousBackgroundBehavior;
        private static InputSettings.EditorInputBehaviorInPlayMode previousEditorInputBehavior;
        private static bool previousRunInBackground;
        private static bool inputConfigured, finished, midFadeSeen, negativeTextureSeen;
        private static int startupFrames;
        private static float started, phaseStarted, phasePhysics, previousMoveSpeed, previousGravity;
        private static RigidbodyConstraints2D previousConstraints;
        private static int initialSpriteRendererCount, contactEvents, starEvents, goalEvents;

        static SpaceActualPhysicsCheck()
        {
            if (Application.isBatchMode && SessionState.GetBool(Prefix + "Active", false)) Resume();
        }

        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use only a separately launched isolated Unity batch editor.");
            report = new Report();
            finished = false;
            SessionState.SetBool(Prefix + "Active", true);
            SessionState.SetString(Prefix + "Started", DateTime.UtcNow.ToString("O"));
            WriteReport();
            try
            {
                Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                InspectAuthoredReferences(scene);
                Resume();
                ElectricBatchPlayMode.EnterAfterEditorStartup();
                // This update fallback also works when unrelated Search initialization
                // prevents Unity's delayCall queue from draining in a batch editor.
                EditorApplication.update -= EnterFromEditorUpdate;
                EditorApplication.update += EnterFromEditorUpdate;
            }
            catch (Exception exception)
            {
                Error(exception);
                Finish();
            }
        }

        private static void Resume()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
        }

        private static void EnterFromEditorUpdate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.update -= EnterFromEditorUpdate;
                return;
            }
            if (!Application.isBatchMode || EditorApplication.isCompiling || EditorApplication.isUpdating || ++startupFrames < 3) return;
            EditorApplication.update -= EnterFromEditorUpdate;
            EditorApplication.EnterPlaymode();
        }

        private static void InspectAuthoredReferences(Scene scene)
        {
            var players = All<Player>(scene);
            Require(players.Count == 1, "Saved Stage_02 must contain exactly one player.");
            var authored = players[0].GetComponent<ChangePlayerVisual>();
            Require(authored != null, "Saved player is missing ChangePlayerVisual.");
            SpriteRenderer savedOverlay = Reference<SpriteRenderer>(authored, "chargeRenderer");
            GameObject savedNeutral = Reference<GameObject>(authored, "existingNeutralVisual");
            Transform authoredVisual = players[0].transform.Find("Visual");
            CheckResult("saved-player-has-authored-overlay", savedOverlay != null && savedNeutral != null
                && authoredVisual != null && savedNeutral == authoredVisual.gameObject
                && savedOverlay.transform.parent == authoredVisual,
                "chargeRenderer and existingNeutralVisual point to the saved Player/Visual hierarchy.");
            Require(savedOverlay != null && authoredVisual != null, "Authored player overlay cannot be inspected.");
            SpriteRenderer savedBody = authoredVisual.GetComponent<SpriteRenderer>();
            CheckResult("saved-player-neutral-and-sprites-are-original-assets", savedBody != null
                && AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(savedBody.sprite)) == NeutralGuid
                && AssetDatabase.GetAssetPath(Reference<Sprite>(authored, "positiveSprite")) == PlusPath
                && AssetDatabase.GetAssetPath(Reference<Sprite>(authored, "negativeSprite")) == MinusPath
                && AssetDatabase.GetAssetPath(savedOverlay.sprite) == PlusPath,
                "Original Circle neutral sprite and the existing 05_Texture/Plus and Minus imported sprites.");
            CheckResult("saved-overlay-starts-hidden-at-identity", !savedOverlay.enabled
                && Mathf.Abs(savedOverlay.color.a) < 0.00001f
                && savedOverlay.transform.localPosition.sqrMagnitude < 0.00001f
                && (savedOverlay.transform.localScale - Vector3.one).sqrMagnitude < 0.00001f
                && Quaternion.Angle(savedOverlay.transform.localRotation, Quaternion.identity) < 0.001f
                && savedOverlay.sharedMaterial == savedBody.sharedMaterial
                && savedOverlay.sortingLayerID == savedBody.sortingLayerID
                && savedOverlay.sortingOrder == savedBody.sortingOrder + 1,
                "Existing renderer material, body layer and body order + 1; no runtime overlay construction required.");

            var chargers = All<ContactCharger2D>(scene);
            Require(chargers.Count == 1, "Saved Stage_02 must contain one ContactCharger2D.");
            var stationVisual = chargers[0].GetComponent<ContactChargeVisual2D>();
            Require(stationVisual != null, "Saved station visual is missing.");
            CheckResult("saved-station-visual-references", Reference<ContactCharger2D>(stationVisual, "charger") == chargers[0]
                && Reference<SpriteRenderer>(stationVisual, "electrodeRenderer") != null
                && Reference<SpriteRenderer>(stationVisual, "chargeRenderer") != null
                && AssetDatabase.GetAssetPath(Reference<Sprite>(stationVisual, "positiveSprite")) == PlusPath
                && AssetDatabase.GetAssetPath(Reference<Sprite>(stationVisual, "negativeSprite")) == MinusPath,
                "Saved station has authored body/overlay renderers and original charge sprites.");

            var leverVisuals = All<ChangeGearLeverVisual>(scene);
            bool leverReferences = leverVisuals.Count > 0;
            foreach (ChangeGearLeverVisual leverVisual in leverVisuals)
                leverReferences &= Reference<SpriteRenderer>(leverVisual, "bodyRenderer") != null
                    && Reference<SpriteRenderer>(leverVisual, "chargeRenderer") != null
                    && AssetDatabase.GetAssetPath(Reference<Sprite>(leverVisual, "positiveSprite")) == PlusPath
                    && AssetDatabase.GetAssetPath(Reference<Sprite>(leverVisual, "negativeSprite")) == MinusPath;
            var levers = All<StageLever2D>(scene);
            CheckResult("saved-lever-has-authored-charge-renderer", leverReferences && levers.Count == 1
                && levers[0].Reader != null, leverVisuals.Count + " saved lever charge visual(s), " + levers.Count + " StageLever2D.");
        }

        private static List<T> All<T>(Scene scene) where T : Component
        {
            var result = new List<T>();
            foreach (GameObject root in scene.GetRootGameObjects()) result.AddRange(root.GetComponentsInChildren<T>(true));
            return result;
        }

        private static T Reference<T>(Object owner, string name) where T : Object
        {
            if (owner == null) return null;
            SerializedProperty property = new SerializedObject(owner).FindProperty(name);
            return property != null ? property.objectReferenceValue as T : null;
        }

        private static void Tick()
        {
            if (!Application.isBatchMode || !SessionState.GetBool(Prefix + "Active", false) || finished) return;
            try
            {
                EnsureReport();
                if ((DateTime.UtcNow - DateTime.Parse(SessionState.GetString(Prefix + "Started", ""))).TotalSeconds > 150)
                    throw new TimeoutException("Space actual physics watchdog exceeded 150 seconds.");
                if (!EditorApplication.isPlaying || EditorApplication.isPaused || SceneManager.GetActiveScene().path != ScenePath) return;
                if (selector == null)
                {
                    selector = Object.FindFirstObjectByType<ElectricStageSelector>();
                    if (selector == null || selector.GetComponent<ElectricStageSession2D>()?.IsInitialized != true)
                    {
                        selector = null;
                        return;
                    }
                    Begin();
                    return;
                }
                report.activeSeconds = Time.unscaledTime - started;
                float alpha = overlay.color.a;
                if (alpha > 0.001f && alpha < 0.999f) midFadeSeen = true;
                if (overlay.enabled && overlay.sprite == negative) negativeTextureSeen = true;
                // Observe natural player-loop frames. Body position control occurs only
                // between phases, never while checking the force-driven velocity.
                float phaseGameSeconds = Time.time - phaseStarted;
                bool settled = phase == Phase.PositiveFrames
                    ? overlay.enabled && overlay.sprite == positive && alpha >= 0.99f
                    : phase == Phase.NegativeFrames
                        ? overlay.enabled && overlay.sprite == negative && alpha >= 0.99f
                        : !overlay.enabled && alpha <= 0.001f;
                // The visual integrates Time.deltaTime, so initial batch-editor stalls
                // must not consume its settling allowance in unscaled/editor time.
                // Keep observing intermediate alpha until the actual target is visible,
                // then advance after at least 0.3 gameplay seconds (3 s timeout).
                if (phaseGameSeconds < 0.3f || Time.fixedTime - phasePhysics < 0.06f) return;
                if (!settled && phaseGameSeconds < 3f) return;
                if (phase == Phase.PositiveFrames)
                {
                    CheckResult("space-positive-fades-through-intermediate-alpha", midFadeSeen,
                        "Natural LateUpdate alpha entered (0,1) after Space selected positive charge.");
                    CheckResult("space-positive-settles-to-authored-plus", overlay.enabled && overlay.sprite == positive && alpha >= 0.99f,
                        "alpha=" + alpha + ", sprite=" + AssetDatabase.GetAssetPath(overlay.sprite));
                    CheckResult("space-positive-applies-real-repulsive-velocity", ball.Body.linearVelocity.x > 0.01f,
                        "Normal FixedUpdate velocity=" + ball.Body.linearVelocity);
                    NoProgressSideEffects("positive");
                    ResetSeparatedFixture();
                    PressSpace(PlayerStateType.Negative, -1f, 1f, "negative");
                    CaptureForceSample("negative", -1f);
                    CaptureOneMeterSample("negative-1m", -1f);
                    BeginPhase(Phase.NegativeFrames);
                }
                else if (phase == Phase.NegativeFrames)
                {
                    CheckResult("space-negative-fades-via-neutral", midFadeSeen && negativeTextureSeen,
                        "Natural LateUpdate passed intermediate alpha and selected Minus after the sign changed.");
                    CheckResult("space-negative-settles-to-authored-minus", overlay.enabled && overlay.sprite == negative && alpha >= 0.99f,
                        "alpha=" + alpha + ", sprite=" + AssetDatabase.GetAssetPath(overlay.sprite));
                    CheckResult("space-negative-applies-real-attractive-velocity", ball.Body.linearVelocity.x < -0.01f,
                        "Normal FixedUpdate velocity=" + ball.Body.linearVelocity);
                    NoProgressSideEffects("negative");
                    ResetSeparatedFixture();
                    PressSpace(PlayerStateType.Neutral, 0f, 0f, "neutral");
                    CaptureForceSample("neutral", 0f);
                    CaptureOneMeterSample("neutral-1m", 0f);
                    BeginPhase(Phase.NeutralFrames);
                }
                else if (phase == Phase.NeutralFrames)
                {
                    CheckResult("space-neutral-fades-back-to-original-body", midFadeSeen && !overlay.enabled && alpha <= 0.001f
                        && body.enabled && AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(body.sprite)) == NeutralGuid,
                        "alpha=" + alpha + ", overlay.enabled=" + overlay.enabled + ", neutral body visible.");
                    bool labels = true;
                    foreach (TMPro.TMP_Text label in body.GetComponentsInChildren<TMPro.TMP_Text>(true)) labels &= label.enabled;
                    CheckResult("neutral-labels-return", labels, "Saved neutral labels are visible after neutral fade completes.");
                    CheckResult("space-neutral-has-no-electric-acceleration", ball.Body.linearVelocity.sqrMagnitude < 0.000001f,
                        "Normal FixedUpdate velocity=" + ball.Body.linearVelocity);
                    CheckResult("no-runtime-player-art-created", visual.transform.Find("Visual").GetComponentsInChildren<SpriteRenderer>(true).Length == initialSpriteRendererCount,
                        "Player/Visual renderer count stayed at saved count " + initialSpriteRendererCount + ".");
                    NoProgressSideEffects("neutral");
                    Finish();
                }
            }
            catch (Exception exception)
            {
                Error(exception);
                Finish();
            }
        }

        private static void Begin()
        {
            Time.timeScale = 1f;
            selector.RestartStage();
            ball = selector.Ball;
            movement = ball.GetComponent<PlayerMovement>();
            controller = ball.GetComponent<PlayerController>();
            reader = ball.GetComponent<PlayerInputReader>();
            visual = ball.GetComponent<ChangePlayerVisual>();
            charger = selector.GetStageComponent<ContactCharger2D>(2);
            star = selector.GetStageComponent<ElectricStar2D>(2);
            goal = selector.GetStageComponent<ElectricGoal2D>(2);
            lesson = selector.GetComponent<ElectricLearningController2D>();
            Require(ball != null && movement != null && controller != null && reader != null && visual != null
                && charger != null && star != null && goal != null && lesson != null && ControllerUpdate != null,
                "Actual scene participants and controller input method are required.");
            overlay = Reference<SpriteRenderer>(visual, "chargeRenderer");
            positive = Reference<Sprite>(visual, "positiveSprite");
            negative = Reference<Sprite>(visual, "negativeSprite");
            body = ball.transform.Find("Visual").GetComponent<SpriteRenderer>();
            Require(overlay != null && body != null && positive != null && negative != null, "Authored player renderers missing in Play Mode.");
            initialSpriteRendererCount = ball.transform.Find("Visual").GetComponentsInChildren<SpriteRenderer>(true).Length;
            CheckResult("initial-neutral-has-zero-signed-charge", ball.Player.CurrentState == PlayerStateType.Neutral && Mathf.Abs(ball.SignedCharge) < 0.000001f,
                "state=" + ball.Player.CurrentState + ", signed charge=" + ball.SignedCharge + ", stored magnitude=" + ball.ChargeMagnitude);
            CheckResult("actual-player-input-components-enabled", controller.isActiveAndEnabled && reader.isActiveAndEnabled && ball.Player.isActiveAndEnabled,
                "Uses the actual saved PlayerController and PlayerInputReader.");
            CheckResult("runtime-station-bound-to-current-player-and-lever", Reference<ElectricBall2D>(charger, "ball") == ball
                && Reference<StageLever2D>(charger, "lever") == selector.Lever,
                "The loaded ContactChargingStation observes the loaded Player and Lever.");

            previousKeyboard = Keyboard.current;
            previousUpdateMode = InputSystem.settings.updateMode;
            previousBackgroundBehavior = InputSystem.settings.backgroundBehavior;
            previousEditorInputBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            previousRunInBackground = Application.runInBackground;
            Application.runInBackground = true;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
            keyboard = InputSystem.AddDevice<Keyboard>("Space actual physics validation keyboard");
            keyboard.MakeCurrent();
            inputConfigured = true;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            InputSystem.Update();

            electrodeBody = charger.GetComponent<Rigidbody2D>();
            var stationSettings = new SerializedObject(charger);
            previousMoveSpeed = stationSettings.FindProperty("moveSpeed").floatValue;
            stationSettings.FindProperty("moveSpeed").floatValue = 0f;
            stationSettings.ApplyModifiedPropertiesWithoutUndo();
            previousGravity = ball.Body.gravityScale;
            previousConstraints = ball.Body.constraints;
            ball.Body.gravityScale = 0f;
            ball.Body.constraints = RigidbodyConstraints2D.FreezePositionY | RigidbodyConstraints2D.FreezeRotation;
            selector.Lever.SetSignedValue(1f);
            charger.ChargedFromNeutral += OnContact;
            star.OnCollected.AddListener(OnStar);
            goal.OnCleared.AddListener(OnGoal);
            started = Time.unscaledTime;
            ResetSeparatedFixture();
            CaptureForceSample("initial-neutral", 0f);
            NoProgressSideEffects("initial");
            PressSpace(PlayerStateType.Positive, 1f, 1f, "positive");
            CaptureForceSample("positive", 1f);
            CaptureOneMeterSample("positive-1m", 1f);
            BeginPhase(Phase.PositiveFrames);
        }

        private static void ResetSeparatedFixture(float fixtureDistance = Distance)
        {
            electrodeBody.position = SourcePose;
            movement.ResetMotion();
            ball.Body.position = electrodeBody.worldCenterOfMass + Vector2.right * fixtureDistance;
            Physics2D.SyncTransforms();
            // Correct for any authored center-of-mass offset without changing colliders.
            ball.Body.position += electrodeBody.worldCenterOfMass + Vector2.right * fixtureDistance - ball.Body.worldCenterOfMass;
            Physics2D.SyncTransforms();
        }

        private static void PressSpace(PlayerStateType expectedState, float expectedSigned, float expectedMagnitude, string label)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Space));
            InputSystem.Update();
            CheckResult("space-" + label + "-reaches-actual-input-reader", reader.StateChangePressed && Keyboard.current == keyboard,
                "Virtual Keyboard.spaceKey.wasPressedThisFrame was observed by PlayerInputReader.");
            ControllerUpdate.Invoke(controller, null);
            CheckResult("space-" + label + "-changes-real-charge-immediately", ball.Player.CurrentState == expectedState
                && Mathf.Abs(ball.SignedCharge - expectedSigned) < 0.000001f
                && Mathf.Abs(ball.ChargeMagnitude - expectedMagnitude) < 0.000001f,
                "state=" + ball.Player.CurrentState + ", signed charge=" + ball.SignedCharge + ", magnitude=" + ball.ChargeMagnitude);
            // Advance an input step while held, as the next ordinary Update would see it.
            InputSystem.Update();
            bool heldHasNoNewEdge = !reader.StateChangePressed;
            ControllerUpdate.Invoke(controller, null);
            CheckResult("space-" + label + "-hold-does-not-cycle-again", heldHasNoNewEdge && ball.Player.CurrentState == expectedState
                && Mathf.Abs(ball.SignedCharge - expectedSigned) < 0.000001f,
                "A held key produces no second press edge.");
            // Release before yielding so the normal controller Update cannot replay a test edge.
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            InputSystem.Update();
        }

        private static void CaptureOneMeterSample(string label, float expectedSign)
        {
            // Measure before yielding and return to the wider fixture before physics:
            // the 1 m attraction probe cannot accidentally produce contact charging.
            ResetSeparatedFixture(1f);
            CaptureForceSample(label, expectedSign, 1f);
            ResetSeparatedFixture();
        }

        private static void CaptureForceSample(string label, float expectedSign, float sampleDistance = Distance)
        {
            Require(charger.TryGetSample(out ElectricForceSample sample), "Loaded station refused a force sample.");
            double expected = CoulombConstant * 0.00003 * 0.00003 / (sampleDistance * sampleDistance) * expectedSign;
            report.samples.Add(new Sample
            {
                polarity = label, signedCharge = sample.SignedCharge, chargeMagnitude = ball.ChargeMagnitude,
                sourceCharge = sample.SourceCharge, distanceMeters = sample.DistanceMeters,
                appliedForceNewtons = sample.ForceNewtons, expectedForceNewtons = (float)expected,
                fieldNewtonsPerCoulomb = sample.FieldNewtonsPerCoulomb.magnitude,
                usesSI = sample.UsesSIUnits, inRange = sample.IsInRange, limited = sample.IsLimited
            });
            CheckResult(label + "-force-obeys-si-coulomb-law-at-same-distance", sample.UsesSIUnits && sample.IsInRange
                && !sample.IsLimited && Mathf.Abs(sample.DistanceMeters - sampleDistance) < 0.00001f
                && Mathf.Abs(sample.SourceCharge - 1f) < 0.000001f
                && Mathf.Abs(sample.ForceNewtons.x - (float)expected) < 0.0001f
                && Mathf.Abs(sample.ForceNewtons.y) < 0.00001f
                && Mathf.Abs(sample.SignedChargeCoulombs - expectedSign * 0.00003f) < 0.000000001f
                && sample.FieldNewtonsPerCoulomb.x > 0f,
                "r=" + sample.DistanceMeters + " m, q=" + sample.SignedChargeCoulombs
                + " C, source=" + sample.SourceChargeCoulombs + " C, F=" + sample.ForceNewtons
                + " N, expected signed x=" + expected + " N (8.0887966 / r^2).");
            Vector2 beforeVelocity = ball.Body.linearVelocity;
            float beforeCharge = ball.SignedCharge;
            charger.TryGetSample(out ElectricForceSample repeated);
            CheckResult(label + "-sampling-is-read-only", (beforeVelocity - ball.Body.linearVelocity).sqrMagnitude < 0.000001f
                && beforeCharge == ball.SignedCharge && (sample.Force - repeated.Force).sqrMagnitude < 0.000001f,
                "Repeated measurement leaves player velocity, charge and force unchanged.");
            WriteReport();
        }

        private static void BeginPhase(Phase next)
        {
            phase = next;
            phaseStarted = Time.time;
            phasePhysics = Time.fixedTime;
            midFadeSeen = false;
            negativeTextureSeen = false;
        }

        private static void NoProgressSideEffects(string label)
        {
            CheckResult(label + "-space-does-not-grant-learning-star-or-goal", contactEvents == 0 && starEvents == 0 && goalEvents == 0
                && !lesson.ContactChallengeSatisfied && !lesson.CurrentLearningGoalSatisfied && ball.Stars == 0
                && !star.IsCollected && !goal.IsCleared,
                "contact=" + contactEvents + ", star=" + starEvents + ", goal=" + goalEvents
                + ", learning=" + lesson.CurrentLearningGoalSatisfied + ", collected stars=" + ball.Stars);
        }

        private static void OnContact(ElectricBall2D _) => contactEvents++;
        private static void OnStar() => starEvents++;
        private static void OnGoal() => goalEvents++;

        private static void CheckResult(string name, bool passed, string details)
        {
            EnsureReport();
            report.checks.Add(new Check { name = name, passed = passed, details = details });
            WriteReport();
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void EnsureReport()
        {
            if (report == null) report = File.Exists(ReportPath) ? JsonUtility.FromJson<Report>(File.ReadAllText(ReportPath)) : new Report();
        }

        private static void WriteReport()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, JsonUtility.ToJson(report, true));
        }

        private static void OnLog(string message, string stack, LogType type)
        {
            if (finished || (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)) return;
            EnsureReport();
            report.errors.Add(message + "\n" + stack);
            WriteReport();
        }

        private static void Error(Exception exception)
        {
            EnsureReport();
            report.errors.Add(exception.ToString());
            WriteReport();
        }

        private static void Finish()
        {
            if (finished) return;
            finished = true;
            EnsureReport();
            report.contactEvents = contactEvents;
            report.starEvents = starEvents;
            report.goalEvents = goalEvents;
            if (ball != null)
            {
                ball.Body.gravityScale = previousGravity;
                ball.Body.constraints = previousConstraints;
            }
            if (charger != null)
            {
                charger.ChargedFromNeutral -= OnContact;
                var settings = new SerializedObject(charger);
                settings.FindProperty("moveSpeed").floatValue = previousMoveSpeed;
                settings.ApplyModifiedPropertiesWithoutUndo();
            }
            if (inputConfigured)
            {
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                if (previousKeyboard != null && previousKeyboard.added) previousKeyboard.MakeCurrent();
                InputSystem.settings.updateMode = previousUpdateMode;
                InputSystem.settings.backgroundBehavior = previousBackgroundBehavior;
                InputSystem.settings.editorInputBehaviorInPlayMode = previousEditorInputBehavior;
                Application.runInBackground = previousRunInBackground;
            }
            report.status = report.errors.Count == 0 && report.checks.Count > 0 && report.checks.TrueForAll(check => check.passed)
                ? "passed" : "failed";
            WriteReport();
            SessionState.SetBool(Prefix + "Active", false);
            EditorApplication.update -= Tick;
            EditorApplication.update -= EnterFromEditorUpdate;
            Application.logMessageReceived -= OnLog;
            EditorApplication.Exit(report.status == "passed" ? 0 : 1);
        }
    }
}
