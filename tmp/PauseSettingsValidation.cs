// Temporary integration harness: copy into Assets/Editor only in the isolated clone.
// Unity -batchmode -force-audio -executeMethod PauseSettingsValidation.RunBatch (omit -quit).
// No saved gameplay data is changed. Only the two SFX preference keys are backed up/restored.
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
public static class PauseSettingsValidation
{
    private const string Prefix = "Codex.PauseSettingsValidation.";
    private const string MenuPath = "Assets/@Project/@sung_wook/@Game/00_Scenes/MainMenu.unity";
    private const string StagePath = "Assets/@Project/@sung_wook/@Game/00_Scenes/Stage/Stage_02.unity";
    private const string Output = "C:/Users/wook1/GitBub-Unity/Science-Project-2026/Logs/PauseSettingsValidation.json";
    private enum Phase { EnterPlay, Menu, FirstStage, SettingsPaused, AfterResume, Restart, ReturnMenu, SecondStage, Finish }
    [Serializable] private sealed class Check { public string name; public bool passed; public string details; }
    [Serializable] private sealed class Backup
    {
        public bool hasVolume; public float volume; public bool hasMute; public int mute;
        public string company; public string product; public bool restored;
    }
    [Serializable] private sealed class Report
    {
        public string status = "running";
        public string mode = "Actual authored main-menu and pause buttons, shared SFX Slider/Toggle events, virtual Esc handled by the actual controller Update, paused gameplay inputs/physics, current-stage restart and menu return through real async fades. An unsaved released-ball fixture verifies pause only; no stage-clear claim. The original presence and values of only the two SFX preference keys are restored after Play Mode teardown.";
        public List<Check> checks = new List<Check>();
        public List<string> errors = new List<string>();
    }
    private static string BackupPath => Path.GetFullPath(Path.Combine(Application.dataPath, "../tmp/PauseSettingsPlayerPrefsBackup.json"));
    private static Keyboard probeKeyboard, previousKeyboard;
    private static InputSettings.EditorInputBehaviorInPlayMode previousEditorInput;
    private static InputSettings.BackgroundBehavior previousBackground;
    private static InputSettings.UpdateMode previousUpdateMode;
    private static bool previousRunInBackground;
    private static readonly Dictionary<Behaviour, bool> inputStates = new Dictionary<Behaviour, bool>();
    private static Vector2 pausedPosition, pausedVelocity;
    private static float pausedFixedTime, pausedCharge;
    private static int pausedStars, initialSfxId, initialTransitionId, originalBallId, changes;

