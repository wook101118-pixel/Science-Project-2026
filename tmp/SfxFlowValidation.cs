// Temporary integration harness. Copy into Assets/Editor only in an isolated Unity project.
// Unity.exe -batchmode -nographics -force-audio -projectPath <clone> -executeMethod SfxFlowValidation.RunBatch
// Omit -quit: this harness owns its isolated Play Mode and editor exit.
using System;
using System.Collections.Generic;
using System.Globalization;
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
using sung_wook;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class SfxFlowValidation
{
    private const string Prefix = "Codex.SfxFlowValidation.";
    private const string MenuPath = "Assets/@Project/@sung_wook/@Game/00_Scenes/MainMenu.unity";
    private const string Stage2Path = "Assets/@Project/@sung_wook/@Game/00_Scenes/Stage/Stage_02.unity";
    private const string SourceLogs = "C:/Users/wook1/GitBub-Unity/Science-Project-2026/Logs";
    private enum Phase { EnterPlay, Menu, Settings, CloseSettings, LoadStage, WaitBounce, WaitWall, Charge,
        WaitStar, LaunchAndGoal, BeginPause, Paused, AfterResume, Restart, ReturnMenu, Finish }

    [Serializable] private sealed class Check { public string name; public bool passed; public string details; }
    [Serializable] private sealed class ClipEvidence { public string cue; public string assetPath; public float length; public int samples; public int channels; public float peak; }
    [Serializable] private sealed class SceneLoad { public string path; public bool busy; public float alpha; public float timeScale; }
    [Serializable] private sealed class Report
    {
        public string status = "running";
        public string mode = "Saved MainMenu and Stage 01–05 audio references; isolated Play Mode with real physics floor/wall/star collisions, actual Space handler, actual saved menu/pause buttons, actual launch component success/failure, and accepted goal callback. Temporary collider/launch fixtures are invisible and unsaved. Goal fixture sets the contact lesson flag only to isolate goal sound; this is not a course-clear validation. AudioSource playback, cue timestamps, pause/resume, async fade, listener count, persistent pool and PCM are observed. No runtime production counters or saved gameplay changes.";
        public List<Check> checks = new List<Check>();
        public List<ClipEvidence> clips = new List<ClipEvidence>();
        public List<SceneLoad> sceneLoads = new List<SceneLoad>();
        public List<string> errors = new List<string>();
    }

    private static Keyboard probeKeyboard;
    private static Keyboard previousKeyboard;
    private static InputSettings.EditorInputBehaviorInPlayMode previousEditorInput;
    private static InputSettings.BackgroundBehavior previousBackground;
    private static InputSettings.UpdateMode previousUpdateMode;
    private static bool previousRunInBackground;
    private static GameObject floorFixture;
    private static GameObject wallFixture;
    private static ElectricStar2D targetStar;
    private static AudioSource pausedSource;
    private static float observedCueTime;
    private static bool wallContactSeen;
    private static int initialServiceId;
    private static int initialSourceCount;
    private static int originalBallId;

    static SfxFlowValidation()
    {
        if (!Application.isBatchMode) return;
        if (SessionState.GetBool(Prefix + "Active", false)) ResumeCallbacks();
        if (SessionState.GetBool(Prefix + "Exiting", false)) EditorApplication.update += ExitWhenReady;
    }

    private static string ReportPath
    {
        get
        {
            string folder = Environment.GetEnvironmentVariable("CODEX_SFX_VALIDATION_OUTPUT");
            return Path.Combine(string.IsNullOrWhiteSpace(folder) ? SourceLogs : Path.GetFullPath(folder), "SfxFlowValidation.json");
        }
    }

    public static void RunBatch()
    {
        if (!Application.isBatchMode) { Debug.LogWarning("SFX validation requires a separate isolated batch editor."); return; }
        Save(new Report());
        try
        {
            SessionState.SetBool(Prefix + "Active", true);
            SessionState.SetBool(Prefix + "Exiting", false);
            SessionState.SetBool(Prefix + "EnterRequested", false);
            SessionState.SetString(Prefix + "StartedUtc", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            SetPhase(Phase.EnterPlay);
            var paths = new List<string> { MenuPath };
            for (int stage = 1; stage <= 5; stage++) paths.Add(ElectricStageProgression.StageScenePath(stage));
            bool inspectedClips = false;
            foreach (string path in paths)
            {
                EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                ElectricSfxPlayer sfx = FindInScene<ElectricSfxPlayer>();
                Require(sfx != null, "Saved scene is missing its SFX component: " + path);
                AudioSource[] pool = Reference<AudioSource[]>(sfx, "gameplaySources");
                AudioSource ui = Reference<AudioSource>(sfx, "uiSource");
                var cues = Reference<ElectricSfxPlayer.CueDefinition[]>(sfx, "cues");
                bool valid = pool != null && pool.Length == 6 && ui != null && cues != null && cues.Length == 8;
                var distinct = new HashSet<AudioSource>();
                if (pool != null) foreach (AudioSource source in pool)
                    valid &= source != null && source.transform.IsChildOf(sfx.transform) && distinct.Add(source)
                        && !source.playOnAwake && !source.loop && source.spatialBlend == 0f;
                valid &= ui != null && !distinct.Contains(ui) && ui.transform.IsChildOf(sfx.transform)
                    && !ui.playOnAwake && !ui.loop && ui.spatialBlend == 0f;
                CheckResult(Path.GetFileNameWithoutExtension(path) + "-saved-eight-cues-and-fixed-seven-source-pool", valid,
                    "Six distinct authored 2D gameplay sources plus one authored UI source; no autoplay or looping.");
                CheckResult(Path.GetFileNameWithoutExtension(path) + "-legacy-music-owner-is-inactive", LegacyMusicOwnersInactive(),
                    "Any saved legacy SoundManager is inactive before Awake can create its BGM node.");
                if (!inspectedClips) { InspectClips(cues); inspectedClips = true; }
                if (path == MenuPath)
                    CheckResult("menu-saves-one-audio-listener", ActiveListenersInScene() == 1, "Saved menu camera supplies exactly one enabled AudioListener.");
            }
            EditorSceneManager.OpenScene(MenuPath, OpenSceneMode.Single);
            ResumeCallbacks();
        }
        catch (Exception error) { Fail(error); }
    }

    private static void InspectClips(ElectricSfxPlayer.CueDefinition[] cues)
    {
        var seen = new HashSet<SfxCue>();
        foreach (var cue in cues)
        {
            Require(cue != null && cue.clip != null && seen.Add(cue.cue), "Cue definitions must contain unique non-null clips.");
            AudioClip clip = cue.clip;
            clip.LoadAudioData();
            float[] pcm = new float[Mathf.Min(clip.samples * clip.channels, 262144)];
            bool readable = pcm.Length > 0 && clip.GetData(pcm, 0);
            float peak = 0f;
            foreach (float sample in pcm) peak = Mathf.Max(peak, Mathf.Abs(sample));
            Report report = Load();
            report.clips.Add(new ClipEvidence { cue = cue.cue.ToString(), assetPath = AssetDatabase.GetAssetPath(clip),
                length = clip.length, samples = clip.samples, channels = clip.channels, peak = peak });
            Save(report);
            CheckResult("clip-" + cue.cue + "-loads-with-audible-nonzero-pcm", readable && peak > 0.0001f
                && clip.length > 0.02f && clip.length < 6f && cue.volume > 0f && cue.volume <= 1f
                && cue.pitch >= 0.5f && cue.pitch <= 2f && cue.minimumInterval >= 0f,
                AssetDatabase.GetAssetPath(clip) + "; seconds=" + clip.length.ToString("0.000", CultureInfo.InvariantCulture)
                + ", PCM peak=" + peak.ToString("0.0000", CultureInfo.InvariantCulture) + ".");
        }
        CheckResult("all-eight-defined-cues-present", seen.Count == 8, "Bounce, Charge, Star, Launch, Record, LearningComplete, StageClear and UiClick.");
    }

    private static void ResumeCallbacks()
    {
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(Prefix + "Active", false)) return;
        try
        {
            DateTime started = DateTime.Parse(SessionState.GetString(Prefix + "StartedUtc", ""), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            Require((DateTime.UtcNow - started).TotalSeconds < 180d, "SFX flow exceeded its 180-second real-time watchdog.");
            Require(Load().errors.Count == 0, "Unity logged an error during SFX verification.");
            Phase phase = (Phase)SessionState.GetInt(Prefix + "Phase", 0);
            if (!EditorApplication.isPlaying)
            {
                if (phase == Phase.EnterPlay && !SessionState.GetBool(Prefix + "EnterRequested", false)
                    && !EditorApplication.isCompiling && !EditorApplication.isUpdating && !EditorApplication.isPlayingOrWillChangePlaymode)
                { SessionState.SetBool(Prefix + "EnterRequested", true); EditorApplication.EnterPlaymode(); }
                return;
            }
            if (EditorApplication.isPaused) return;
            if (phase == Phase.EnterPlay) { SetPhase(Phase.Menu); phase = Phase.Menu; }
            ElectricSfxPlayer sfx = Sfx();
            SceneTransitionController transition = Object.FindFirstObjectByType<SceneTransitionController>();
            if (sfx == null || transition == null) return;
            if (phase == Phase.Menu)
            {
                if (SceneTransitionController.IsBusy) return;
                MainMenuController menu = FindInScene<MainMenuController>();
                if (menu == null) return;
                Button settingsButton = Reference<Button>(menu, "settingsButton");
                // FinishTransition may release IsBusy after MainMenu.Update already ran this frame.
                // Let the real menu refresh its saved button state on the next player frame.
                if (settingsButton == null || !settingsButton.IsActive() || !settingsButton.IsInteractable()) return;
                SetupKeyboard();
                initialServiceId = sfx.GetInstanceID();
                initialSourceCount = sfx.GetComponentsInChildren<AudioSource>(true).Length;
                CheckResult("menu-runtime-has-one-persistent-sfx-and-listener", ElectricSfxPlayer.IsAvailable
                    && sfx.gameObject.scene.name == "DontDestroyOnLoad" && CountActiveSfx() == 1 && CountListeners() == 1
                    && initialSourceCount == 7 && NoRuntimeBgm(),
                    "Authored SFX service shares the persistent transition root and no legacy BGM source is created.");
                observedCueTime = CueTime(SfxCue.UiClick);
                Click(settingsButton);
                CheckEmitted("saved-settings-button-plays-ui-click", SfxCue.UiClick, observedCueTime, true);
                SetPhase(Phase.Settings);
                return;
            }
            if (phase == Phase.Settings)
            {
                if (!After(0.2d)) return;
                MainMenuController menu = FindInScene<MainMenuController>();
                Require(menu.IsSettingsOpen, "Saved settings button did not open its window.");
                observedCueTime = CueTime(SfxCue.UiClick);
                Click(Reference<Button>(menu, "closeSettingsButton"));
                CheckEmitted("saved-settings-close-plays-ui-click", SfxCue.UiClick, observedCueTime, true);
                SetPhase(Phase.CloseSettings);
                return;
            }
            if (phase == Phase.CloseSettings)
            {
                if (!After(0.2d)) return;
                PrepareLoad(Stage2Path);
                observedCueTime = CueTime(SfxCue.UiClick);
                Click(Reference<Button>(FindInScene<MainMenuController>(), "startButton"));
                CheckEmitted("saved-start-button-plays-ui-click-during-accepted-load", SfxCue.UiClick, observedCueTime, true);
                CheckResult("transition-blocks-gameplay-sfx-while-allowing-accepted-ui", SceneTransitionController.IsBusy
                    && Reference<AsyncOperation>(transition, "pendingLoad") != null && !ElectricSfxPlayer.Play(SfxCue.Bounce)
                    && !ElectricSfxPlayer.Play(SfxCue.StageClear), "Async loading rejects gameplay and clear cues while the accepted menu click is audible.");
                SetPhase(Phase.LoadStage);
                return;
            }
            if (phase == Phase.LoadStage)
            {
                if (!AwaitLoad(Stage2Path)) return;
                ElectricStageSession2D session = FindInScene<ElectricStageSession2D>();
                if (session == null || !session.IsInitialized) return;
                CheckPersistence("first-stage-load");
                ElectricStageProgression progression = FindInScene<ElectricStageProgression>();
                Require(progression != null, "Saved gameplay scene has no progression owner.");
                progression.enabled = false; // Isolate the later accepted-goal audio fixture from automatic scene progression.
                PrepareBallFixture();
                PrepareBounce();
                SetPhase(Phase.WaitBounce);
                return;
            }
            if (phase == Phase.WaitBounce)
            {
                Require(!After(6d), "The saved player did not bounce on the invisible Ground fixture within six seconds.");
                if (!Changed(CueTime(SfxCue.Bounce), observedCueTime)) return;
                CheckEmitted("actual-downward-ground-contact-emits-bounce", SfxCue.Bounce, observedCueTime, false, false);
                CheckResult("bounce-sound-coincides-with-upward-physical-impulse", Ball().Body.linearVelocity.y > 0.1f,
                    "The saved PlayerMovement reacts to a real Ground collision and the Rigidbody is moving upward.");
                PrepareWall();
                SetPhase(Phase.WaitWall);
                return;
            }
            if (phase == Phase.WaitWall)
            {
                Collider2D ballCollider = Ball().GetComponent<Collider2D>();
                Collider2D wallCollider = wallFixture.GetComponent<Collider2D>();
                if (ballCollider.IsTouching(wallCollider)) wallContactSeen = true;
                Require(!After(5d), "The real wall contact fixture was not observed within five seconds.");
                if (!wallContactSeen || !After(0.5d)) return;
                CheckResult("actual-side-wall-contact-does-not-emit-bounce", Same(CueTime(SfxCue.Bounce), observedCueTime)
                    && Mathf.Abs(Ball().Body.linearVelocity.y) < 0.001f,
                    "A vertical Ground-tagged collider generated actual side contact without an upward support normal or bounce sound.");
                wallFixture.SetActive(false);
                Ball().Body.linearVelocity = Vector2.zero;
                Ball().Body.constraints = RigidbodyConstraints2D.FreezeAll;
                SetPhase(Phase.Charge);
                return;
            }
            if (phase == Phase.Charge)
            {
                if (!After(0.2d)) return;
                ElectricBall2D ball = Ball();
                Require(ball.CurrentPolarity == Polarity.Neutral, "The off-map charge fixture should begin neutral.");
                observedCueTime = CueTime(SfxCue.Charge);
                InputSystem.QueueStateEvent(probeKeyboard, new KeyboardState(Key.Space));
                PumpInput();
                Require(probeKeyboard.spaceKey.wasPressedThisFrame, "Synthetic Space did not reach the Input System keyboard.");
                ball.GetComponent<PlayerController>().SendMessage("Update", SendMessageOptions.RequireReceiver);
                ReleaseKeyboard();
                CheckEmitted("actual-space-handler-emits-charge-sfx", SfxCue.Charge, observedCueTime, false);
                CheckResult("space-still-changes-real-player-charge", ball.CurrentPolarity == Polarity.Positive && ball.SignedCharge > 0f,
                    "Audio accompanies the existing real charge and sprite state change.");
                float changedAt = CueTime(SfxCue.Charge);
                ball.SetPositive();
                CheckResult("unchanged-player-state-does-not-replay-charge", Same(CueTime(SfxCue.Charge), changedAt),
                    "Repeating the current state produces no additional sound emission.");
                targetStar = FindInScene<ElectricStar2D>();
                Require(targetStar != null && targetStar.isActiveAndEnabled, "No saved active star is available for the overlap fixture.");
                observedCueTime = CueTime(SfxCue.Star);
                ball.Body.position = targetStar.transform.position;
                ball.Body.linearVelocity = Vector2.zero;
                Physics2D.SyncTransforms();
                SetPhase(Phase.WaitStar);
                return;
            }
            if (phase == Phase.WaitStar)
            {
                Require(!After(5d), "The saved star did not receive its real physics overlap within five seconds.");
                if (!targetStar.IsCollected) return;
                CheckEmitted("saved-star-real-trigger-emits-collection-sfx", SfxCue.Star, observedCueTime, false, false);
                CheckResult("collected-star-is-counted-and-hidden-once", Ball().Stars == 1 && !targetStar.gameObject.activeSelf,
                    "The actual existing star trigger increments the current attempt and hides the star.");
                float collectedAt = CueTime(SfxCue.Star);
                typeof(ElectricStar2D).GetMethod("OnTriggerEnter2D", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(targetStar, new object[] { Ball().GetComponent<Collider2D>() });
                CheckResult("duplicate-star-trigger-does-not-replay-sfx", Same(CueTime(SfxCue.Star), collectedAt) && Ball().Stars == 1,
                    "The disabled/collected star rejects a duplicate callback.");
                SetPhase(Phase.LaunchAndGoal);
                return;
            }
            if (phase == Phase.LaunchAndGoal)
            {
                if (!After(0.2d)) return;
                CheckLaunchAndAcceptedGoal();
                SetPhase(Phase.BeginPause);
                return;
            }
            if (phase == Phase.BeginPause)
            {
                if (!After(0.45d)) return;
                CheckResult("record-service-cue-uses-gameplay-pool", ElectricSfxPlayer.Play(SfxCue.Record)
                    && PlayingSource(SfxCue.Record, false) != null, "The existing record feedback has an authored gameplay cue.");
                CheckResult("learning-complete-service-cue-uses-gameplay-pool", ElectricSfxPlayer.Play(SfxCue.LearningComplete)
                    && PlayingSource(SfxCue.LearningComplete, false) != null, "The existing learning feedback has an authored gameplay cue.");
                Require(ElectricSfxPlayer.Play(SfxCue.Bounce), "Could not begin a real gameplay sound before pausing.");
                pausedSource = PlayingSource(SfxCue.Bounce, false);
                Require(pausedSource != null, "The pre-pause bounce AudioSource did not play.");
                observedCueTime = CueTime(SfxCue.UiClick);
                PauseMenuController pause = FindInScene<PauseMenuController>();
                pause.Pause();
                CheckEmitted("opening-pause-plays-ui-click-at-zero-time-scale", SfxCue.UiClick, observedCueTime, true);
                CheckResult("pause-immediately-pauses-all-gameplay-audio", pause.IsPaused && Time.timeScale == 0f
                    && Value<bool>(sfx, "gameplayPaused") && AllGameplayStopped(),
                    "Pause uses AudioSource.Pause immediately; UI audio remains separate and active.");
                observedCueTime = CueTime(SfxCue.Bounce);
                CheckResult("paused-gameplay-cues-are-rejected", !ElectricSfxPlayer.Play(SfxCue.Bounce)
                    && !ElectricSfxPlayer.Play(SfxCue.Charge) && !ElectricSfxPlayer.Play(SfxCue.Star)
                    && !ElectricSfxPlayer.Play(SfxCue.Launch) && !ElectricSfxPlayer.Play(SfxCue.Record)
                    && !ElectricSfxPlayer.Play(SfxCue.LearningComplete) && Same(CueTime(SfxCue.Bounce), observedCueTime),
                    "A frozen game cannot start new gameplay sounds.");
                SetPhase(Phase.Paused);
                return;
            }
            if (phase == Phase.Paused)
            {
                if (!After(0.4d)) return;
                CheckResult("clear-cue-can-play-on-completion-owned-zero-time", ElectricSfxPlayer.Play(SfxCue.StageClear)
                    && PlayingSource(SfxCue.StageClear, true) != null && Time.timeScale == 0f,
                    "The completion fanfare uses UI audio so a completed game may own Time.timeScale=0.");
                float uiBefore = CueTime(SfxCue.UiClick);
                Click(Reference<Button>(FindInScene<PauseMenuController>(), "resumeButton"));
                CheckEmitted("saved-continue-button-plays-ui-sfx", SfxCue.UiClick, uiBefore, true);
                CheckResult("continue-unpauses-existing-gameplay-source", Time.timeScale == 1f
                    && !Value<bool>(sfx, "gameplayPaused") && pausedSource != null && pausedSource.isPlaying,
                    "The same paused bounce source resumes instead of restarting or creating a new source.");
                SetPhase(Phase.AfterResume);
                return;
            }
            if (phase == Phase.AfterResume)
            {
                if (!After(0.25d)) return;
                Require(ElectricSfxPlayer.Play(SfxCue.Launch), "Could not prepare a gameplay sound for the transition stop check.");
                Require(PlayingSource(SfxCue.Launch, false) != null, "Pre-transition gameplay source was not playing.");
                PauseMenuController pause = FindInScene<PauseMenuController>();
                pause.Pause();
                PrepareLoad(Stage2Path);
                originalBallId = Ball().GetInstanceID();
                // Restart is a distinct saved UI action; pause-opening click may be within the intentional UI debounce.
                Click(Reference<Button>(pause, "restartButton"));
                CheckResult("restart-load-begins-async-and-stops-all-old-gameplay-sources", SceneTransitionController.IsBusy
                    && Reference<AsyncOperation>(transition, "pendingLoad") != null && AllGameplayStopped(),
                    "All pre-transition gameplay sources are stopped before the old scene unloads.");
                foreach (AudioSource source in Reference<AudioSource[]>(sfx, "gameplaySources"))
                    CheckResult("transition-source-" + source.name + "-is-stopped-not-paused", source.timeSamples == 0,
                        "Stopped source returns its playback cursor to sample zero.");
                SetPhase(Phase.Restart);
                return;
            }
            if (phase == Phase.Restart)
            {
                if (!AwaitLoad(Stage2Path)) return;
                ElectricStageSession2D session = FindInScene<ElectricStageSession2D>();
                if (session == null || !session.IsInitialized) return;
                CheckPersistence("current-stage-restart");
                CheckResult("restart-uses-fresh-saved-stage-without-audio-leaks", Ball().GetInstanceID() != originalBallId
                    && Ball().Stars == 0 && AllGameplayStopped() && !Value<bool>(sfx, "gameplayPaused"),
                    "The current stage resets while the persistent fixed audio pool is reused and old sounds stay stopped.");
                PauseMenuController pause = FindInScene<PauseMenuController>();
                pause.Pause();
                PrepareLoad(MenuPath);
                Click(Reference<Button>(pause, "mainMenuButton"));
                CheckResult("saved-return-button-starts-async-menu-load", SceneTransitionController.IsBusy
                    && Reference<AsyncOperation>(transition, "pendingLoad") != null && AllGameplayStopped(),
                    "Main menu return uses the existing asynchronous fade and clears gameplay sounds.");
                SetPhase(Phase.ReturnMenu);
                return;
            }
            if (phase == Phase.ReturnMenu)
            {
                if (!AwaitLoad(MenuPath)) return;
                CheckPersistence("main-menu-return");
                CheckResult("returned-menu-keeps-sfx-and-no-gameplay-or-music", FindInScene<MainMenuController>() != null
                    && FindInScene<ElectricBall2D>() == null && AllGameplayStopped() && Time.timeScale == 1f && NoRuntimeBgm(),
                    "The returned menu has one listener, one persistent audio service and no old gameplay or music playback.");
                SetPhase(Phase.Finish);
                Finish();
            }
        }
        catch (Exception error) { Fail(error); }
    }

    private static void PrepareBallFixture()
    {
        ElectricBall2D ball = Ball();
        ElectricLearningController2D lesson = FindInScene<ElectricLearningController2D>();
        if (lesson != null) lesson.SendMessage("ReleaseTrial", SendMessageOptions.RequireReceiver);
        ball.enabled = true;
        ball.GetComponent<PlayerMovement>().enabled = true;
        ball.Body.bodyType = RigidbodyType2D.Dynamic;
        ball.Body.simulated = true;
        ball.Body.constraints = RigidbodyConstraints2D.FreezeRotation;
        ball.Body.mass = 1f;
        ball.Body.gravityScale = 1f;
        ball.Body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        ball.Body.position = new Vector2(-50f, 10f);
        ball.Body.linearVelocity = Vector2.zero;
        ball.Body.angularVelocity = 0f;
        ball.SetNeutral();
        ball.ResetStars();
        Physics2D.SyncTransforms();
    }

    private static void PrepareBounce()
    {
        int layer = 0;
        foreach (Collider2D collider in ComponentsInScene<Collider2D>())
            if (collider.gameObject.CompareTag("Ground") && !collider.isTrigger) { layer = collider.gameObject.layer; break; }
        floorFixture = new GameObject("Sfx Validation Invisible Ground");
        floorFixture.tag = "Ground";
        floorFixture.layer = layer;
        floorFixture.transform.position = new Vector2(-50f, 8f);
        floorFixture.AddComponent<BoxCollider2D>().size = new Vector2(8f, 0.3f);
        observedCueTime = CueTime(SfxCue.Bounce);
        Physics2D.SyncTransforms();
    }

    private static void PrepareWall()
    {
        floorFixture.SetActive(false);
        wallFixture = new GameObject("Sfx Validation Invisible Ground Wall");
        wallFixture.tag = "Ground";
        wallFixture.layer = floorFixture.layer;
        wallFixture.transform.position = new Vector2(-49.2f, 10f);
        wallFixture.AddComponent<BoxCollider2D>().size = new Vector2(0.3f, 8f);
        Ball().Body.position = new Vector2(-50.5f, 10f);
        Ball().Body.gravityScale = 0f;
        Ball().Body.constraints = RigidbodyConstraints2D.FreezePositionY | RigidbodyConstraints2D.FreezeRotation;
        Ball().Body.linearVelocity = new Vector2(8f, 0f);
        observedCueTime = CueTime(SfxCue.Bounce);
        wallContactSeen = false;
        Physics2D.SyncTransforms();
    }

    private static void CheckLaunchAndAcceptedGoal()
    {
        ElectricBall2D ball = Ball();
        ball.Body.position = new Vector2(-50f, 10f);
        ball.Body.linearVelocity = Vector2.zero;
        ball.Body.constraints = RigidbodyConstraints2D.FreezeRotation;
        GameObject fixture = new GameObject("Sfx Validation Invisible Voltage Fixture");
        fixture.transform.position = ball.Body.position;
        BoxCollider2D zone = fixture.AddComponent<BoxCollider2D>();
        zone.isTrigger = true;
        zone.size = new Vector2(3f, 3f);
        GameObject bottom = new GameObject("Bottom");
        bottom.transform.SetParent(fixture.transform);
        bottom.transform.position = new Vector2(-50f, 9f);
        GameObject top = new GameObject("Top");
        top.transform.SetParent(fixture.transform);
        top.transform.position = new Vector2(-50f, 11f);
        VoltageLauncher2D launcher = fixture.AddComponent<VoltageLauncher2D>();
        launcher.Configure(ball, null, zone, bottom.transform, top.transform);
        Physics2D.SyncTransforms();
        float previous = CueTime(SfxCue.Launch);
        launcher.RequestLaunch();
        launcher.SendMessage("FixedUpdate", SendMessageOptions.RequireReceiver);
        CheckResult("failed-zero-voltage-launch-does-not-play-sfx", !launcher.HasLaunchSample && Same(CueTime(SfxCue.Launch), previous),
            "The actual launch component rejects an uncharged launch without sound.");
        Field(launcher, "<Voltage>k__BackingField").SetValue(launcher, 50f);
        launcher.RequestLaunch();
        launcher.SendMessage("FixedUpdate", SendMessageOptions.RequireReceiver);
        CheckEmitted("successful-actual-voltage-launch-plays-sfx", SfxCue.Launch, previous, false);
        CheckResult("launch-sound-requires-real-success", launcher.HasLaunchSample && launcher.LastLaunchVoltage == 50f,
            "A real successful 50 V impulse, rather than a bare request, emitted Launch.");
        launcher.enabled = false;
        ElectricGoal2D goal = FindInScene<ElectricGoal2D>();
        ElectricLearningController2D lesson = FindInScene<ElectricLearningController2D>();
        Require(goal != null && lesson != null && ball.Stars >= goal.RequiredStars, "Accepted goal fixture lacks saved bindings or collected star.");
        Field(lesson, "<ContactChallengeSatisfied>k__BackingField").SetValue(lesson, true);
        previous = CueTime(SfxCue.StageClear);
        goal.SendMessage("TryClear", ball.GetComponent<Collider2D>(), SendMessageOptions.RequireReceiver);
        CheckEmitted("saved-goal-accepted-callback-plays-clear-on-ui-source", SfxCue.StageClear, previous, true);
        CheckResult("accepted-goal-retains-existing-one-shot-clear-guard", goal.IsCleared && !ball.Body.simulated,
            "Lesson flag is an explicit unsaved fixture; the saved goal's normal clear guard and freeze path are exercised.");
        float clearAt = CueTime(SfxCue.StageClear);
        goal.SendMessage("TryClear", ball.GetComponent<Collider2D>(), SendMessageOptions.RequireReceiver);
        CheckResult("duplicate-accepted-goal-does-not-replay-sfx", Same(CueTime(SfxCue.StageClear), clearAt),
            "A second callback cannot emit a second clear sound.");
        ball.enabled = true;
        ball.Body.simulated = true;
        ball.Body.position = new Vector2(-50f, 10f);
        ball.Body.constraints = RigidbodyConstraints2D.FreezeAll;
        ball.Body.linearVelocity = Vector2.zero;
        Physics2D.SyncTransforms();
    }

    private static void CheckEmitted(string name, SfxCue cue, float before, bool ui, bool requireCurrentlyPlaying = true)
    {
        AudioSource source = PlayingSource(cue, ui);
        var definition = Definition(cue);
        if (source == null && !requireCurrentlyPlaying)
            foreach (AudioSource candidate in Reference<AudioSource[]>(Sfx(), "gameplaySources"))
                if (candidate != null && candidate.clip == definition.clip) { source = candidate; break; }
        CheckResult(name, Changed(CueTime(cue), before) && source != null
            && (!requireCurrentlyPlaying || source.isPlaying)
            && Mathf.Abs(source.pitch - definition.pitch) < 0.0001f
            && Mathf.Abs(source.volume - Value<float>(Sfx(), "masterVolume") * definition.volume) < 0.0001f,
            "Matching assigned clip, configured pitch/volume and advanced unscaled cue timestamp; AudioSource.isPlaying="
            + (source != null && source.isPlaying) + ". " + (requireCurrentlyPlaying
                ? "Current playback is required for this directly dispatched event."
                : "A physics callback may finish its short clip before the next editor update; timestamp and assigned source remain observable."));
    }

    private static AudioSource PlayingSource(SfxCue cue, bool ui)
    {
        AudioClip clip = Definition(cue).clip;
        if (ui)
        {
            AudioSource source = Reference<AudioSource>(Sfx(), "uiSource");
            return source != null && source.isPlaying && source.clip == clip ? source : null;
        }
        foreach (AudioSource source in Reference<AudioSource[]>(Sfx(), "gameplaySources"))
            if (source != null && source.isPlaying && source.clip == clip) return source;
        return null;
    }
    private static ElectricSfxPlayer.CueDefinition Definition(SfxCue cue)
    {
        foreach (var candidate in Reference<ElectricSfxPlayer.CueDefinition[]>(Sfx(), "cues"))
            if (candidate.cue == cue) return candidate;
        throw new InvalidOperationException("Missing authored cue " + cue + ".");
    }
    private static float CueTime(SfxCue cue) => Reference<float[]>(Sfx(), "lastPlayedTimes")[(int)cue];
    private static bool Changed(float current, float before) => !float.IsNegativeInfinity(current) && current != before;
    private static bool Same(float a, float b) => a == b || float.IsNaN(a) && float.IsNaN(b);
    private static bool AllGameplayStopped()
    {
        foreach (AudioSource source in Reference<AudioSource[]>(Sfx(), "gameplaySources")) if (source != null && source.isPlaying) return false;
        return true;
    }
    private static int CountActiveSfx()
    {
        int count = 0;
        foreach (ElectricSfxPlayer sfx in Object.FindObjectsByType<ElectricSfxPlayer>(FindObjectsSortMode.None)) if (sfx.isActiveAndEnabled) count++;
        return count;
    }
    private static int CountListeners()
    {
        int count = 0;
        foreach (AudioListener listener in Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None)) if (listener.isActiveAndEnabled) count++;
        return count;
    }
    private static int ActiveListenersInScene()
    {
        int count = 0;
        foreach (AudioListener listener in ComponentsInScene<AudioListener>()) if (listener.isActiveAndEnabled) count++;
        return count;
    }
    private static bool LegacyMusicOwnersInactive()
    {
        foreach (MonoBehaviour component in ComponentsInScene<MonoBehaviour>())
            if (component != null && component.GetType().Name == "SoundManager" && component.gameObject.activeInHierarchy) return false;
        return true;
    }
    private static bool NoRuntimeBgm()
    {
        foreach (AudioSource source in Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None))
            if (source.isActiveAndEnabled && (source.loop || source.gameObject.name.IndexOf("BGM", StringComparison.OrdinalIgnoreCase) >= 0)) return false;
        return LegacyMusicOwnersInactive();
    }
    private static void CheckPersistence(string suffix)
    {
        ElectricSfxPlayer sfx = Sfx();
        CheckResult("persistent-fixed-audio-pool-" + suffix, CountActiveSfx() == 1 && sfx.GetInstanceID() == initialServiceId
            && sfx.GetComponentsInChildren<AudioSource>(true).Length == initialSourceCount && initialSourceCount == 7 && CountListeners() == 1 && NoRuntimeBgm(),
            "Same persistent SFX instance and seven saved sources, one listener, no runtime music node or duplicate service.");
    }

    private static void PrepareLoad(string target)
    {
        SessionState.SetString(Prefix + "ExpectedScene", target);
        SessionState.SetBool(Prefix + "Loaded", false);
        SessionState.SetBool(Prefix + "FadeOut", false);
        SessionState.SetBool(Prefix + "FadeIn", false);
    }
    private static bool AwaitLoad(string expected)
    {
        SceneTransitionController service = Object.FindFirstObjectByType<SceneTransitionController>();
        CanvasGroup overlay = Reference<CanvasGroup>(service, "fadeOverlay");
        if (SceneTransitionController.IsBusy)
        {
            Require(Time.timeScale == 0f, "A scene transition unexpectedly resumed gameplay time.");
            if (overlay.alpha > 0.01f && overlay.alpha < 0.99f)
                SessionState.SetBool(Prefix + (SessionState.GetBool(Prefix + "Loaded", false) ? "FadeIn" : "FadeOut"), true);
            return false;
        }
        if (SceneManager.GetActiveScene().path != expected) return false;
        CheckResult("async-fade-and-unscaled-audio-owner-" + expected + "-" + (Phase)SessionState.GetInt(Prefix + "Phase", 0),
            SessionState.GetBool(Prefix + "Loaded", false) && SessionState.GetBool(Prefix + "FadeOut", false)
            && SessionState.GetBool(Prefix + "FadeIn", false) && overlay.alpha == 0f && Time.timeScale == 1f,
            "Real sceneLoaded callback occurred behind opaque fade; intermediate outgoing and incoming fade alphas were observed.");
        return true;
    }
    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!SessionState.GetBool(Prefix + "Active", false) || !EditorApplication.isPlaying) return;
        SceneTransitionController service = Object.FindFirstObjectByType<SceneTransitionController>();
        CanvasGroup overlay = service != null ? Reference<CanvasGroup>(service, "fadeOverlay") : null;
        Report report = Load();
        report.sceneLoads.Add(new SceneLoad { path = scene.path, busy = SceneTransitionController.IsBusy,
            alpha = overlay != null ? overlay.alpha : -1f, timeScale = Time.timeScale });
        Save(report);
        if (scene.path != SessionState.GetString(Prefix + "ExpectedScene", "")) return;
        SessionState.SetBool(Prefix + "Loaded", true);
        CheckResult("incoming-scene-activates-behind-opaque-fade-" + scene.path + "-" + (Phase)SessionState.GetInt(Prefix + "Phase", 0),
            overlay != null && overlay.alpha >= 0.999f && SceneTransitionController.IsBusy && Time.timeScale == 0f,
            "Observed from the actual SceneManager.sceneLoaded callback.");
    }

    private static void SetupKeyboard()
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
        ReleaseKeyboard();
    }
    private static void ReleaseKeyboard()
    {
        if (probeKeyboard == null || !probeKeyboard.added) return;
        InputSystem.QueueStateEvent(probeKeyboard, new KeyboardState());
        PumpInput();
    }
    private static void PumpInput()
    {
        MethodInfo update = typeof(InputSystem).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Static,
            null, new[] { typeof(InputUpdateType) }, null);
        Require(update != null, "Input System has no Dynamic event dispatcher.");
        update.Invoke(null, new object[] { InputUpdateType.Dynamic });
    }
    private static void RestoreKeyboard()
    {
        if (probeKeyboard == null) return;
        if (probeKeyboard.added) InputSystem.RemoveDevice(probeKeyboard);
        probeKeyboard = null;
        if (previousKeyboard != null && previousKeyboard.added) previousKeyboard.MakeCurrent();
        InputSystem.settings.editorInputBehaviorInPlayMode = previousEditorInput;
        InputSystem.settings.backgroundBehavior = previousBackground;
        InputSystem.settings.updateMode = previousUpdateMode;
        Application.runInBackground = previousRunInBackground;
    }

    private static ElectricSfxPlayer Sfx()
    {
        foreach (ElectricSfxPlayer sfx in Object.FindObjectsByType<ElectricSfxPlayer>(FindObjectsSortMode.None))
            if (sfx.isActiveAndEnabled && sfx.gameObject.scene.name == "DontDestroyOnLoad") return sfx;
        return FindInScene<ElectricSfxPlayer>();
    }
    private static ElectricBall2D Ball()
    {
        ElectricBall2D ball = FindInScene<ElectricBall2D>();
        Require(ball != null, "Current gameplay scene has no saved ElectricBall2D.");
        return ball;
    }
    private static T FindInScene<T>() where T : Component
    {
        foreach (T component in ComponentsInScene<T>()) return component;
        return null;
    }
    private static IEnumerable<T> ComponentsInScene<T>() where T : Component
    {
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            foreach (T component in root.GetComponentsInChildren<T>(true)) yield return component;
    }
    private static FieldInfo Field(object owner, string name)
    {
        FieldInfo field = owner?.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Require(field != null, "Missing fixture/serialized field " + name + ".");
        return field;
    }
    private static T Reference<T>(object owner, string name) where T : class => Field(owner, name).GetValue(owner) as T;
    private static T Value<T>(object owner, string name) => (T)Field(owner, name).GetValue(owner);
    private static void Click(Button button)
    {
        Require(button != null && button.IsActive() && button.IsInteractable() && EventSystem.current != null,
            "Saved button is unavailable: " + (button != null ? button.name : "missing"));
        button.onClick.Invoke();
    }
    private static bool After(double seconds) => EditorApplication.timeSinceStartup - SessionState.GetFloat(Prefix + "PhaseStarted", 0f) >= seconds;
    private static void SetPhase(Phase phase)
    {
        SessionState.SetInt(Prefix + "Phase", (int)phase);
        SessionState.SetFloat(Prefix + "PhaseStarted", (float)EditorApplication.timeSinceStartup);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static Report Load() => JsonUtility.FromJson<Report>(File.ReadAllText(ReportPath));
    private static void Save(Report report)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
        File.WriteAllText(ReportPath, JsonUtility.ToJson(report, true));
    }
    private static void CheckResult(string name, bool passed, string details)
    {
        Report report = Load();
        report.checks.Add(new Check { name = name, passed = passed, details = details });
        Save(report);
        Require(passed, "Failed check: " + name + ". " + details);
    }
    private static void OnLog(string message, string stack, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        Report report = Load();
        report.errors.Add(message + "\n" + stack);
        Save(report);
    }
    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (SessionState.GetBool(Prefix + "Active", false) && state == PlayModeStateChange.EnteredEditMode)
            Fail(new InvalidOperationException("Play Mode ended before SFX verification completed."));
    }
    private static void Fail(Exception error)
    {
        Report report = Load();
        report.errors.Add(error.ToString());
        Save(report);
        Finish();
    }
    private static void Finish()
    {
        if (!SessionState.GetBool(Prefix + "Active", false)) return;
        SessionState.SetBool(Prefix + "Active", false);
        RestoreKeyboard();
        Report report = Load();
        bool passed = report.errors.Count == 0 && (Phase)SessionState.GetInt(Prefix + "Phase", 0) == Phase.Finish && report.checks.Count >= 45;
        foreach (Check check in report.checks) passed &= check.passed;
        report.status = passed ? "passed" : "failed";
        Save(report);
        SessionState.SetInt(Prefix + "ExitCode", passed ? 0 : 1);
        SessionState.SetBool(Prefix + "Exiting", true);
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        Application.logMessageReceived -= OnLog;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        Time.timeScale = 1f;
        if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
        EditorApplication.update -= ExitWhenReady;
        EditorApplication.update += ExitWhenReady;
    }
    private static void ExitWhenReady()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        SessionState.SetBool(Prefix + "Exiting", false);
        EditorApplication.Exit(SessionState.GetInt(Prefix + "ExitCode", 1));
    }
}
