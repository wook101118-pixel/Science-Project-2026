// Temporary standalone-only fixtures. Copy ONLY into the isolated clone's Assets.
// Requires --qa-gameplay-improvements OR --qa-gameplay-cards, plus absolute report/backup paths.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using sung_wook;
using Object = UnityEngine.Object;

public sealed class QaGameplayImprovementProbe : MonoBehaviour
{
    [Serializable] private sealed class Check { public string name, details; public bool passed; }
    [Serializable] private sealed class Layout { public string label, text; public bool overflow; public float fontSize, x, y, width, height; }
    [Serializable] private sealed class Report
    {
        public string status = "running", startedUtc, finishedUtc, unity, platform, graphics;
        public string scope = "Windows standalone authored UI callbacks, actual screenshots, virtual InputSystem keyboard edges delivered to real input Update methods, public lesson commands, and isolated physics fixtures. This is not a no-teleport route playthrough or physical OS keyboard test. The separate QaPlayerRouteProbe owns authored default campaign routes.";
        public string fixtures = "Stage 3 rejection fixtures may reposition/freeze the saved ball. Stage 4 position/velocity/stars are seeded to verify preservation; ascent is obtained through real field physics. A temporary non-rendered far-away floor tests actual collision events, not fake event invocation. Brake/momentum measurements isolate the saved player with gravity disabled and no obstacles. No artwork, sprite, or color changes.";
        public bool preferencesRestored;
        public float brakeSeconds, brakeDistance, reverseZeroSeconds, preservedImpulseSpeed;
        public List<Check> checks = new List<Check>();
        public List<Layout> layouts = new List<Layout>();
        public List<string> screenshots = new List<string>(), errors = new List<string>();
    }
    [Serializable] private sealed class Backup
    {
        public string company, product;
        public bool hasWidth, hasHeight, hasFullscreen, hasVolume, hasMute, restored;
        public int width, height, fullscreen, mute;
        public float volume;
    }
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private Report report;
    private Backup backup;
    private string reportPath, backupPath;
    private Keyboard keyboard, oldKeyboard;
    private InputSettings.UpdateMode oldUpdate;
    private InputSettings.BackgroundBehavior oldBackground;
    private ElectricStageSelector selector;
    private ElectricLearningController2D lesson;
    private ElectricStageSession2D session;
    private ElectricStageHudView hud;
    private ElectricBall2D ball;
    private PlayerMovement movement;
    private float started;
    private bool finishing, initialized, cardsOnly;
    private GameObject fixtureFloor;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        bool cards = Array.IndexOf(Environment.GetCommandLineArgs(), "--qa-gameplay-cards") >= 0;
        if (Application.isEditor || (!cards && Array.IndexOf(Environment.GetCommandLineArgs(), "--qa-gameplay-improvements") < 0)) return;
        string path = Arg("--qa-report="), backup = Arg("--qa-backup=");
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path) || string.IsNullOrWhiteSpace(backup) || !Path.IsPathRooted(backup)) return;
        var root = new GameObject("Temporary Gameplay Improvements QA");
        DontDestroyOnLoad(root);
        root.AddComponent<QaGameplayImprovementProbe>().Initialize(path, backup, cards);
    }
    private static string Arg(string prefix)
    {
        foreach (string arg in Environment.GetCommandLineArgs()) if (arg.StartsWith(prefix, StringComparison.Ordinal)) return arg.Substring(prefix.Length);
        return null;
    }
    private void Initialize(string path, string savedBackup, bool onlyCards)
    {
        reportPath = path;
        backupPath = savedBackup;
        cardsOnly = onlyCards;
        started = Time.realtimeSinceStartup;
        report = new Report { startedUtc = DateTime.UtcNow.ToString("O"), unity = Application.unityVersion,
            platform = Application.platform.ToString(), graphics = SystemInfo.graphicsDeviceName + " / " + SystemInfo.graphicsDeviceType };
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            Directory.CreateDirectory(Path.GetDirectoryName(backupPath));
            if (File.Exists(backupPath)) Require(JsonUtility.FromJson<Backup>(File.ReadAllText(backupPath)).restored, "Unrestored QA preference backup exists.");
            backup = new Backup { company = Application.companyName, product = Application.productName,
                hasWidth = PlayerPrefs.HasKey(DisplayPreferences.WidthKey), width = PlayerPrefs.GetInt(DisplayPreferences.WidthKey),
                hasHeight = PlayerPrefs.HasKey(DisplayPreferences.HeightKey), height = PlayerPrefs.GetInt(DisplayPreferences.HeightKey),
                hasFullscreen = PlayerPrefs.HasKey(DisplayPreferences.FullscreenKey), fullscreen = PlayerPrefs.GetInt(DisplayPreferences.FullscreenKey),
                hasVolume = PlayerPrefs.HasKey(SfxPreferences.VolumeKey), volume = PlayerPrefs.GetFloat(SfxPreferences.VolumeKey),
                hasMute = PlayerPrefs.HasKey(SfxPreferences.MuteKey), mute = PlayerPrefs.GetInt(SfxPreferences.MuteKey) };
            File.WriteAllText(backupPath, JsonUtility.ToJson(backup, true));
            oldKeyboard = Keyboard.current;
            oldUpdate = InputSystem.settings.updateMode;
            oldBackground = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            keyboard = InputSystem.AddDevice<Keyboard>("Temporary gameplay QA keyboard");
            keyboard.MakeCurrent();
            Application.runInBackground = true;
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            Application.logMessageReceived += OnLog;
            initialized = true;
            Flush();
            StartCoroutine(Guarded(cardsOnly ? RunCardsOnly() : Run()));
        }
        catch (Exception error) { Finish(error); }
    }
    private IEnumerator RunCardsOnly()
    {
        yield return Ready("MainMenu");
        float splashDeadline = Time.realtimeSinceStartup + 12f;
        while (!UnityEngine.Rendering.SplashScreen.isFinished && Time.realtimeSinceStartup < splashDeadline) yield return null;
        for (int stage = 3; stage <= 5; stage++)
        {
            yield return LoadStage(stage); // The shared reveal restores timeScale 1 after the prior paused card.
            BindStage();
            CollapsiblePanel card = Find<CollapsiblePanel>();
            LayoutElement content = Field<LayoutElement>(card, "content");
            Button toggle = Field<Button>(card, "toggleButton");
            CheckResult("stage" + stage + "-details-default-collapsed", !card.IsOpen && content.preferredHeight == 0f
                && Time.timeScale == 1f, "Fresh saved StageHUD folds details and the shared scene reveal restores gameplay time.");
            Time.timeScale = 0f;
            foreach (Vector2Int size in new[] { new Vector2Int(640, 480), new Vector2Int(1600, 900) })
            {
                Screen.SetResolution(size.x, size.y, FullScreenMode.Windowed);
                yield return Delay(1f);
                string label = "stage" + stage + "-" + size.x;
                CheckResult(label + "-native-size", Screen.width == size.x && Screen.height == size.y,
                    "Actual render size " + Screen.width + "x" + Screen.height + ".");
                toggle.onClick.Invoke();
                yield return Delay(.35f);
                CheckResult(label + "-details-open-at-pause", card.IsOpen && content.preferredHeight > 0f && Time.timeScale == 0f,
                    "Authored button expands the full stage-specific text with unscaled animation.");
                yield return Capture(label + "-open");
                RecordLayout(label + "-open", card, content, true);
                toggle.onClick.Invoke();
                yield return Delay(.35f);
                CheckResult(label + "-details-close", !card.IsOpen && content.preferredHeight == 0f,
                    "Authored button returns the card to its saved compact height.");
            }
        }
        Time.timeScale = 1f;
        CheckResult("cards-no-runtime-errors", report.errors.Count == 0, string.Join("\n", report.errors));
        Finish(null);
    }
    private IEnumerator Run()
    {
        yield return Ready("MainMenu");
        float splashDeadline = Time.realtimeSinceStartup + 12f;
        while (!UnityEngine.Rendering.SplashScreen.isFinished && Time.realtimeSinceStartup < splashDeadline) yield return null;
        MainMenuController menu = Find<MainMenuController>();
        Field<Button>(menu, "startButton").onClick.Invoke();
        yield return Ready("Stage_02");
        BindStage();
        CheckResult("start-button-loads-stage2-with-shared-fade", selector.SelectedStage == 2 && Time.timeScale == 1f, "Actual authored Start button callback and asynchronous reveal completed.");
        CollapsiblePanel card = Find<CollapsiblePanel>();
        LayoutElement content = Field<LayoutElement>(card, "content");
        Button toggle = Field<Button>(card, "toggleButton");
        CheckResult("details-default-collapsed", !card.IsOpen && content.preferredHeight == 0f, "Fresh StageHUD starts folded.");
        // Pause keeps the same UI visible while screenshot/layout inspections avoid changing gameplay.
        Time.timeScale = 0f;
        foreach (Vector2Int size in new[] { new Vector2Int(640, 480), new Vector2Int(1600, 900) })
        {
            Screen.SetResolution(size.x, size.y, FullScreenMode.Windowed);
            yield return Delay(1f);
            CheckResult("native-window-size-" + size.x, Screen.width == size.x && Screen.height == size.y,
                "Actual render size " + Screen.width + "x" + Screen.height + "; no mocked camera render.");
            if (card.IsOpen) { toggle.onClick.Invoke(); yield return Delay(.35f); }
            yield return Capture("stage2-" + size.x + "-closed");
            CheckResult("details-closed-" + size.x, !card.IsOpen && content.preferredHeight == 0f, "Saved button closes the content to zero height.");
            toggle.onClick.Invoke();
            yield return Delay(.35f);
            CheckResult("details-opens-while-paused-" + size.x, card.IsOpen && content.preferredHeight > 0f && Time.timeScale == 0f, "Authored button and unscaled animation work at timeScale zero.");
            yield return Capture("stage2-" + size.x + "-open");
            RecordLayout("stage2-" + size.x + "-open", card, content);
            toggle.onClick.Invoke();
            yield return Delay(.35f);
        }
        Time.timeScale = 1f;
        yield return LoadStage(3);
        BindStage();
        ElectricLearningInput2D learningInput = selector.GetComponent<ElectricLearningInput2D>();
        DistanceLauncher2D launcher = selector.GetStageComponent<DistanceLauncher2D>(3);
        selector.Lever.SetValue(0f);
        yield return FixedFrames(2);
        CheckResult("comparison-initial-step-and-held-spawn", lesson.IsPreparingTrial && !lesson.HasBaseline && lesson.CurrentTaskText.Contains("1/3"), lesson.CurrentTaskText);
        bool bEdge = DispatchKey(learningInput, Key.B);
        RefreshHud();
        CheckResult("b-edge-saves-baseline-and-visible-feedback", bEdge && lesson.HasBaseline && QuestContains("기준 저장"), Quest());
        CheckResult("comparison-next-step-change-one-condition", lesson.ComparisonPhaseText.Contains("2/3"), lesson.ComparisonPhaseText);
        selector.Lever.SetValue(.95f);
        yield return FixedFrames(2);
        CheckResult("changed-distance-prompts-launch", lesson.ComparisonPhaseText.Contains("3/3"), lesson.ComparisonPhaseText);
        bool eEdge = DispatchKey(launcher, Key.E);
        yield return FixedFrames(2);
        RefreshHud();
        CheckResult("e-edge-comparison-success-visible", eEdge && lesson.ComparisonChallengeSatisfied && QuestContains("비교 성공"), Quest());
        yield return Capture("stage3-comparison-success");

        lesson.PrepareTrial();
        yield return FixedFrames(2);
        lesson.RecordBaseline(); // Same lever, same saved spawn: intentional same-condition negative fixture.
        DispatchKey(launcher, Key.E);
        yield return FixedFrames(2);
        RefreshHud();
        CheckResult("same-condition-rejected-with-visible-reason", lesson.HasResult && !lesson.ComparisonChallengeSatisfied && QuestContains("조건이 같습니다"), Quest());

        lesson.PrepareTrial();
        lesson.SetBallCharge(0f);
        DispatchKey(launcher, Key.E);
        yield return FixedFrames(2);
        RefreshHud();
        CheckResult("neutral-launch-rejected-with-visible-reason", !launcher.HasLaunchSample && !lesson.ComparisonChallengeSatisfied && QuestContains("중성"), Quest());
        yield return Capture("stage3-neutral-reason");

        lesson.PrepareTrial();
        lesson.SetBallCharge(1f);
        ball.Body.position = new Vector2(20f, 0f); // Rejection-only fixture, not campaign route evidence.
        DispatchKey(launcher, Key.E);
        yield return FixedFrames(2);
        RefreshHud();
        CheckResult("outside-zone-rejected-with-visible-reason", !launcher.HasLaunchSample && !lesson.ComparisonChallengeSatisfied && QuestContains("범위 밖"), Quest());

        yield return LoadStage(4);
        BindStage();
        learningInput = selector.GetComponent<ElectricLearningInput2D>();
        PlayerController controller = ball.GetComponent<PlayerController>();
        lesson.SetBallCharge(-1f);
        selector.Lever.SetValue(0f);
        float ascentDeadline = Time.realtimeSinceStartup + 4f;
        while (!lesson.HasFieldAscent && Time.realtimeSinceStartup < ascentDeadline) yield return null;
        CheckResult("stage4-ascent-earned-through-real-field", lesson.HasFieldAscent && lesson.NegativeChallengeSatisfied, "No ascent flags assigned; real saved field accelerates the saved negative ball.");
        ball.CollectStar(); // Preservation fixture only; separate route probe verifies real star trigger.
        ball.Body.constraints = RigidbodyConstraints2D.FreezeAll;
        ball.Body.position = new Vector2(-3f, -2f);
        ball.Body.linearVelocity = new Vector2(1.25f, -.75f);
        Vector2 position = ball.Body.position, velocity = ball.Body.linearVelocity;
        int stars = ball.Stars;
        foreach (Key key in new[] { Key.X, Key.Z, Key.C })
        {
            bool edge = DispatchKey(learningInput, key);
            float expected = key == Key.X ? -1f : key == Key.Z ? 1f : 0f;
            CheckResult("stage4-live-" + key + "-preserves-state", edge && ball.Body.position == position && ball.Body.linearVelocity == velocity
                && ball.Stars == stars && lesson.HasFieldAscent && lesson.NegativeChallengeSatisfied && Mathf.Approximately(ball.SignedCharge, expected),
                "Actual virtual key edge; charge=" + ball.SignedCharge + ", stars=" + ball.Stars + ", position=" + ball.Body.position + ", velocity=" + ball.Body.linearVelocity);
        }
        bool positiveEdge = DispatchKey(controller, Key.Space);
        bool positive = Mathf.Approximately(ball.SignedCharge, 1f);
        bool negativeEdge = DispatchKey(controller, Key.Space);
        CheckResult("space-after-neutral-restores-real-positive-and-negative", positiveEdge && negativeEdge && positive && Mathf.Approximately(ball.SignedCharge, -1f)
            && ball.Body.position == position && ball.Body.linearVelocity == velocity && ball.Stars == stars && lesson.HasFieldAscent,
            "C neutral → actual PlayerController Space edges → +1 → -1; pose, velocity, stars, ascent retained.");
        lesson.TryGetCurrentSample(out ElectricForceSample negativeSample);
        lesson.SetBallCharge(0f);
        lesson.TryGetCurrentSample(out ElectricForceSample neutralSample);
        CheckResult("live-neutral-removes-electric-force", negativeSample.IsInRange && negativeSample.Force.y > 0f && neutralSample.IsInRange && neutralSample.Force.sqrMagnitude == 0f,
            "Same field and position, Fnegative=" + negativeSample.Force + ", Fneutral=" + neutralSample.Force);
        lesson.SetBallCharge(-1f);
        yield return Capture("stage4-live-polarity");

        // Far-away collision fixture proves checkpoints change on genuine Ground support, not arbitrary touches.
        controller.enabled = false;
        selector.GetStageComponent<UniformField2D>(4).enabled = false;
        ball.Body.constraints = RigidbodyConstraints2D.FreezeRotation;
        ball.Body.gravityScale = 1f;
        ball.Body.position = new Vector2(-20f, 2f);
        movement.ResetMotion();
        fixtureFloor = new GameObject("Temporary checkpoint collision fixture");
        fixtureFloor.transform.position = new Vector3(-20f, 0f, 0f);
        fixtureFloor.AddComponent<BoxCollider2D>().size = new Vector2(5f, .4f);
        Vector2 oldCheckpoint = Field<Vector2>(session, "checkpointPosition");
        int landingEvents = 0;
        Vector2 actualLanding = Vector2.zero;
        Action<Vector2> onLanding = p => { landingEvents++; actualLanding = p; };
        movement.LandedOnGround += onLanding;
        yield return Delay(.9f);
        CheckResult("untagged-real-collision-does-not-create-checkpoint", landingEvents == 0 && Field<Vector2>(session, "checkpointPosition") == oldCheckpoint,
            "A real supporting collision on an untagged fixture does not publish LandedOnGround or replace the initial spawn checkpoint.");
        fixtureFloor.tag = "Ground";
        ball.Body.position = new Vector2(-20f, 2f);
        movement.ResetMotion();
        float landingDeadline = Time.realtimeSinceStartup + 3f;
        while (landingEvents == 0 && Time.realtimeSinceStartup < landingDeadline) yield return null;
        movement.LandedOnGround -= onLanding;
        Vector2 checkpoint = Field<Vector2>(session, "checkpointPosition");
        CheckResult("ground-actual-top-contact-creates-checkpoint", landingEvents > 0 && Mathf.Abs(checkpoint.x + 20f) < .1f
            && Vector2.Distance(checkpoint, actualLanding + Vector2.up * .05f) < .03f && session.CanRetryCheckpoint,
            "Actual collision event count=" + landingEvents + ", landing=" + actualLanding + ", checkpoint=" + checkpoint);
        ball.Body.position = new Vector2(-18f, 3f);
        ball.Body.linearVelocity = new Vector2(2f, 3f);
        int retryBefore = session.RetryCount;
        bool accepted = selector.RetryCheckpoint();
        CheckResult("public-checkpoint-retry-preserves-learning-stars-charge", accepted && session.RetryCount == retryBefore + 1
            && Vector2.Distance(ball.Body.position, checkpoint) < .001f && ball.Body.linearVelocity == Vector2.zero
            && ball.Stars == stars && Mathf.Approximately(ball.SignedCharge, -1f) && lesson.HasFieldAscent,
            "Public selector RetryCheckpoint restores genuine landing position, resets motion, and preserves completed records.");
        CheckResult("checkpoint-clears-incomplete-motion-observations", !Field<bool>(lesson, "hasPreviousFieldPosition")
            && Field<float>(lesson, "ascentDistance") == 0f && Field<float>(lesson, "neutralDescentDistance") == 0f,
            "Immediate post-command transient position/distance samples are empty, so teleport distance cannot satisfy movement goals.");
        ball.Body.position = new Vector2(-18f, 3f);
        retryBefore = session.RetryCount;
        bool backspaceEdge = DispatchKey(selector.GetComponent<ElectricStageInput2D>(), Key.Backspace);
        RefreshHud();
        CheckResult("backspace-edge-uses-checkpoint-and-shows-feedback", backspaceEdge && session.RetryCount == retryBefore + 1
            && Vector2.Distance(ball.Body.position, checkpoint) < .001f && QuestContains("최근 발판"), Quest());
        Time.timeScale = 0f;
        retryBefore = session.RetryCount;
        CheckResult("checkpoint-blocked-while-paused", !session.CanRetryCheckpoint && !selector.RetryCheckpoint() && session.RetryCount == retryBefore,
            "Paused gameplay cannot consume a retry or move the ball.");
        Time.timeScale = 1f;
        Object.Destroy(fixtureFloor);
        fixtureFloor = null;
        bool rEdge = DispatchKey(selector.GetComponent<ElectricStageInput2D>(), Key.R);
        selector.TryGetSpawnPosition(out Vector2 spawn);
        CheckResult("r-edge-full-reset-clears-records-and-stars", rEdge && session.RetryCount == 0 && ball.Stars == 0
            && !lesson.HasFieldAscent && !lesson.HasNeutralFieldDescent && !lesson.HasRestoredNegativeField
            && Vector2.Distance(ball.Body.position, spawn) < .001f && ball.CurrentPolarity == Polarity.Positive,
            "Actual R Update command resets the full stage, unlike Backspace recovery.");

        // Isolated motion fixture: same authored movement component/serialized values, seeded state only.
        ball.Body.gravityScale = 0f;
        ball.Body.constraints = RigidbodyConstraints2D.FreezePositionY | RigidbodyConstraints2D.FreezeRotation;
        ball.Body.position = new Vector2(-20f, 4f);
        movement.ResetMotion();
        ball.Body.linearVelocity = Vector2.right * 7f;
        movement.SetMovex(0f);
        float begin = Time.fixedTime, startX = ball.Body.position.x;
        float deadline = Time.realtimeSinceStartup + 3f;
        while (Mathf.Abs(ball.Body.linearVelocity.x) > .001f && Time.realtimeSinceStartup < deadline) yield return new WaitForFixedUpdate();
        report.brakeSeconds = Time.fixedTime - begin;
        report.brakeDistance = ball.Body.position.x - startX;
        CheckResult("authored-braking-deceleration-nine", Mathf.Approximately(Field<float>(movement, "deceleration"), 9f)
            && report.brakeSeconds >= .74f && report.brakeSeconds <= .84f && report.brakeDistance >= 2.45f && report.brakeDistance <= 2.85f,
            "7 units/s → rest: " + report.brakeSeconds + " fixed seconds, distance=" + report.brakeDistance + ". Gravity/collision isolation fixture.");
        movement.ResetMotion();
        ball.Body.linearVelocity = Vector2.right * 7f;
        movement.SetMovex(-1f);
        begin = Time.fixedTime;
        deadline = Time.realtimeSinceStartup + 3f;
        while (ball.Body.linearVelocity.x > 0f && Time.realtimeSinceStartup < deadline) yield return new WaitForFixedUpdate();
        report.reverseZeroSeconds = Time.fixedTime - begin;
        CheckResult("opposite-input-brakes-before-reversing", report.reverseZeroSeconds >= .74f && report.reverseZeroSeconds <= .84f,
            "Reverse input crosses zero in " + report.reverseZeroSeconds + " fixed seconds.");
        movement.ResetMotion();
        bool impulse = movement.LaunchImpulse(Vector2.right * 5f * ball.Body.mass);
        movement.SetMovex(0f);
        yield return FixedFrames(26);
        report.preservedImpulseSpeed = ball.Body.linearVelocity.x;
        CheckResult("electric-impulse-momentum-survives-no-input", impulse && Mathf.Abs(report.preservedImpulseSpeed - 5f) < .02f,
            "Public LaunchImpulse, no input, >0.5 simulated seconds: vx=" + report.preservedImpulseSpeed);
        CheckResult("mastery-hints-available-without-goal-gate", !string.IsNullOrEmpty(session.MasteryHint)
            && session.MasteryHint.Contains("선택") && !lesson.CurrentLearningGoalSatisfied,
            "Optional challenge hint exists independently of mandatory lesson completion: " + session.MasteryHint);
        CheckResult("no-runtime-errors", report.errors.Count == 0, string.Join("\n", report.errors));
        Finish(null);
    }

    private void BindStage()
    {
        selector = Find<ElectricStageSelector>();
        ball = selector.Ball;
        movement = ball.GetComponent<PlayerMovement>();
        lesson = selector.GetComponent<ElectricLearningController2D>();
        session = selector.GetComponent<ElectricStageSession2D>();
        hud = Find<ElectricStageHudView>();
        Require(session != null && session.IsInitialized, "Saved stage session is not initialized.");
    }
    private IEnumerator LoadStage(int stage)
    {
        Require(SceneTransitionController.LoadScene(ElectricStageProgression.StageScenePath(stage)), "Stage transition rejected.");
        yield return Ready("Stage_" + stage.ToString("00"));
    }
    private IEnumerator Ready(string scene)
    {
        float deadline = Time.realtimeSinceStartup + 25f;
        while ((SceneManager.GetActiveScene().name != scene || !SceneTransitionController.IsAvailable || SceneTransitionController.IsBusy) && Time.realtimeSinceStartup < deadline) yield return null;
        Require(SceneManager.GetActiveScene().name == scene && !SceneTransitionController.IsBusy, "Timed out loading " + scene);
        yield return null;
    }
    private static IEnumerator Delay(float seconds)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < deadline) yield return null;
    }
    private static IEnumerator FixedFrames(int frames)
    {
        for (int i = 0; i < frames; i++) yield return new WaitForFixedUpdate();
    }
    private bool DispatchKey(MonoBehaviour target, Key key)
    {
        Require(target != null && target.isActiveAndEnabled, "Input target is not enabled: " + key);
        InputSystem.QueueStateEvent(keyboard, new KeyboardState()); Pump();
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(key)); Pump();
        bool edge = keyboard[key].wasPressedThisFrame;
        target.SendMessage("Update", SendMessageOptions.RequireReceiver);
        Pump();
        bool heldHasNoEdge = !keyboard[key].wasPressedThisFrame;
        target.SendMessage("Update", SendMessageOptions.RequireReceiver);
        InputSystem.QueueStateEvent(keyboard, new KeyboardState()); Pump();
        return edge && heldHasNoEdge;
    }
    private static void Pump() => typeof(InputSystem).GetMethod("Update", BindingFlags.Static | BindingFlags.NonPublic,
        null, new[] { typeof(InputUpdateType) }, null).Invoke(null, new object[] { InputUpdateType.Dynamic });
    private void RefreshHud() => hud.SendMessage("LateUpdate", SendMessageOptions.RequireReceiver);
    private string Quest() => Field<TMP_Text>(hud, "questText").text;
    private bool QuestContains(string fragment) { RefreshHud(); return Quest().Contains(fragment); }
    private IEnumerator Capture(string label)
    {
        Debug.developerConsoleVisible = false;
        yield return new WaitForEndOfFrame();
        Texture2D capture = ScreenCapture.CaptureScreenshotAsTexture();
        Require(capture != null, "Actual screen capture failed.");
        string path = Path.Combine(Path.GetDirectoryName(reportPath), Path.GetFileNameWithoutExtension(reportPath) + "-" + label + ".png");
        File.WriteAllBytes(path, capture.EncodeToPNG());
        Object.Destroy(capture);
        report.screenshots.Add(path);
        Flush();
    }
    private void RecordLayout(string label, CollapsiblePanel card, LayoutElement content, bool requireAllTextInside = false)
    {
        Canvas.ForceUpdateCanvases();
        RectTransform panel = card.transform as RectTransform;
        bool inside = InsideScreen(panel), overflow = false;
        foreach (TMP_Text text in content.GetComponentsInChildren<TMP_Text>())
        {
            text.ForceMeshUpdate();
            Rect screen = ScreenRect(text.rectTransform);
            report.layouts.Add(new Layout { label = label, text = text.text, overflow = text.isTextOverflowing,
                fontSize = text.fontSize, x = screen.x, y = screen.y, width = screen.width, height = screen.height });
            overflow |= text.isTextOverflowing;
            if (requireAllTextInside) inside &= InsideScreen(text.rectTransform);
        }
        CheckResult("details-visible-layout-" + label, inside && !overflow, "Panel inside native screen=" + inside + "; expanded TMP overflow=" + overflow);
    }
    private static Rect ScreenRect(RectTransform rect)
    {
        var corners = new Vector3[4]; rect.GetWorldCorners(corners);
        Canvas canvas = rect.GetComponentInParent<Canvas>();
        Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        Vector2 min = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
        Vector2 max = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }
    private static bool InsideScreen(RectTransform rect)
    {
        if (rect == null) return false;
        Rect screen = ScreenRect(rect);
        return screen.xMin >= -1f && screen.yMin >= -1f && screen.xMax <= Screen.width + 1f && screen.yMax <= Screen.height + 1f;
    }
    private static T Find<T>() where T : Object
    {
        T target = Object.FindFirstObjectByType<T>();
        Require(target != null, "Required authored component missing: " + typeof(T).Name);
        return target;
    }
    private static T Field<T>(object target, string name)
    {
        FieldInfo field = target.GetType().GetField(name, Fields);
        Require(field != null, "Required field missing: " + name);
        return (T)field.GetValue(target);
    }
    private void CheckResult(string name, bool passed, string details)
    {
        report.checks.Add(new Check { name = name, passed = passed, details = details });
        Flush();
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private void OnLog(string message, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) { report.errors.Add(message + "\n" + stack); Flush(); }
    }
    private void Update()
    {
        if (!finishing && initialized && Time.realtimeSinceStartup - started > 180f) Finish(new TimeoutException("Gameplay improvement QA exceeded 180 seconds."));
    }
    private void Flush()
    {
        if (!string.IsNullOrEmpty(reportPath)) File.WriteAllText(reportPath, JsonUtility.ToJson(report, true));
    }
    private IEnumerator Guarded(IEnumerator sequence)
    {
        var stack = new Stack<IEnumerator>(); stack.Push(sequence);
        while (stack.Count > 0 && !finishing)
        {
            object item = null; bool next = false; Exception error = null;
            try { next = stack.Peek().MoveNext(); if (next) item = stack.Peek().Current; }
            catch (Exception exception) { error = exception; }
            if (error != null) { Finish(error); yield break; }
            if (!next) stack.Pop(); else if (item is IEnumerator child) stack.Push(child); else yield return item;
        }
    }
    private static void RestoreInt(string key, bool exists, int value)
    {
        if (exists) PlayerPrefs.SetInt(key, value); else PlayerPrefs.DeleteKey(key);
    }
    private void RestorePreferences()
    {
        if (backup == null) return;
        Require(backup.company == Application.companyName && backup.product == Application.productName, "Preference identity changed.");
        // Native Screen synchronization may have persisted the two screenshot resolutions.
        DisplayPreferences.Save(); SfxPreferences.Save();
        RestoreInt(DisplayPreferences.WidthKey, backup.hasWidth, backup.width);
        RestoreInt(DisplayPreferences.HeightKey, backup.hasHeight, backup.height);
        RestoreInt(DisplayPreferences.FullscreenKey, backup.hasFullscreen, backup.fullscreen);
        RestoreInt(SfxPreferences.MuteKey, backup.hasMute, backup.mute);
        if (backup.hasVolume) PlayerPrefs.SetFloat(SfxPreferences.VolumeKey, backup.volume); else PlayerPrefs.DeleteKey(SfxPreferences.VolumeKey);
        PlayerPrefs.Save();
        backup.restored = true;
        File.WriteAllText(backupPath, JsonUtility.ToJson(backup, true));
        report.preferencesRestored = true;
    }
    private void Finish(Exception error)
    {
        if (finishing) return;
        finishing = true;
        if (error != null) report.errors.Add(error.ToString());
        try { RestorePreferences(); } catch (Exception restore) { report.errors.Add(restore.ToString()); }
        Application.logMessageReceived -= OnLog;
        if (initialized)
        {
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            if (oldKeyboard != null && oldKeyboard.added) oldKeyboard.MakeCurrent();
            InputSystem.settings.updateMode = oldUpdate;
            InputSystem.settings.backgroundBehavior = oldBackground;
        }
        if (fixtureFloor != null) Object.Destroy(fixtureFloor);
        report.status = report.errors.Count == 0 && report.preferencesRestored && !report.checks.Exists(c => !c.passed) ? "passed" : "failed";
        report.finishedUtc = DateTime.UtcNow.ToString("O");
        Flush();
        Application.Quit(report.status == "passed" ? 0 : 1);
    }
}
