// Temporary integration harness. Copy only into Assets/Editor in the isolated clone.
// Separate Unity processes: RunWriteBatch then RunReadBatch, without -quit.
// Editor UI/PlayerPrefs validation only: runtime deliberately skips native display changes in Editor.
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
public static class DisplaySettingsValidation
{
    private const string Prefix = "Codex.DisplaySettingsValidation.";
    private const string MenuPath = "Assets/@Project/@sung_wook/@Game/00_Scenes/MainMenu.unity";
    private const string StagePath = "Assets/@Project/@sung_wook/@Game/00_Scenes/Stage/Stage_02.unity";
    private const string Output = "C:/Users/wook1/GitBub-Unity/Science-Project-2026/Logs";
    private enum Phase { EnterPlay, Menu, AwaitStage, SettingsPaused, PopupOpened, PopupClosed, Resumed, AwaitMenu }
    [Serializable] private sealed class Check { public string name; public bool passed; public string details; }
    [Serializable] private sealed class Backup
    {
        public bool hasWidth; public int width; public bool hasHeight; public int height;
        public bool hasFullscreen; public int fullscreen;
        public bool hasSfxVolume; public float sfxVolume; public bool hasSfxMute; public int sfxMute;
        public string company; public string product; public string createdUtc; public bool restored;
        public int expectedWidth; public int expectedHeight; public bool expectedFullscreen;
        public string writerProcessStartedUtc;
    }
    [Serializable] private sealed class Report
    {
        public string status = "running";
        public string mode;
        public string processStartedUtc;
        public List<Check> checks = new List<Check>();
        public List<string> errors = new List<string>();
    }
    private static string BackupPath => Path.GetFullPath(Path.Combine(Application.dataPath, "../tmp/DisplaySettingsPrefsBackup.json"));
    private static bool IsRead => SessionState.GetBool(Prefix + "Read", false);
    private static string ReportPath => Path.Combine(Output, IsRead ? "DisplaySettingsReadValidation.json" : "DisplaySettingsWriteValidation.json");
    private static Keyboard probeKeyboard, previousKeyboard;
    private static InputSettings.EditorInputBehaviorInPlayMode previousEditorInput;
    private static InputSettings.BackgroundBehavior previousBackground;
    private static InputSettings.UpdateMode previousUpdateMode;
    private static bool previousRunInBackground;
    private static readonly Dictionary<Behaviour, bool> inputStates = new Dictionary<Behaviour, bool>();
    private static float pausedFixedTime, pausedCharge;
    private static Vector2 pausedPosition, pausedVelocity;
    private static int pausedStars, changes, initialTransitionId, initialSfxId;
    private static GameObject openedPopup, openedBlocker;