    static PauseSettingsValidation()
    {
        if (!Application.isBatchMode) return;
        if (SessionState.GetBool(Prefix + "Active", false)) ResumeCallbacks();
        if (SessionState.GetBool(Prefix + "Exiting", false)) EditorApplication.update += ExitWhenReady;
    }
    public static void RunBatch()
    {
        RequireIsolated();
        Save(new Report());
        SessionState.SetBool(Prefix + "Active", true);
        SessionState.SetBool(Prefix + "Exiting", false);
        SessionState.SetBool(Prefix + "EnterRequested", false);
        SessionState.SetString(Prefix + "StartedUtc", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        try
        {
            Require(!File.Exists(BackupPath) || JsonUtility.FromJson<Backup>(File.ReadAllText(BackupPath)).restored,
                "An unrestored pause-settings preference backup exists; run RestoreBackupBatch first.");
            var backup = new Backup { hasVolume = PlayerPrefs.HasKey(SfxPreferences.VolumeKey), volume = PlayerPrefs.GetFloat(SfxPreferences.VolumeKey, 1f),
                hasMute = PlayerPrefs.HasKey(SfxPreferences.MuteKey), mute = PlayerPrefs.GetInt(SfxPreferences.MuteKey, 0),
                company = Application.companyName, product = Application.productName };
            Directory.CreateDirectory(Path.GetDirectoryName(BackupPath));
            File.WriteAllText(BackupPath, JsonUtility.ToJson(backup, true));
            PlayerPrefs.SetFloat(SfxPreferences.VolumeKey, .62f);
            PlayerPrefs.SetInt(SfxPreferences.MuteKey, 0);
            PlayerPrefs.Save();
            CheckResult("fixture-preserves-company-product-and-backs-up-only-two-sfx-keys", true,
                "No DeleteAll, project identity change, unrelated preference access, or gameplay asset saves.");
            for (int stage = 1; stage <= 5; stage++) InspectSavedStage(stage);
            EditorSceneManager.OpenScene(MenuPath, OpenSceneMode.Single);
            SetPhase(Phase.EnterPlay);
            ResumeCallbacks();
        }
        catch (Exception error) { Fail(error); }
    }
    public static void RestoreBackupBatch()
    {
        RequireIsolated();
        RestoreOriginalPreferences();
        EditorApplication.Exit(0);
    }
    private static void RequireIsolated() => Require(Application.isBatchMode
        && Application.dataPath.Replace('\\', '/').Contains("/Temp/CodexUnityVisualBuild/"),
        "Pause settings verification must run in the isolated batch project only.");

    private static void InspectSavedStage(int stage)
    {
        EditorSceneManager.OpenScene(ElectricStageProgression.StageScenePath(stage), OpenSceneMode.Single);
        PauseMenuController pause = FindInScene<PauseMenuController>();
        Require(pause != null, "Saved stage has no pause controller: " + stage);
        GameObject root = Reference<GameObject>(pause, "menuRoot");
        GameObject settings = Reference<GameObject>(pause, "settingsWindow");
        SfxSettingsView view = settings != null ? settings.GetComponentInChildren<SfxSettingsView>(true) : null;
        Button open = Reference<Button>(pause, "settingsButton");
        Button close = Reference<Button>(pause, "closeSettingsButton");
        CheckResult("stage-" + stage + "-pause-settings-saved-and-closed", root != null && settings != null && !root.activeSelf && !settings.activeSelf
            && pause.isActiveAndEnabled && settings.transform.parent == root.transform.parent && view != null
            && open != null && open.transform.IsChildOf(root.transform) && close != null && close.transform.IsChildOf(settings.transform),
            "Pause listener remains active; settings is a saved sibling panel with the shared SfxSettingsView and both button references.");
        Slider slider = Reference<Slider>(view, "volumeSlider");
        Toggle mute = Reference<Toggle>(view, "muteToggle");
        CheckResult("stage-" + stage + "-shared-slider-toggle-are-wired", slider != null && mute != null && Reference<Text>(view, "volumeValueText") != null
            && Near(slider.minValue, 0f) && Near(slider.maxValue, 1f) && !slider.wholeNumbers,
            "Saved normalized 0–1 Slider, percentage Text, and mute Toggle resolve in the stage prefab instance.");
        bool colors = true;
        foreach (Graphic graphic in settings.GetComponentsInChildren<Graphic>(true)) colors &= HasOriginalStyle(graphic);
        foreach (Graphic graphic in open.GetComponentsInChildren<Graphic>(true)) colors &= HasOriginalStyle(graphic);
        CheckResult("stage-" + stage + "-settings-keep-default-assets-and-colors", colors && open.transition == Selectable.Transition.None
            && close.transition == Selectable.Transition.None && slider.transition == Selectable.Transition.None && mute.transition == Selectable.Transition.None,
            "Images retain white tint and existing builtin sprites; text retains Unity RGB 50/255; controls have no selectable recoloring.");
    }
    private static bool HasOriginalStyle(Graphic graphic)
    {
        Color expected = graphic is Text ? new Color(50f / 255f, 50f / 255f, 50f / 255f, 1f) : Color.white;
        bool result = Near(graphic.color.r, expected.r) && Near(graphic.color.g, expected.g) && Near(graphic.color.b, expected.b) && Near(graphic.color.a, expected.a);
        if (graphic is Image image && image.sprite != null) result &= AssetDatabase.GetAssetPath(image.sprite) == "Resources/unity_builtin_extra";
        return result;
    }
    private static void ResumeCallbacks()
    {
        EditorApplication.update -= Tick; EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged -= PlayModeChanged; EditorApplication.playModeStateChanged += PlayModeChanged;
        Application.logMessageReceived -= OnLog; Application.logMessageReceived += OnLog;
        SceneManager.sceneLoaded -= SceneLoaded; SceneManager.sceneLoaded += SceneLoaded;
    }
    private static void Tick()
    {
        if (!SessionState.GetBool(Prefix + "Active", false)) return;
        try
        {
            DateTime started = DateTime.Parse(SessionState.GetString(Prefix + "StartedUtc", ""), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            Require((DateTime.UtcNow - started).TotalSeconds < 180d, "Pause settings verification exceeded its 180-second watchdog.");
            Require(Load().errors.Count == 0, "Unity logged an error during pause settings verification.");
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
            if (phase == Phase.Menu)
            {
                if (SceneTransitionController.IsBusy) return;
                MainMenuController menu = FindInScene<MainMenuController>();
                if (menu == null || !Reference<Button>(menu, "startButton").IsInteractable()) return;
                initialSfxId = Sfx().GetInstanceID(); initialTransitionId = Service().GetInstanceID();
                Click(Reference<Button>(menu, "settingsButton"));
                CheckUi(.62f, false, "main-menu-reads-seeded-shared-preferences");
                CheckResult("main-settings-has-one-listener", ViewListenerCount() == 1, "Exactly one enabled SfxSettingsView listens to shared preferences.");
                Click(Reference<Button>(menu, "closeSettingsButton"));
                CheckResult("closed-main-settings-unsubscribes", ViewListenerCount() == 0, "Closing the main panel removes its preference listener.");
                PrepareLoad(StagePath, false);
                Click(Reference<Button>(menu, "startButton"));
                CheckAcceptedLoad("start-stage");
                SetPhase(Phase.FirstStage);
                return;
            }
            if (phase == Phase.FirstStage)
            {
                if (!AwaitLoad(StagePath) || !StageReady()) return;
                CheckPersistence(.62f, false, "first-stage");
                SetupKeyboard();
                PreparePausedFixture();
                PauseMenuController pause = Pause();
                Click(Reference<Button>(pause, "settingsButton"));
                CheckUi(.62f, false, "pause-settings-share-main-menu-values");
                CheckResult("settings-replaces-only-pause-panel-and-stays-paused", pause.IsPaused && pause.IsSettingsOpen && Time.timeScale == 0f
                    && !Reference<GameObject>(pause, "menuRoot").activeSelf && Reference<GameObject>(pause, "settingsWindow").activeInHierarchy,
                    "Opening settings leaves gameplay frozen and displays the saved sibling panel.");
                changes = 0; SfxPreferences.Changed += CountChanged;
                try { Slider().value = .28f; Mute().isOn = true; }
                finally { SfxPreferences.Changed -= CountChanged; }
                CheckUi(.28f, true, "pause-controls-edit-live-shared-preferences");
                CheckResult("paused-controls-emit-once-and-save-both-values", changes == 2 && SavedValues(.28f, true),
                    "One slider edit and one toggle edit immediately persist exactly two shared changes.");
                CheckResult("all-seven-sources-update-gain-and-mute-during-pause", SourcesMatch(.28f, true),
                    "Six paused gameplay sources and the existing UI source apply the same shared 28% volume and mute immediately.");
                pause.Resume(); pause.RestartStage(); pause.ReturnToMainMenu();
                CheckResult("hidden-resume-restart-and-menu-methods-refuse-settings-navigation", !SceneTransitionController.IsBusy && pause.IsSettingsOpen
                    && pause.IsPaused && Time.timeScale == 0f && SceneManager.GetActiveScene().path == StagePath,
                    "Public navigation methods cannot bypass the open settings panel, resume gameplay, or start an unseen scene load.");
                InputSystem.QueueStateEvent(probeKeyboard, new KeyboardState(Key.A, Key.D, Key.Space, Key.R, Key.E, Key.B, Key.T));
                PumpDynamicInput();
                CheckResult("held-gameplay-keys-reach-input-system", probeKeyboard.spaceKey.isPressed && probeKeyboard.rKey.isPressed
                    && probeKeyboard.aKey.isPressed && probeKeyboard.eKey.isPressed,
                    "The input fixture holds movement, charge, restart, lesson and launch keys across real paused frames.");
                SessionState.SetInt(Prefix + "PausedFrame", Time.frameCount);
                SetPhase(Phase.SettingsPaused);
                return;
            }
            if (phase == Phase.SettingsPaused)
            {
                if (!After(.3d) || Time.frameCount <= SessionState.GetInt(Prefix + "PausedFrame", 0) + 2) return;
                PauseMenuController pause = Pause(); ElectricBall2D ball = Ball();
                CheckResult("settings-freezes-physics-and-blocks-gameplay-input", pause.IsPaused && pause.IsSettingsOpen && Time.timeScale == 0f
                    && Near(Time.fixedTime, pausedFixedTime) && Vector2.Distance(ball.Body.position, pausedPosition) < .0001f
                    && Vector2.Distance(ball.Body.linearVelocity, pausedVelocity) < .0001f && Near(ball.SignedCharge, pausedCharge) && ball.Stars == pausedStars
                    && InputsSuspended(), "Across at least three Play Mode frames and 0.3 seconds, physics, charge, star count and disabled gameplay input remain unchanged.");
                ReleaseKeyboard();
                Click(Reference<Button>(pause, "closeSettingsButton"));
                CheckPausedMenu("saved-back-button-returns-to-pause");
                CheckResult("back-unsubscribes-shared-settings-view", ViewListenerCount() == 0,
                    "Returning to the pause list removes the settings subscription without resuming gameplay.");
                for (int reopen = 0; reopen < 3; reopen++)
                {
                    Click(Reference<Button>(pause, "settingsButton"));
                    CheckUi(.28f, true, "reopened-pause-settings-" + reopen);
                    CheckResult("reopened-pause-view-subscribes-once-" + reopen, ViewListenerCount() == 1,
                        "Repeated enable/disable never duplicates shared preference listeners.");
                    if (reopen < 2) Click(Reference<Button>(pause, "closeSettingsButton"));
                }
                DispatchEscape(pause);
                CheckPausedMenu("first-escape-closes-settings-without-resuming");
                CheckResult("settings-return-focuses-settings-button", EventSystem.current.currentSelectedGameObject
                    == Reference<Button>(pause, "settingsButton").gameObject, "Esc returns keyboard focus to the saved Settings button.");
                DispatchEscape(pause);
                CheckResult("second-escape-resumes-gameplay", !pause.IsPaused && !pause.IsSettingsOpen && Time.timeScale == 1f
                    && !Reference<GameObject>(pause, "menuRoot").activeSelf && !Reference<GameObject>(pause, "settingsWindow").activeSelf
                    && InputsRestored() && ViewListenerCount() == 0, "The next Esc closes the pause list, restores captured input states and releases gameplay time.");
                SessionState.SetInt(Prefix + "ResumedFrame", Time.frameCount);
                SetPhase(Phase.AfterResume);
                return;
            }
            if (phase == Phase.AfterResume)
            {
                if (!After(.15d) || Time.frameCount <= SessionState.GetInt(Prefix + "ResumedFrame", 0) + 2) return;
                CheckResult("resumed-gameplay-physics-advances", Time.fixedTime > pausedFixedTime && Vector2.Distance(Ball().Body.position, pausedPosition) > .0001f,
                    "The same released ball advances naturally again after settings and pause close.");
                PauseMenuController pause = Pause(); DispatchEscape(pause);
                Click(Reference<Button>(pause, "settingsButton")); CheckUi(.28f, true, "before-restart-settings-retain-values");
                Click(Reference<Button>(pause, "closeSettingsButton"));
                PrepareLoad(StagePath, true); Click(Reference<Button>(pause, "restartButton"));
                CheckAcceptedLoad("pause-current-stage-restart");
                AsyncOperation first = Reference<AsyncOperation>(Service(), "pendingLoad");
                Reference<Button>(pause, "restartButton").onClick.Invoke();
                CheckResult("restart-double-invocation-keeps-one-operation", Reference<AsyncOperation>(Service(), "pendingLoad") == first,
                    "A duplicate invocation cannot replace the shared asynchronous restart.");
                SetPhase(Phase.Restart);
                return;
            }
            if (phase == Phase.Restart)
            {
                if (!AwaitLoad(StagePath) || !StageReady()) return;
                PauseMenuController pause = Pause();
                CheckResult("restart-creates-fresh-stage-and-closed-panels", Ball().GetInstanceID() != originalBallId && Ball().Stars == 0
                    && !pause.IsPaused && !pause.IsSettingsOpen && !Reference<GameObject>(pause, "menuRoot").activeSelf
                    && !Reference<GameObject>(pause, "settingsWindow").activeSelf && ViewListenerCount() == 0,
                    "Restart resets the attempt and both UI panels while retaining saved audio preferences.");
                CheckPersistence(.28f, true, "after-current-stage-restart");
                DispatchEscape(pause); Click(Reference<Button>(pause, "settingsButton"));
                CheckUi(.28f, true, "fresh-stage-settings-read-shared-values");
                Click(Reference<Button>(pause, "closeSettingsButton"));
                PrepareLoad(MenuPath, true); Click(Reference<Button>(pause, "mainMenuButton"));
                CheckAcceptedLoad("pause-return-to-main-menu");
                SetPhase(Phase.ReturnMenu);
                return;
            }
            if (phase == Phase.ReturnMenu)
            {
                if (!AwaitLoad(MenuPath)) return;
                MainMenuController menu = FindInScene<MainMenuController>();
                if (menu == null || !Reference<Button>(menu, "settingsButton").IsInteractable()) return;
                CheckPersistence(.28f, true, "returned-main-menu");
                CheckResult("main-menu-return-clears-gameplay-pause", FindInScene<PauseMenuController>() == null && Time.timeScale == 1f && !menu.IsSettingsOpen,
                    "The previous stage and its pause panels leave no persistent pause owner.");
                Click(Reference<Button>(menu, "settingsButton"));
                CheckUi(.28f, true, "main-menu-sees-in-game-edits");
                changes = 0; SfxPreferences.Changed += CountChanged;
                try { Slider().value = .46f; Mute().isOn = false; }
                finally { SfxPreferences.Changed -= CountChanged; }
                CheckUi(.46f, false, "main-menu-edits-shared-values-again");
                CheckResult("main-menu-change-is-live-on-same-audio-pool", changes == 2 && SourcesMatch(.46f, false) && SavedValues(.46f, false),
                    "Main menu edits the same two saved settings and seven persistent sources, with no duplicate change callbacks.");
                Click(Reference<Button>(menu, "closeSettingsButton"));
                PrepareLoad(StagePath, false); Click(Reference<Button>(menu, "startButton"));
                CheckAcceptedLoad("start-after-main-menu-edit"); SetPhase(Phase.SecondStage);
                return;
            }
            if (phase == Phase.SecondStage)
            {
                if (!AwaitLoad(StagePath) || !StageReady()) return;
                CheckPersistence(.46f, false, "second-start-keeps-shared-main-menu-edits");
                PauseMenuController pause = Pause(); DispatchEscape(pause);
                Click(Reference<Button>(pause, "settingsButton"));
                CheckUi(.46f, false, "in-game-settings-see-new-main-menu-edits");
                Click(Reference<Button>(pause, "closeSettingsButton"));
                Click(Reference<Button>(pause, "resumeButton"));
                CheckResult("saved-continue-works-after-settings", !pause.IsPaused && !pause.IsSettingsOpen && Time.timeScale == 1f && ViewListenerCount() == 0,
                    "The actual Continue button remains functional after returning from settings.");
                SetPhase(Phase.Finish); Finish(true);
            }
        }
        catch (Exception error) { Fail(error); }
    }

    private static void PreparePausedFixture()
    {
        ElectricBall2D ball = Ball(); originalBallId = ball.GetInstanceID();
        ElectricLearningController2D lesson = FindInScene<ElectricLearningController2D>();
        if (lesson != null && lesson.IsPreparingTrial) lesson.SendMessage("ReleaseTrial", SendMessageOptions.RequireReceiver);
        ball.Body.bodyType = RigidbodyType2D.Dynamic; ball.Body.simulated = true;
        ball.Body.position = new Vector2(-5f, 4f); ball.Body.linearVelocity = new Vector2(.6f, -.2f);
        ball.SetChargeMagnitude(.75f); ball.SetPositive(); ball.CollectStar();
        inputStates.Clear(); foreach (Behaviour input in InputComponents()) inputStates[input] = input.enabled;
        PauseMenuController pause = Pause(); DispatchEscape(pause);
        CheckResult("actual-escape-opens-pause-with-gameplay-input-suspended", pause.IsPaused && Time.timeScale == 0f && InputsSuspended()
            && Reference<GameObject>(pause, "menuRoot").activeInHierarchy && !pause.IsSettingsOpen,
            "Virtual Esc reaches the InputSystem and the actual PauseMenuController.Update handler opens the saved pause panel.");
        pausedPosition = ball.Body.position; pausedVelocity = ball.Body.linearVelocity;
        pausedFixedTime = Time.fixedTime; pausedCharge = ball.SignedCharge; pausedStars = ball.Stars;
    }
    private static void CheckPausedMenu(string name)
    {
        PauseMenuController pause = Pause();
        CheckResult(name, pause.IsPaused && !pause.IsSettingsOpen && Time.timeScale == 0f && InputsSuspended()
            && Reference<GameObject>(pause, "menuRoot").activeInHierarchy && !Reference<GameObject>(pause, "settingsWindow").activeSelf,
            "Only the pause list is visible; gameplay time and captured input remain suspended.");
    }
    private static IEnumerable<Behaviour> InputComponents()
    {
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            foreach (Behaviour input in root.GetComponentsInChildren<Behaviour>(true))
                if (input is PlayerController || input is PlayerInputReader || input is ElectricStageInput2D || input is ElectricLearningInput2D
                    || input is GearLeverController || input is global::LeverReader || input is global::LeverMover
                    || input is DistanceLauncher2D || input is VoltageLauncher2D) yield return input;
    }
    private static bool InputsSuspended()
    { int count = 0; foreach (var pair in inputStates) if (pair.Key != null && pair.Key.gameObject.activeInHierarchy) { count++; if (pair.Key.enabled) return false; } return count >= 5; }
    private static bool InputsRestored()
    { foreach (var pair in inputStates) if (pair.Key != null && pair.Key.enabled != pair.Value) return false; return inputStates.Count >= 5; }
    private static bool StageReady() { ElectricStageSession2D session = FindInScene<ElectricStageSession2D>(); return session != null && session.IsInitialized; }
    private static void CheckUi(float volume, bool muted, string name)
    {
        SfxSettingsView view = FindInScene<SfxSettingsView>();
        CheckResult(name, view != null && view.isActiveAndEnabled && Near(Slider().value, volume) && Mute().isOn == muted
            && Reference<Text>(view, "volumeValueText").text == Mathf.RoundToInt(volume * 100f) + "%"
            && Near(SfxPreferences.Volume, volume) && SfxPreferences.IsMuted == muted,
            "Saved Slider, percent label, mute Toggle and shared preference agree: " + Mathf.RoundToInt(volume * 100f) + "%, muted=" + muted + ".");
    }
    private static int ViewListenerCount()
    {
        FieldInfo field = typeof(SfxPreferences).GetField("Changed", BindingFlags.NonPublic | BindingFlags.Static);
        Require(field != null, "Missing shared preferences event backing field.");
        Delegate callback = field.GetValue(null) as Delegate; int count = 0;
        if (callback != null) foreach (Delegate entry in callback.GetInvocationList()) if (entry.Target is SfxSettingsView) count++;
        return count;
    }
    private static bool SavedValues(float volume, bool muted) => Near(PlayerPrefs.GetFloat(SfxPreferences.VolumeKey, -1f), volume)
        && PlayerPrefs.GetInt(SfxPreferences.MuteKey, -1) == (muted ? 1 : 0);
    private static bool SourcesMatch(float volume, bool muted)
    {
        ElectricSfxPlayer sfx = Sfx();
        Dictionary<AudioSource, float> gains = Reference<Dictionary<AudioSource, float>>(sfx, "sourceVolumes");
        float master = Value<float>(sfx, "masterVolume"); int count = 0;
        foreach (AudioSource source in Sources())
        { float gain = 0f; gains.TryGetValue(source, out gain); if (source == null || source.mute != muted || !Near(source.volume, Mathf.Clamp01(master * gain * volume))) return false; count++; }
        return count == 7;
    }
    private static IEnumerable<AudioSource> Sources()
    { foreach (AudioSource source in Reference<AudioSource[]>(Sfx(), "gameplaySources")) yield return source; yield return Reference<AudioSource>(Sfx(), "uiSource"); }
    private static void CheckPersistence(float volume, bool muted, string name)
    {
        CheckResult(name + "-uses-same-persistent-service-and-preferences", Sfx().GetInstanceID() == initialSfxId
            && Service().GetInstanceID() == initialTransitionId && Object.FindObjectsByType<ElectricSfxPlayer>(FindObjectsSortMode.None).Length == 1
            && Object.FindObjectsByType<SceneTransitionController>(FindObjectsSortMode.None).Length == 1
            && Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None).Length == 7
            && Near(SfxPreferences.Volume, volume) && SfxPreferences.IsMuted == muted && SavedValues(volume, muted) && SourcesMatch(volume, muted),
            "One original transition service and seven-source SFX pool retain the same saved volume and mute across the real scene change.");
    }
    private static void PrepareLoad(string target, bool mute)
    {
        SessionState.SetString(Prefix + "Target", target); SessionState.SetBool(Prefix + "ExpectedMute", mute);
        SessionState.SetBool(Prefix + "Loaded", false); SessionState.SetBool(Prefix + "FadeOut", false); SessionState.SetBool(Prefix + "FadeIn", false);
    }
    private static void CheckAcceptedLoad(string name)
    {
        AsyncOperation load = Reference<AsyncOperation>(Service(), "pendingLoad");
        CheckResult(name + "-starts-real-async-load", SceneTransitionController.IsBusy && load != null && !load.allowSceneActivation && Time.timeScale == 0f,
            "The saved button starts the actual asynchronous scene operation with activation delayed behind the shared fade.");
    }
    private static bool AwaitLoad(string path)
    {
        SceneTransitionController service = Service(); if (service == null) return false;
        CanvasGroup overlay = Reference<CanvasGroup>(service, "fadeOverlay");
        if (SceneTransitionController.IsBusy)
        {
            Require(Time.timeScale == 0f && overlay.blocksRaycasts, "Async transitions must retain time ownership and intercept UI through reveal.");
            if (overlay.alpha > .01f && overlay.alpha < .99f)
                SessionState.SetBool(Prefix + (SessionState.GetBool(Prefix + "Loaded", false) ? "FadeIn" : "FadeOut"), true);
            return false;
        }
        if (SceneManager.GetActiveScene().path != path || !SessionState.GetBool(Prefix + "Loaded", false)) return false;
        CheckResult("async-fade-completes-" + (Phase)SessionState.GetInt(Prefix + "Phase", 0), Near(Time.timeScale, 1f) && Near(overlay.alpha, 0f)
            && !overlay.blocksRaycasts && SessionState.GetBool(Prefix + "FadeOut", false) && SessionState.GetBool(Prefix + "FadeIn", false),
            "Observed intermediate fade-out, actual sceneLoaded behind opaque cover, intermediate fade-in and normal gameplay time restoration.");
        return true;
    }
    private static void SceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!SessionState.GetBool(Prefix + "Active", false) || !EditorApplication.isPlaying || scene.path != SessionState.GetString(Prefix + "Target", "")) return;
        SessionState.SetBool(Prefix + "Loaded", true);
        CanvasGroup overlay = Reference<CanvasGroup>(Service(), "fadeOverlay");
        bool mute = SessionState.GetBool(Prefix + "ExpectedMute", false); bool sources = true;
        foreach (AudioSource source in Sources()) sources &= source.mute == mute;
        CheckResult("incoming-scene-activates-under-fade-" + (Phase)SessionState.GetInt(Prefix + "Phase", 0), SceneTransitionController.IsBusy
            && Time.timeScale == 0f && overlay.alpha >= .999f && sources,
            "The real sceneLoaded callback sees opaque fade, frozen gameplay and the current shared mute state on existing sources.");
    }

    private static void SetupKeyboard()
    {
        previousKeyboard = Keyboard.current; previousEditorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
        previousBackground = InputSystem.settings.backgroundBehavior; previousUpdateMode = InputSystem.settings.updateMode;
        previousRunInBackground = Application.runInBackground;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
        Application.runInBackground = true; probeKeyboard = InputSystem.AddDevice<Keyboard>(); probeKeyboard.MakeCurrent(); ReleaseKeyboard();
    }
    private static void DispatchEscape(PauseMenuController controller)
    {
        ReleaseKeyboard(); InputSystem.QueueStateEvent(probeKeyboard, new KeyboardState(Key.Escape)); PumpDynamicInput();
        Require(probeKeyboard.escapeKey.wasPressedThisFrame, "Virtual Esc failed to reach the InputSystem keyboard.");
        controller.SendMessage("Update", SendMessageOptions.RequireReceiver); ReleaseKeyboard();
    }
    private static void ReleaseKeyboard()
    { if (probeKeyboard != null && probeKeyboard.added) { InputSystem.QueueStateEvent(probeKeyboard, new KeyboardState()); PumpDynamicInput(); } }
    private static void PumpDynamicInput()
    {
        MethodInfo update = typeof(InputSystem).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(InputUpdateType) }, null);
        Require(update != null, "Missing InputSystem Dynamic update dispatcher."); update.Invoke(null, new object[] { InputUpdateType.Dynamic });
    }
    private static void RestoreKeyboard()
    {
        if (probeKeyboard == null) return;
        if (probeKeyboard.added) InputSystem.RemoveDevice(probeKeyboard); probeKeyboard = null;
        if (previousKeyboard != null && previousKeyboard.added) previousKeyboard.MakeCurrent();
        InputSystem.settings.editorInputBehaviorInPlayMode = previousEditorInput; InputSystem.settings.backgroundBehavior = previousBackground;
        InputSystem.settings.updateMode = previousUpdateMode; Application.runInBackground = previousRunInBackground;
    }
    private static void RestoreOriginalPreferences()
    {
        Require(File.Exists(BackupPath), "No original pause-settings preferences backup exists.");
        Backup original = JsonUtility.FromJson<Backup>(File.ReadAllText(BackupPath));
        Require(original.company == Application.companyName && original.product == Application.productName,
            "Refusing preference restore under a different company/product identity.");
        if (original.hasVolume) PlayerPrefs.SetFloat(SfxPreferences.VolumeKey, original.volume); else PlayerPrefs.DeleteKey(SfxPreferences.VolumeKey);
        if (original.hasMute) PlayerPrefs.SetInt(SfxPreferences.MuteKey, original.mute); else PlayerPrefs.DeleteKey(SfxPreferences.MuteKey);
        PlayerPrefs.Save(); original.restored = true; File.WriteAllText(BackupPath, JsonUtility.ToJson(original, true));
    }
    private static bool BackupRestoredExactly()
    {
        Backup original = JsonUtility.FromJson<Backup>(File.ReadAllText(BackupPath));
        return original.restored && PlayerPrefs.HasKey(SfxPreferences.VolumeKey) == original.hasVolume && PlayerPrefs.HasKey(SfxPreferences.MuteKey) == original.hasMute
            && (!original.hasVolume || Near(PlayerPrefs.GetFloat(SfxPreferences.VolumeKey), original.volume))
            && (!original.hasMute || PlayerPrefs.GetInt(SfxPreferences.MuteKey) == original.mute);
    }
    private static ElectricSfxPlayer Sfx()
    { foreach (ElectricSfxPlayer item in Object.FindObjectsByType<ElectricSfxPlayer>(FindObjectsSortMode.None)) if (item.gameObject.scene.name == "DontDestroyOnLoad") return item; return Object.FindFirstObjectByType<ElectricSfxPlayer>(); }
    private static SceneTransitionController Service()
    { foreach (SceneTransitionController item in Object.FindObjectsByType<SceneTransitionController>(FindObjectsSortMode.None)) if (item.gameObject.scene.name == "DontDestroyOnLoad") return item; return Object.FindFirstObjectByType<SceneTransitionController>(); }
    private static PauseMenuController Pause() { PauseMenuController item = FindInScene<PauseMenuController>(); Require(item != null, "Saved gameplay scene has no pause controller."); return item; }
    private static ElectricBall2D Ball() { ElectricBall2D item = FindInScene<ElectricBall2D>(); Require(item != null, "Saved gameplay scene has no electric ball."); return item; }
    private static Slider Slider() => Reference<Slider>(FindInScene<SfxSettingsView>(), "volumeSlider");
    private static Toggle Mute() => Reference<Toggle>(FindInScene<SfxSettingsView>(), "muteToggle");
    private static T FindInScene<T>() where T : Component
    { foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects()) foreach (T found in root.GetComponentsInChildren<T>(true)) return found; return null; }
    private static FieldInfo Field(object owner, string name)
    { Require(owner != null, "Missing owner for " + name); FieldInfo field = owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic); Require(field != null, "Missing field " + name); return field; }
    private static T Reference<T>(object owner, string name) where T : class => Field(owner, name).GetValue(owner) as T;
    private static T Value<T>(object owner, string name) => (T)Field(owner, name).GetValue(owner);
    private static void Click(Button button)
    { Require(button != null && button.IsActive() && button.IsInteractable() && EventSystem.current != null, "Saved button unavailable: " + (button == null ? "missing" : button.name)); button.onClick.Invoke(); }
    private static bool Near(float a, float b) => Mathf.Abs(a - b) < .0001f;
    private static void CountChanged() => changes++;
    private static void SetPhase(Phase phase) { SessionState.SetInt(Prefix + "Phase", (int)phase); SessionState.SetFloat(Prefix + "PhaseTime", (float)EditorApplication.timeSinceStartup); }
    private static bool After(double seconds) => EditorApplication.timeSinceStartup - SessionState.GetFloat(Prefix + "PhaseTime", 0f) >= seconds;
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static Report Load() => JsonUtility.FromJson<Report>(File.ReadAllText(Output));
    private static void Save(Report report) { Directory.CreateDirectory(Path.GetDirectoryName(Output)); File.WriteAllText(Output, JsonUtility.ToJson(report, true)); }
    private static void CheckResult(string name, bool passed, string details)
    { Report report = Load(); report.checks.Add(new Check { name = name, passed = passed, details = details }); Save(report); Require(passed, "Failed check: " + name + ". " + details); }
    private static void OnLog(string message, string stack, LogType type)
    { if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return; Report report = Load(); report.errors.Add(message + "\n" + stack); Save(report); }
    private static void PlayModeChanged(PlayModeStateChange state)
    { if (SessionState.GetBool(Prefix + "Active", false) && state == PlayModeStateChange.EnteredEditMode) Fail(new InvalidOperationException("Play Mode ended before pause settings verification completed.")); }
    private static void Fail(Exception error) { Report report = Load(); report.errors.Add(error.ToString()); Save(report); Finish(false); }
    private static void Finish(bool success)
    {
        SessionState.SetBool(Prefix + "Active", false); SfxPreferences.Changed -= CountChanged; RestoreKeyboard();
        Report report = Load(); report.status = success && report.errors.Count == 0 ? "passed" : "failed"; Save(report);
        SessionState.SetInt(Prefix + "ExitCode", report.status == "passed" ? 0 : 1); SessionState.SetBool(Prefix + "Exiting", true);
        EditorApplication.update -= Tick; EditorApplication.playModeStateChanged -= PlayModeChanged;
        Application.logMessageReceived -= OnLog; SceneManager.sceneLoaded -= SceneLoaded;
        Time.timeScale = 1f; if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
        EditorApplication.update -= ExitWhenReady; EditorApplication.update += ExitWhenReady;
    }
    private static void ExitWhenReady()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        // Restore after settings/SFX OnDisable has completed so no cached shutdown save can replace original values.
        try
        {
            if (File.Exists(BackupPath))
            {
                RestoreOriginalPreferences();
                CheckResult("original-two-playerprefs-keys-restored-after-play-teardown", BackupRestoredExactly(),
                    "Original key presence and typed values are restored; unrelated keys and project identity remain untouched.");
            }
        }
        catch (Exception error)
        { Report report = Load(); report.status = "failed"; report.errors.Add(error.ToString()); Save(report); SessionState.SetInt(Prefix + "ExitCode", 1); }
        SessionState.SetBool(Prefix + "Exiting", false);
        EditorApplication.Exit(SessionState.GetInt(Prefix + "ExitCode", 1));
    }
}