    static DisplaySettingsValidation()
    {
        if (!Application.isBatchMode) return;
        if (SessionState.GetBool(Prefix + "Active", false)) ResumeCallbacks();
        if (SessionState.GetBool(Prefix + "Exiting", false)) EditorApplication.update += ExitWhenReady;
    }
    public static void RunWriteBatch() => Start(false);
    public static void RunReadBatch() => Start(true);
    public static void RestoreBackupBatch()
    {
        RequireIsolated(); RestoreOriginalPreferences(); EditorApplication.Exit(0);
    }
    private static void Start(bool read)
    {
        RequireIsolated();
        SessionState.SetBool(Prefix + "Read", read);
        string started = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        Save(new Report { processStartedUtc = started, mode = read
            ? "A separate Unity process reads the writer's three display PlayerPrefs keys before Play Mode, verifies the actual authored main and pause Dropdown callbacks share them across async fades, then restores the original three display keys after teardown. SFX keys are observed only. Native display changes are intentionally skipped in Unity Editor."
            : "Actual saved main-menu and pause Dropdown events, shared preferences/subscription lifecycle, read-only resolution options, original builtin UI colors, paused gameplay inputs/physics, actual Esc chain, and async menu/stage navigation. Only three display keys are backed up/reset; SFX keys remain untouched. Native display changes are intentionally skipped in Unity Editor." });
        try
        {
            SessionState.SetBool(Prefix + "Active", true);
            SessionState.SetBool(Prefix + "Exiting", false);
            SessionState.SetBool(Prefix + "EnterRequested", false);
            SessionState.SetString(Prefix + "StartedUtc", started);
            if (read)
            {
                Require(File.Exists(BackupPath), "A writer backup is required before the separate reader process.");
                Backup backup = ReadBackup(); Require(!backup.restored, "Writer backup was already restored.");
                CheckResult("separate-process-reads-durable-display-preferences-before-play", SavedValues(backup.expectedWidth, backup.expectedHeight, backup.expectedFullscreen)
                    && backup.writerProcessStartedUtc != started, "Three raw PlayerPrefs values match the final writer selection before Play Mode initializes.");
                CheckSfxUnchanged("reader-start-preserves-sfx");
            }
            else
            {
                Require(!File.Exists(BackupPath) || ReadBackup().restored,
                    "An unrestored display preference backup exists; run RestoreBackupBatch before another writer.");
                var backup = new Backup {
                    hasWidth = PlayerPrefs.HasKey(DisplayPreferences.WidthKey), width = PlayerPrefs.GetInt(DisplayPreferences.WidthKey, 0),
                    hasHeight = PlayerPrefs.HasKey(DisplayPreferences.HeightKey), height = PlayerPrefs.GetInt(DisplayPreferences.HeightKey, 0),
                    hasFullscreen = PlayerPrefs.HasKey(DisplayPreferences.FullscreenKey), fullscreen = PlayerPrefs.GetInt(DisplayPreferences.FullscreenKey, 0),
                    hasSfxVolume = PlayerPrefs.HasKey(SfxPreferences.VolumeKey), sfxVolume = PlayerPrefs.GetFloat(SfxPreferences.VolumeKey, 1f),
                    hasSfxMute = PlayerPrefs.HasKey(SfxPreferences.MuteKey), sfxMute = PlayerPrefs.GetInt(SfxPreferences.MuteKey, 0),
                    company = Application.companyName, product = Application.productName, createdUtc = started, writerProcessStartedUtc = started };
                WriteBackup(backup);
                PlayerPrefs.DeleteKey(DisplayPreferences.WidthKey); PlayerPrefs.DeleteKey(DisplayPreferences.HeightKey);
                PlayerPrefs.DeleteKey(DisplayPreferences.FullscreenKey); PlayerPrefs.Save();
                CheckResult("default-fixture-resets-only-three-owned-display-keys", !PlayerPrefs.HasKey(DisplayPreferences.WidthKey)
                    && !PlayerPrefs.HasKey(DisplayPreferences.HeightKey) && !PlayerPrefs.HasKey(DisplayPreferences.FullscreenKey),
                    "Original presence and integer values of three display keys are backed up; no DeleteAll or identity changes.");
                CheckSfxUnchanged("default-fixture-preserves-sfx");
                for (int stage = 1; stage <= 5; stage++)
                {
                    EditorSceneManager.OpenScene(ElectricStageProgression.StageScenePath(stage), OpenSceneMode.Single);
                    InspectSavedSettings("stage-" + stage);
                }
            }
            EditorSceneManager.OpenScene(MenuPath, OpenSceneMode.Single);
            InspectSavedSettings("main-menu");
            SetPhase(Phase.EnterPlay); ResumeCallbacks();
        }
        catch (Exception error) { Fail(error); }
    }
    private static void RequireIsolated() => Require(Application.isBatchMode
        && Application.dataPath.Replace('\\', '/').Contains("/Temp/CodexUnityVisualBuild/"), "Run this integration harness only in the isolated batch project.");

    private static void InspectSavedSettings(string scope)
    {
        DisplaySettingsView view = FindInScene<DisplaySettingsView>();
        Require(view != null, scope + " has no saved DisplaySettingsView.");
        Dropdown mode = Reference<Dropdown>(view, "screenModeDropdown");
        Dropdown resolution = Reference<Dropdown>(view, "resolutionDropdown");
        CheckResult(scope + "-saved-display-dropdowns-are-wired", mode != null && resolution != null && mode.template != null
            && resolution.template != null && mode.captionText != null && resolution.captionText != null
            && mode.itemText != null && resolution.itemText != null && !mode.template.gameObject.activeSelf && !resolution.template.gameObject.activeSelf,
            "Both authored default Dropdowns have saved caption, inactive template and item label references.");
        bool colors = true;
        foreach (Graphic graphic in view.GetComponentsInChildren<Graphic>(true)) colors &= HasOriginalStyle(graphic);
        CheckResult(scope + "-display-controls-keep-original-basic-assets-and-colors", colors
            && mode.transition == Selectable.Transition.None && resolution.transition == Selectable.Transition.None,
            "Images retain white tint and existing builtin sprites; Text retains default RGB 50/255; selectable controls do not recolor assets.");
        CheckResult(scope + "-existing-sfx-view-is-preserved", FindInScene<SfxSettingsView>() != null,
            "Existing SFX Slider, mute Toggle and shared SfxSettingsView remain present beside display settings.");
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
            Require((DateTime.UtcNow - started).TotalSeconds < 180d, "Display settings validation exceeded its 180-second watchdog.");
            Require(Load().errors.Count == 0, "Unity logged an error during display settings validation.");
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
                if (menu == null || !Reference<Button>(menu, "settingsButton").IsInteractable()) return;
                initialTransitionId = Service().GetInstanceID(); initialSfxId = Sfx().GetInstanceID();
                InspectOptions();
                Click(Reference<Button>(menu, "settingsButton"));
                CheckUi("initial-main-menu-displays-current-shared-values");
                CheckResult("main-menu-has-one-display-listener", DisplayViewListenerCount() == 1, "Exactly one enabled display view subscribes to shared preferences.");
                if (IsRead) CheckExpectedPersisted("reader-main-menu-restores-writer-selection");
                else
                {
                    int target = OtherResolutionIndex(DisplayPreferences.SelectedResolutionIndex);
                    ChangeControls(target, !DisplayPreferences.IsFullscreen, "main-menu-dropdown-edits");
                    for (int reopen = 0; reopen < 3; reopen++)
                    {
                        Click(Reference<Button>(menu, "closeSettingsButton"));
                        CheckResult("closed-main-settings-unsubscribes-" + reopen, DisplayViewListenerCount() == 0, "The closed display view removes its shared listener.");
                        Click(Reference<Button>(menu, "settingsButton")); CheckUi("reopened-main-settings-" + reopen);
                        CheckResult("reopened-main-settings-subscribes-once-" + reopen, DisplayViewListenerCount() == 1, "Reopening does not duplicate event callbacks.");
                    }
                }
                SessionState.SetInt(Prefix + "MenuWidth", DisplayPreferences.Width); SessionState.SetInt(Prefix + "MenuHeight", DisplayPreferences.Height);
                SessionState.SetBool(Prefix + "MenuFullscreen", DisplayPreferences.IsFullscreen);
                CheckSfxUnchanged("main-menu-edits-preserve-sfx");
                Click(Reference<Button>(menu, "closeSettingsButton"));
                PrepareLoad(StagePath); Click(Reference<Button>(menu, "startButton")); CheckAcceptedLoad("start-stage");
                SetPhase(Phase.AwaitStage); return;
            }
            if (phase == Phase.AwaitStage)
            {
                if (!AwaitLoad(StagePath) || !StageReady()) return;
                CheckResult("gameplay-shares-menu-selection", DisplayPreferences.Width == SessionState.GetInt(Prefix + "MenuWidth", 0)
                    && DisplayPreferences.Height == SessionState.GetInt(Prefix + "MenuHeight", 0)
                    && DisplayPreferences.IsFullscreen == SessionState.GetBool(Prefix + "MenuFullscreen", false),
                    "Real async scene activation retains the same width, height and screen mode.");
                SetupKeyboard();
                inputStates.Clear(); foreach (Behaviour input in InputComponents()) inputStates[input] = input.enabled;
                PauseMenuController pause = Pause(); DispatchEscape(pause);
                CheckResult("actual-escape-opens-pause", pause.IsPaused && Time.timeScale == 0f && InputsSuspended(),
                    "InputSystem Esc reaches actual pause Update; gameplay inputs and time are suspended.");
                Click(Reference<Button>(pause, "settingsButton")); CheckUi("pause-settings-share-main-menu-values");
                CheckResult("only-one-visible-display-view-in-pause", DisplayViewListenerCount() == 1 && pause.IsSettingsOpen && !Reference<GameObject>(pause, "menuRoot").activeSelf,
                    "The saved settings panel replaces the pause list and owns one display listener.");
                if (IsRead) CheckExpectedPersisted("reader-pause-settings-restores-writer-selection");
                else ChangeControls(OtherResolutionIndex(DisplayPreferences.SelectedResolutionIndex), !DisplayPreferences.IsFullscreen, "paused-dropdown-edits");
                CheckSfxUnchanged("paused-display-edits-preserve-sfx");
                ElectricBall2D ball = Ball(); pausedPosition = ball.Body.position; pausedVelocity = ball.Body.linearVelocity;
                pausedFixedTime = Time.fixedTime; pausedCharge = ball.SignedCharge; pausedStars = ball.Stars;
                SessionState.SetInt(Prefix + "PausedFrame", Time.frameCount); SetPhase(Phase.SettingsPaused); return;
            }
            if (phase == Phase.SettingsPaused)
            {
                if (!After(.3d) || Time.frameCount <= SessionState.GetInt(Prefix + "PausedFrame", 0) + 2) return;
                PauseMenuController pause = Pause(); ElectricBall2D ball = Ball();
                CheckResult("display-settings-do-not-resume-or-change-gameplay", pause.IsPaused && pause.IsSettingsOpen && Time.timeScale == 0f
                    && Near(Time.fixedTime, pausedFixedTime) && Vector2.Distance(ball.Body.position, pausedPosition) < .0001f
                    && Vector2.Distance(ball.Body.linearVelocity, pausedVelocity) < .0001f && Near(ball.SignedCharge, pausedCharge)
                    && ball.Stars == pausedStars && InputsSuspended(), "Across real Play Mode frames, display edits preserve paused physics, charge, stars and disabled input.");
                for (int reopen = 0; reopen < 2; reopen++)
                {
                    Click(Reference<Button>(pause, "closeSettingsButton"));
                    CheckResult("pause-settings-close-removes-listener-" + reopen, DisplayViewListenerCount() == 0 && pause.IsPaused && Time.timeScale == 0f,
                        "Back returns to pause without resuming and unsubscribes display settings.");
                    Click(Reference<Button>(pause, "settingsButton")); CheckUi("pause-reopen-retains-selection-" + reopen);
                    CheckResult("pause-settings-reopen-keeps-one-listener-" + reopen, DisplayViewListenerCount() == 1, "Reopening produces one shared listener.");
                }
                Dropdown resolution = ResolutionDropdown(); resolution.Show();
                openedPopup = Reference<GameObject>(resolution, "m_Dropdown"); openedBlocker = Reference<GameObject>(resolution, "m_Blocker");
                CheckResult("resolution-dropdown-creates-real-popup-during-pause", openedPopup != null && openedPopup.activeInHierarchy
                    && openedPopup.name == "Dropdown List" && openedBlocker != null && openedBlocker.activeInHierarchy && Time.timeScale == 0f,
                    "The actual built-in Dropdown.Show creates its authored-template popup and modal blocker while gameplay remains paused.");
                SetPhase(Phase.PopupOpened); return;
            }
            if (phase == Phase.PopupOpened)
            {
                if (!After(.25d)) return;
                PauseMenuController pause = Pause();
                CanvasGroup popupGroup = openedPopup != null ? openedPopup.GetComponent<CanvasGroup>() : null;
                bool itemsKeepOriginalStyle = openedPopup != null;
                if (openedPopup != null) foreach (Graphic graphic in openedPopup.GetComponentsInChildren<Graphic>(true)) itemsKeepOriginalStyle &= HasOriginalStyle(graphic);
                CheckResult("dropdown-popup-fades-visible-with-unscaled-time", popupGroup != null && popupGroup.alpha >= .999f
                    && openedPopup.GetComponentsInChildren<Toggle>().Length >= DisplayPreferences.Resolutions.Count && itemsKeepOriginalStyle
                    && pause.IsPaused && Time.timeScale == 0f && InputsSuspended(),
                    "Real dropdown options become opaque at timeScale=0, retain the authored builtin sprites and default font colors, and leave gameplay frozen.");
                DispatchEscape(pause);
                CheckResult("first-escape-returns-from-settings-to-pause", pause.IsPaused && !pause.IsSettingsOpen && Time.timeScale == 0f
                    && InputsSuspended() && DisplayViewListenerCount() == 0 && EventSystem.current.currentSelectedGameObject == Reference<Button>(pause, "settingsButton").gameObject,
                    "Existing Esc chain returns to the pause list and focuses its Settings button.");
                CheckResult("closing-settings-clears-dropdown-popup-ownership", Reference<GameObject>(ResolutionDropdown(), "m_Dropdown") == null
                    && Reference<GameObject>(ResolutionDropdown(), "m_Blocker") == null,
                    "Built-in Dropdown.OnDisable immediately clears popup and blocker references when the settings panel closes.");
                SessionState.SetInt(Prefix + "PopupCloseFrame", Time.frameCount); SetPhase(Phase.PopupClosed); return;
            }
            if (phase == Phase.PopupClosed)
            {
                if (Time.frameCount <= SessionState.GetInt(Prefix + "PopupCloseFrame", 0) + 1) return;
                PauseMenuController pause = Pause();
                CheckResult("dropdown-popup-and-blocker-are-destroyed-after-settings-close", openedPopup == null && openedBlocker == null
                    && pause.IsPaused && !pause.IsSettingsOpen && Time.timeScale == 0f,
                    "After the normal deferred Unity destruction frame, no popup or blocker survives over the pause menu.");
                DispatchEscape(pause);
                CheckResult("second-escape-resumes-gameplay", !pause.IsPaused && !pause.IsSettingsOpen && Time.timeScale == 1f && InputsRestored(),
                    "The following Esc restores the captured gameplay input state and normal time.");
                SessionState.SetInt(Prefix + "ResumeFrame", Time.frameCount); SetPhase(Phase.Resumed); return;
            }
            if (phase == Phase.Resumed)
            {
                if (!After(.1d) || Time.frameCount <= SessionState.GetInt(Prefix + "ResumeFrame", 0) + 2) return;
                CheckResult("fixed-gameplay-time-advances-after-resume", Time.fixedTime > pausedFixedTime, "Fixed simulation time advances again after the pause closes.");
                PauseMenuController pause = Pause(); DispatchEscape(pause);
                PrepareLoad(MenuPath); Click(Reference<Button>(pause, "mainMenuButton")); CheckAcceptedLoad("return-menu");
                SetPhase(Phase.AwaitMenu); return;
            }
            if (phase == Phase.AwaitMenu)
            {
                if (!AwaitLoad(MenuPath)) return;
                MainMenuController menu = FindInScene<MainMenuController>();
                if (menu == null || !Reference<Button>(menu, "settingsButton").IsInteractable()) return;
                CheckResult("menu-return-retains-one-service-and-releases-pause", Time.timeScale == 1f && FindInScene<PauseMenuController>() == null
                    && Service().GetInstanceID() == initialTransitionId && Sfx().GetInstanceID() == initialSfxId
                    && Object.FindObjectsByType<SceneTransitionController>(FindObjectsSortMode.None).Length == 1
                    && Object.FindObjectsByType<ElectricSfxPlayer>(FindObjectsSortMode.None).Length == 1,
                    "Returning destroys the stage pause owner and retains the original shared transition and SFX services.");
                Click(Reference<Button>(menu, "settingsButton")); CheckUi("main-menu-sees-in-game-display-settings");
                if (IsRead) CheckExpectedPersisted("reader-menu-return-still-shares-restored-selection");
                else
                {
                    int target = DisplayPreferences.Resolutions.Count - 1;
                    SetControlsAndVerify(target, true, "final-persistent-writer-selection");
                    DisplayPreferences.Save();
                    Backup backup = ReadBackup(); backup.expectedWidth = DisplayPreferences.Width; backup.expectedHeight = DisplayPreferences.Height;
                    backup.expectedFullscreen = DisplayPreferences.IsFullscreen; WriteBackup(backup);
                    CheckResult("writer-saves-known-display-selection-for-separate-process", SavedValues(backup.expectedWidth, backup.expectedHeight, true),
                        "The final saved width, height and fullscreen=true are recorded for an independent Unity process to read.");
                }
                CheckSfxUnchanged("final-navigation-preserves-sfx");
                Click(Reference<Button>(menu, "closeSettingsButton"));
                CheckResult("final-close-removes-display-listener", DisplayViewListenerCount() == 0, "No display view stays subscribed while its panel is closed.");
                Finish(true);
            }
        }
        catch (Exception error) { Fail(error); }
    }

    private static void InspectOptions()
    {
        IReadOnlyList<Vector2Int> options = DisplayPreferences.Resolutions;
        Require(options != null && options.Count > 0, "No display resolution choices exist.");
        var unique = new HashSet<Vector2Int>(); bool sorted = true; bool valid = true;
        for (int i = 0; i < options.Count; i++)
        {
            Vector2Int size = options[i]; valid &= size.x >= 640 && size.y >= 360 && unique.Add(size);
            if (i > 0) sorted &= options[i - 1].x < size.x || (options[i - 1].x == size.x && options[i - 1].y < size.y);
        }
        CheckResult("resolution-options-are-positive-unique-and-sorted", valid && sorted,
            "Resolution choices contain unique dimensions without refresh-rate duplicates, sorted by width then height.");
        CheckResult("selected-resolution-is-in-supported-list", DisplayPreferences.SelectedResolutionIndex >= 0
            && DisplayPreferences.SelectedResolutionIndex < options.Count && options[DisplayPreferences.SelectedResolutionIndex] == new Vector2Int(DisplayPreferences.Width, DisplayPreferences.Height),
            "Current preferences always resolve to one published option.");
        bool readOnly = !(options is IList<Vector2Int> list) || list.IsReadOnly;
        CheckResult("published-resolution-options-cannot-be-edited", readOnly, "The publicly exposed list is read-only rather than a mutable list behind IReadOnlyList.");
    }
    private static void ChangeControls(int index, bool fullscreen, string name)
    {
        int beforeWidth = Screen.width, beforeHeight = Screen.height; FullScreenMode beforeMode = Screen.fullScreenMode;
        bool resolutionChanges = index != DisplayPreferences.SelectedResolutionIndex; bool modeChanges = fullscreen != DisplayPreferences.IsFullscreen;
        changes = 0; DisplayPreferences.Changed += CountChanged;
        try { SetControlsAndVerify(index, fullscreen, name); }
        finally { DisplayPreferences.Changed -= CountChanged; }
        CheckResult(name + "-emit-exactly-once-and-persist-immediately", changes == (resolutionChanges ? 1 : 0) + (modeChanges ? 1 : 0)
            && SavedValues(DisplayPreferences.Width, DisplayPreferences.Height, DisplayPreferences.IsFullscreen),
            "Each changed Dropdown value causes one shared change and saves immediately; refresh uses no-notify updates.");
        CheckResult(name + "-editor-window-remains-protected", Screen.width == beforeWidth && Screen.height == beforeHeight && Screen.fullScreenMode == beforeMode,
            "Editor UI and PlayerPrefs are exercised without requesting an OS fullscreen or native game-window change.");
    }
    private static void SetControlsAndVerify(int index, bool fullscreen, string name)
    {
        ResolutionDropdown().value = index; ModeDropdown().value = fullscreen ? 0 : 1;
        Vector2Int expected = DisplayPreferences.Resolutions[index];
        CheckResult(name, DisplayPreferences.Width == expected.x && DisplayPreferences.Height == expected.y
            && DisplayPreferences.IsFullscreen == fullscreen, "Actual saved Dropdown.value events update the shared width, height and fullscreen preferences.");
        CheckUi(name + "-view-refreshes");
    }
    private static void CheckUi(string name)
    {
        DisplaySettingsView view = FindInScene<DisplaySettingsView>(); Dropdown resolution = ResolutionDropdown(); Dropdown mode = ModeDropdown();
        CheckResult(name, view != null && view.isActiveAndEnabled && resolution.options.Count == DisplayPreferences.Resolutions.Count
            && mode.options.Count == 2 && mode.value == (DisplayPreferences.IsFullscreen ? 0 : 1)
            && resolution.value == DisplayPreferences.SelectedResolutionIndex
            && resolution.captionText.text.Contains(DisplayPreferences.Width.ToString(CultureInfo.InvariantCulture))
            && resolution.captionText.text.Contains(DisplayPreferences.Height.ToString(CultureInfo.InvariantCulture)),
            "Visible authored Dropdowns show the same selected dimensions and fullscreen/windowed value as shared preferences.");
        Backup backup = ReadBackup();
        SfxSettingsView sfxView = FindInScene<SfxSettingsView>();
        CheckResult(name + "-sfx-controls-still-agree", sfxView != null && sfxView.isActiveAndEnabled
            && Near(Reference<Slider>(sfxView, "volumeSlider").value, SfxPreferences.Volume)
            && Reference<Toggle>(sfxView, "muteToggle").isOn == SfxPreferences.IsMuted,
            "Existing live SFX Slider and mute Toggle continue to show their independent shared settings.");
    }
    private static void CheckExpectedPersisted(string name)
    {
        Backup backup = ReadBackup();
        CheckResult(name, DisplayPreferences.Width == backup.expectedWidth && DisplayPreferences.Height == backup.expectedHeight
            && DisplayPreferences.IsFullscreen == backup.expectedFullscreen && SavedValues(backup.expectedWidth, backup.expectedHeight, backup.expectedFullscreen),
            "The new process and both visible settings panels agree with the writer's durable final values.");
    }
    private static bool SavedValues(int width, int height, bool fullscreen) => PlayerPrefs.HasKey(DisplayPreferences.WidthKey)
        && PlayerPrefs.HasKey(DisplayPreferences.HeightKey) && PlayerPrefs.HasKey(DisplayPreferences.FullscreenKey)
        && PlayerPrefs.GetInt(DisplayPreferences.WidthKey) == width && PlayerPrefs.GetInt(DisplayPreferences.HeightKey) == height
        && PlayerPrefs.GetInt(DisplayPreferences.FullscreenKey) == (fullscreen ? 1 : 0);
    private static void CheckSfxUnchanged(string name)
    {
        Backup backup = ReadBackup();
        CheckResult(name, PlayerPrefs.HasKey(SfxPreferences.VolumeKey) == backup.hasSfxVolume && PlayerPrefs.HasKey(SfxPreferences.MuteKey) == backup.hasSfxMute
            && (!backup.hasSfxVolume || Near(PlayerPrefs.GetFloat(SfxPreferences.VolumeKey), backup.sfxVolume))
            && (!backup.hasSfxMute || PlayerPrefs.GetInt(SfxPreferences.MuteKey) == backup.sfxMute),
            "Display changes leave the original presence and values of both existing SFX PlayerPrefs keys unchanged.");
    }
    private static int OtherResolutionIndex(int current)
    { Require(DisplayPreferences.Resolutions.Count > 1, "Resolution testing needs at least two safe available choices."); return current == 0 ? DisplayPreferences.Resolutions.Count - 1 : 0; }
    private static int DisplayViewListenerCount()
    {
        FieldInfo field = typeof(DisplayPreferences).GetField("Changed", BindingFlags.NonPublic | BindingFlags.Static);
        Require(field != null, "Missing shared display event backing field.");
        Delegate callbacks = field.GetValue(null) as Delegate; int count = 0;
        if (callbacks != null) foreach (Delegate callback in callbacks.GetInvocationList()) if (callback.Target is DisplaySettingsView) count++;
        return count;
    }
    private static bool StageReady() { ElectricStageSession2D session = FindInScene<ElectricStageSession2D>(); return session != null && session.IsInitialized; }
    private static IEnumerable<Behaviour> InputComponents()
    {
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects()) foreach (Behaviour input in root.GetComponentsInChildren<Behaviour>(true))
            if (input is PlayerController || input is PlayerInputReader || input is ElectricStageInput2D || input is ElectricLearningInput2D
                || input is GearLeverController || input is global::LeverReader || input is global::LeverMover
                || input is DistanceLauncher2D || input is VoltageLauncher2D) yield return input;
    }
    private static bool InputsSuspended()
    { int count = 0; foreach (var pair in inputStates) if (pair.Key != null && pair.Key.gameObject.activeInHierarchy) { count++; if (pair.Key.enabled) return false; } return count >= 5; }
    private static bool InputsRestored()
    { foreach (var pair in inputStates) if (pair.Key != null && pair.Key.enabled != pair.Value) return false; return inputStates.Count >= 5; }
    private static void PrepareLoad(string path)
    {
        SessionState.SetString(Prefix + "Target", path); SessionState.SetBool(Prefix + "Loaded", false);
        SessionState.SetBool(Prefix + "FadeOut", false); SessionState.SetBool(Prefix + "FadeIn", false);
    }
    private static void CheckAcceptedLoad(string name)
    {
        AsyncOperation load = Reference<AsyncOperation>(Service(), "pendingLoad");
        CheckResult(name + "-starts-real-asynchronous-load", SceneTransitionController.IsBusy && Time.timeScale == 0f
            && load != null && !load.allowSceneActivation, "Saved navigation button starts a real async scene load with activation held behind the fade.");
    }
    private static bool AwaitLoad(string path)
    {
        SceneTransitionController service = Service(); if (service == null) return false;
        CanvasGroup overlay = Reference<CanvasGroup>(service, "fadeOverlay");
        if (SceneTransitionController.IsBusy)
        {
            Require(Time.timeScale == 0f && overlay.blocksRaycasts, "Scene transitions must retain paused time and UI cover.");
            if (overlay.alpha > .01f && overlay.alpha < .99f)
                SessionState.SetBool(Prefix + (SessionState.GetBool(Prefix + "Loaded", false) ? "FadeIn" : "FadeOut"), true);
            return false;
        }
        if (SceneManager.GetActiveScene().path != path || !SessionState.GetBool(Prefix + "Loaded", false)) return false;
        CheckResult("real-async-fade-completes-" + (Phase)SessionState.GetInt(Prefix + "Phase", 0), Near(Time.timeScale, 1f) && Near(overlay.alpha, 0f)
            && !overlay.blocksRaycasts && SessionState.GetBool(Prefix + "FadeOut", false) && SessionState.GetBool(Prefix + "FadeIn", false),
            "Observed intermediate fade-out and fade-in around actual scene activation, then normal time and UI interaction restore.");
        return true;
    }
    private static void SceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!SessionState.GetBool(Prefix + "Active", false) || !EditorApplication.isPlaying || scene.path != SessionState.GetString(Prefix + "Target", "")) return;
        SessionState.SetBool(Prefix + "Loaded", true); CanvasGroup overlay = Reference<CanvasGroup>(Service(), "fadeOverlay");
        CheckResult("scene-activates-behind-opaque-fade-" + (Phase)SessionState.GetInt(Prefix + "Phase", 0), SceneTransitionController.IsBusy
            && Time.timeScale == 0f && overlay.alpha >= .999f, "The real sceneLoaded callback observes frozen time and the shared opaque fade.");
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
        Require(probeKeyboard.escapeKey.wasPressedThisFrame, "Virtual Esc did not reach the current InputSystem keyboard.");
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
        Require(File.Exists(BackupPath), "No original display preferences backup exists."); Backup backup = ReadBackup();
        Require(backup.company == Application.companyName && backup.product == Application.productName, "Refusing preference restore under a different company/product identity.");
        if (backup.hasWidth) PlayerPrefs.SetInt(DisplayPreferences.WidthKey, backup.width); else PlayerPrefs.DeleteKey(DisplayPreferences.WidthKey);
        if (backup.hasHeight) PlayerPrefs.SetInt(DisplayPreferences.HeightKey, backup.height); else PlayerPrefs.DeleteKey(DisplayPreferences.HeightKey);
        if (backup.hasFullscreen) PlayerPrefs.SetInt(DisplayPreferences.FullscreenKey, backup.fullscreen); else PlayerPrefs.DeleteKey(DisplayPreferences.FullscreenKey);
        PlayerPrefs.Save(); backup.restored = true; WriteBackup(backup);
    }
    private static bool BackupRestoredExactly()
    {
        Backup backup = ReadBackup();
        return backup.restored && PlayerPrefs.HasKey(DisplayPreferences.WidthKey) == backup.hasWidth
            && PlayerPrefs.HasKey(DisplayPreferences.HeightKey) == backup.hasHeight && PlayerPrefs.HasKey(DisplayPreferences.FullscreenKey) == backup.hasFullscreen
            && (!backup.hasWidth || PlayerPrefs.GetInt(DisplayPreferences.WidthKey) == backup.width)
            && (!backup.hasHeight || PlayerPrefs.GetInt(DisplayPreferences.HeightKey) == backup.height)
            && (!backup.hasFullscreen || PlayerPrefs.GetInt(DisplayPreferences.FullscreenKey) == backup.fullscreen);
    }
    private static Backup ReadBackup() => JsonUtility.FromJson<Backup>(File.ReadAllText(BackupPath));
    private static void WriteBackup(Backup backup) { Directory.CreateDirectory(Path.GetDirectoryName(BackupPath)); File.WriteAllText(BackupPath, JsonUtility.ToJson(backup, true)); }
    private static SceneTransitionController Service()
    { foreach (SceneTransitionController service in Object.FindObjectsByType<SceneTransitionController>(FindObjectsSortMode.None)) if (service.gameObject.scene.name == "DontDestroyOnLoad") return service; return Object.FindFirstObjectByType<SceneTransitionController>(); }
    private static ElectricSfxPlayer Sfx()
    { foreach (ElectricSfxPlayer service in Object.FindObjectsByType<ElectricSfxPlayer>(FindObjectsSortMode.None)) if (service.gameObject.scene.name == "DontDestroyOnLoad") return service; return Object.FindFirstObjectByType<ElectricSfxPlayer>(); }
    private static PauseMenuController Pause() { PauseMenuController item = FindInScene<PauseMenuController>(); Require(item != null, "No saved pause controller found."); return item; }
    private static ElectricBall2D Ball() { ElectricBall2D item = FindInScene<ElectricBall2D>(); Require(item != null, "No saved gameplay ball found."); return item; }
    private static Dropdown ModeDropdown() => Reference<Dropdown>(FindInScene<DisplaySettingsView>(), "screenModeDropdown");
    private static Dropdown ResolutionDropdown() => Reference<Dropdown>(FindInScene<DisplaySettingsView>(), "resolutionDropdown");
    private static T FindInScene<T>() where T : Component
    { foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects()) foreach (T found in root.GetComponentsInChildren<T>(true)) return found; return null; }
    private static FieldInfo Field(object owner, string name)
    { Require(owner != null, "Missing owner for " + name); FieldInfo field = owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic); Require(field != null, "Missing field " + name); return field; }
    private static T Reference<T>(object owner, string name) where T : class => Field(owner, name).GetValue(owner) as T;
    private static void Click(Button button)
    { Require(button != null && button.IsActive() && button.IsInteractable() && EventSystem.current != null, "Saved navigation button is unavailable."); button.onClick.Invoke(); }
    private static bool Near(float a, float b) => Mathf.Abs(a - b) < .0001f;
    private static void CountChanged() => changes++;
    private static void SetPhase(Phase phase) { SessionState.SetInt(Prefix + "Phase", (int)phase); SessionState.SetFloat(Prefix + "PhaseTime", (float)EditorApplication.timeSinceStartup); }
    private static bool After(double seconds) => EditorApplication.timeSinceStartup - SessionState.GetFloat(Prefix + "PhaseTime", 0f) >= seconds;
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static Report Load() => JsonUtility.FromJson<Report>(File.ReadAllText(ReportPath));
    private static void Save(Report report) { Directory.CreateDirectory(Path.GetDirectoryName(ReportPath)); File.WriteAllText(ReportPath, JsonUtility.ToJson(report, true)); }
    private static void CheckResult(string name, bool passed, string details)
    { Report report = Load(); report.checks.Add(new Check { name = name, passed = passed, details = details }); Save(report); Require(passed, "Failed check: " + name + ". " + details); }
    private static void OnLog(string message, string stack, LogType type)
    { if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return; Report report = Load(); report.errors.Add(message + "\n" + stack); Save(report); }
    private static void PlayModeChanged(PlayModeStateChange state)
    { if (SessionState.GetBool(Prefix + "Active", false) && state == PlayModeStateChange.EnteredEditMode) Fail(new InvalidOperationException("Play Mode ended before display validation completed.")); }
    private static void Fail(Exception error) { Report report = Load(); report.errors.Add(error.ToString()); Save(report); Finish(false); }
    private static void Finish(bool success)
    {
        SessionState.SetBool(Prefix + "Active", false); DisplayPreferences.Changed -= CountChanged; RestoreKeyboard();
        Report report = Load(); report.status = success && report.errors.Count == 0 ? "passed" : "failed"; Save(report);
        SessionState.SetInt(Prefix + "ExitCode", report.status == "passed" ? 0 : 1);
        SessionState.SetBool(Prefix + "RestoreOnExit", IsRead || report.status != "passed"); SessionState.SetBool(Prefix + "Exiting", true);
        EditorApplication.update -= Tick; EditorApplication.playModeStateChanged -= PlayModeChanged;
        Application.logMessageReceived -= OnLog; SceneManager.sceneLoaded -= SceneLoaded;
        Time.timeScale = 1f; if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
        EditorApplication.update -= ExitWhenReady; EditorApplication.update += ExitWhenReady;
    }
    private static void ExitWhenReady()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        try
        {
            // Wait until all OnDisable Save hooks finish before restoring exact original key presence.
            if (File.Exists(BackupPath) && SessionState.GetBool(Prefix + "RestoreOnExit", false))
            {
                RestoreOriginalPreferences();
                CheckResult("original-three-display-keys-restored-after-play-teardown", BackupRestoredExactly(),
                    "Original presence and integer values are restored after cached runtime shutdown saves complete; no other key is written.");
            }
            if (File.Exists(BackupPath)) CheckSfxUnchanged("teardown-still-preserves-original-sfx-keys");
        }
        catch (Exception error)
        { Report report = Load(); report.status = "failed"; report.errors.Add(error.ToString()); Save(report); SessionState.SetInt(Prefix + "ExitCode", 1); }
        SessionState.SetBool(Prefix + "Exiting", false); EditorApplication.Exit(SessionState.GetInt(Prefix + "ExitCode", 1));
    }
}
