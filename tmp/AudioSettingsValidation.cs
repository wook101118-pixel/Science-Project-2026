// Temporary integration harness. Copy into Assets/Editor only in the isolated clone.
// Separate Unity processes must invoke RunWriteBatch then RunReadBatch, with -force-audio.
// Omit -quit: each entry point owns Play Mode and editor exit. RestoreBackupBatch is recovery.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using sung_wook;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class AudioSettingsValidation
{
    private const string Prefix = "Codex.AudioSettingsValidation.";
    private const string MenuPath = "Assets/@Project/@sung_wook/@Game/00_Scenes/MainMenu.unity";
    private const string StagePath = "Assets/@Project/@sung_wook/@Game/00_Scenes/Stage/Stage_02.unity";
    private const string Output = "C:/Users/wook1/GitBub-Unity/Science-Project-2026/Logs";
    private enum Phase { EnterPlay, Menu, Playing, Paused, Resume, AwaitStage, AwaitMenu, Finish }
    [Serializable] private sealed class Check { public string name; public bool passed; public string details; }
    [Serializable] private sealed class Backup
    {
        public bool hasVolume; public float volume; public bool hasMute; public int mute;
        public string company; public string product; public string createdUtc; public bool restored;
    }
    [Serializable] private sealed class Report
    {
        public string status = "running";
        public string mode;
        public string processStartedUtc;
        public List<Check> checks = new List<Check>();
        public List<string> errors = new List<string>();
    }
    private static readonly Dictionary<AudioSource, float> baseGains = new Dictionary<AudioSource, float>();
    private static int initialSfxId;
    private static float pausedTime;
    private static int changes;
    private static string BackupPath => Path.GetFullPath(Path.Combine(Application.dataPath, "../tmp/AudioSettingsPlayerPrefsBackup.json"));
    private static bool IsRead => SessionState.GetBool(Prefix + "Read", false);
    private static string ReportPath => Path.Combine(Output, IsRead ? "AudioSettingsReadValidation.json" : "AudioSettingsWriteValidation.json");

    static AudioSettingsValidation()
    {
        if (!Application.isBatchMode) return;
        if (SessionState.GetBool(Prefix + "Active", false)) ResumeCallbacks();
        if (SessionState.GetBool(Prefix + "Exiting", false)) EditorApplication.update += ExitWhenReady;
    }

    public static void RunWriteBatch() => Start(false);
    public static void RunReadBatch() => Start(true);
    public static void RestoreBackupBatch()
    {
        RequireIsolated();
        RestoreOriginalPreferences();
        Debug.Log("Audio settings validation restored only its two backed-up PlayerPrefs keys.");
        EditorApplication.Exit(0);
    }

    private static void Start(bool read)
    {
        RequireIsolated();
        SessionState.SetBool(Prefix + "Read", read);
        Save(new Report { mode = read
            ? "Separate Unity process reads the writer's saved preferences, opens actual settings UI, invokes saved Start and pause Main Menu buttons through async fades, then restores only the two original keys."
            : "Actual authored Slider and Toggle events control the seven existing SFX sources while playing and paused. Existing UI asset colors, no BGM, invalid-value handling, close/reopen lifecycle, and durable PlayerPrefs are verified. Two original keys are backed up without changing company/product or unrelated preferences.",
            processStartedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) });
        try
        {
            if (read)
            {
                Require(File.Exists(BackupPath), "No writer backup exists; read validation must run after writer in a separate process.");
                CheckResult("new-process-reads-written-playerprefs-before-play", PlayerPrefs.HasKey(SfxPreferences.VolumeKey)
                    && PlayerPrefs.HasKey(SfxPreferences.MuteKey) && Near(PlayerPrefs.GetFloat(SfxPreferences.VolumeKey), .37f)
                    && PlayerPrefs.GetInt(SfxPreferences.MuteKey) == 1, "PlayerPrefs reads 0.37 and muted=1 before any Play Mode or settings UI initializes.");
            }
            else
            {
                Require(!File.Exists(BackupPath) || JsonUtility.FromJson<Backup>(File.ReadAllText(BackupPath)).restored,
                    "An unrestored preferences backup already exists; run RestoreBackupBatch before a new writer.");
                var backup = new Backup { hasVolume = PlayerPrefs.HasKey(SfxPreferences.VolumeKey),
                    volume = PlayerPrefs.GetFloat(SfxPreferences.VolumeKey, 1f), hasMute = PlayerPrefs.HasKey(SfxPreferences.MuteKey),
                    mute = PlayerPrefs.GetInt(SfxPreferences.MuteKey, 0), company = Application.companyName,
                    product = Application.productName, createdUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) };
                Directory.CreateDirectory(Path.GetDirectoryName(BackupPath));
                File.WriteAllText(BackupPath, JsonUtility.ToJson(backup, true));
                PlayerPrefs.DeleteKey(SfxPreferences.VolumeKey);
                PlayerPrefs.DeleteKey(SfxPreferences.MuteKey);
                PlayerPrefs.Save();
                CheckResult("only-two-owned-keys-reset-for-default-fixture", !PlayerPrefs.HasKey(SfxPreferences.VolumeKey)
                    && !PlayerPrefs.HasKey(SfxPreferences.MuteKey), "No DeleteAll; original key presence and exact typed values are recorded in the isolated clone's tmp folder.");
            }
            SessionState.SetBool(Prefix + "Active", true);
            SessionState.SetBool(Prefix + "Exiting", false);
            SessionState.SetBool(Prefix + "EnterRequested", false);
            SessionState.SetString(Prefix + "StartedUtc", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            SetPhase(Phase.EnterPlay);
            EditorSceneManager.OpenScene(MenuPath, OpenSceneMode.Single);
            InspectSavedStyle();
            ResumeCallbacks();
        }
        catch (Exception error) { Fail(error); }
    }

    private static void RequireIsolated()
    {
        Require(Application.isBatchMode && Application.dataPath.Replace('\\', '/').Contains("/Temp/CodexUnityVisualBuild/"),
            "This preferences integration harness must run only in the isolated batch project.");
    }
    private static void InspectSavedStyle()
    {
        SfxSettingsView view = FindInScene<SfxSettingsView>();
        Require(view != null, "The saved MainMenu has no SfxSettingsView.");
        Slider slider = Reference<Slider>(view, "volumeSlider");
        Toggle toggle = Reference<Toggle>(view, "muteToggle");
        Text label = Reference<Text>(view, "volumeValueText");
        CheckResult("saved-settings-slider-and-toggle-are-wired", slider != null && toggle != null && label != null
            && Near(slider.minValue, 0f) && Near(slider.maxValue, 1f) && !slider.wholeNumbers,
            "Saved Slider spans normalized 0–1 and the saved Toggle/Text references resolve before Play Mode.");
        bool originalColors = true;
        foreach (Graphic graphic in view.GetComponentsInChildren<Graphic>(true))
        {
            Color expected = graphic is Text ? new Color(50f / 255f, 50f / 255f, 50f / 255f, 1f) : Color.white;
            originalColors &= Near(graphic.color.r, expected.r) && Near(graphic.color.g, expected.g)
                && Near(graphic.color.b, expected.b) && Near(graphic.color.a, expected.a);
            if (graphic is Image image && image.sprite != null)
                originalColors &= AssetDatabase.GetAssetPath(image.sprite) == "Resources/unity_builtin_extra";
        }
        CheckResult("settings-use-existing-basic-ui-sprites-and-original-colors", originalColors,
            "All settings Images retain white tint and builtin sprite assets; text keeps Unity default RGB 50/255.");
        CheckResult("settings-selectables-do-not-tint-assets", slider.transition == Selectable.Transition.None
            && toggle.transition == Selectable.Transition.None, "Slider and Toggle retain the project's no-tint default control style.");
    }

    private static void ResumeCallbacks()
    {
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        EditorApplication.playModeStateChanged += PlayModeChanged;
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
        SceneManager.sceneLoaded -= SceneLoaded;
        SceneManager.sceneLoaded += SceneLoaded;
    }
    private static void Tick()
    {
        if (!SessionState.GetBool(Prefix + "Active", false)) return;
        try
        {
            DateTime started = DateTime.Parse(SessionState.GetString(Prefix + "StartedUtc", ""), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            Require((DateTime.UtcNow - started).TotalSeconds < 180d, "Audio settings verification exceeded its 180-second watchdog.");
            Require(Load().errors.Count == 0, "Unity logged an error during audio settings verification.");
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
                ElectricSfxPlayer sfx = Sfx();
                Require(sfx != null, "No persistent SFX service exists.");
                initialSfxId = sfx.GetInstanceID();
                if (IsRead)
                {
                    CheckResult("saved-mute-is-applied-before-first-menu-sound", AllSourcesMatchMute(true)
                        && Near(SfxPreferences.Volume, .37f) && SfxPreferences.IsMuted,
                        "Every one of the seven authored sources is muted before the first saved menu click.");
                    Click(Reference<Button>(menu, "settingsButton"));
                    CheckUi(.37f, true, "new-process-initial-ui");
                    CheckResult("muted-ui-still-plays-with-retained-gain", Reference<AudioSource>(sfx, "uiSource").clip != null
                        && Reference<AudioSource>(sfx, "uiSource").mute, "The actual settings click is gated by source.mute while its clip/gain remain available for unmute.");
                    Click(Reference<Button>(menu, "closeSettingsButton"));
                    PrepareLoad(StagePath);
                    Click(Reference<Button>(menu, "startButton"));
                    CheckAcceptedAsyncLoad();
                    SetPhase(Phase.AwaitStage);
                }
                else
                {
                    CheckResult("missing-keys-default-to-full-volume-unmuted", Near(SfxPreferences.Volume, 1f)
                        && !SfxPreferences.IsMuted && AllSourcesMatchMute(false), "Runtime initialization defaults to 100% and unmuted.");
                    Click(Reference<Button>(menu, "settingsButton"));
                    CheckUi(1f, false, "first-run-default-ui");
                    CheckResult("opening-default-settings-does-not-create-keys", !PlayerPrefs.HasKey(SfxPreferences.VolumeKey)
                        && !PlayerPrefs.HasKey(SfxPreferences.MuteKey), "Refreshing controls uses their no-notify APIs.");
                    CheckResult("one-active-preferences-listener", ViewListenerCount() == 1, "Exactly one enabled settings view listens to Changed.");
                    PreparePlayingSources();
                    SetPhase(Phase.Playing);
                }
                return;
            }
            if (phase == Phase.Playing)
            {
                if (!After(.1d)) return;
                CheckResult("all-seven-existing-sources-are-really-playing", AllPlaying(true),
                    "Existing clips loop only as an unsaved test fixture so real playback survives control changes.");
                changes = 0;
                SfxPreferences.Changed += CountChanged;
                try
                {
                    Slider().value = .35f;
                    CheckUi(.35f, false, "slider-live-change");
                    CheckResult("slider-updates-every-playing-source-immediately", GainsMatch(.35f) && AllPlaying(true)
                        && changes == 1 && Near(PlayerPrefs.GetFloat(SfxPreferences.VolumeKey, -1f), .35f),
                        "Actual Slider event scales seven existing per-cue gains without interrupting playback and saves its key.");
                    Mute().isOn = true;
                    CheckUi(.35f, true, "toggle-live-mute");
                    CheckResult("toggle-mutes-existing-playing-gameplay-and-ui", AllSourcesMatchMute(true) && AllPlaying(true)
                        && GainsMatch(.35f) && PlayerPrefs.GetInt(SfxPreferences.MuteKey, -1) == 1,
                        "Mute immediately affects all six gameplay sources and the UI source without destroying their gain/playback.");
                    Mute().isOn = false;
                    CheckResult("unmute-restores-retained-playing-gains", AllSourcesMatchMute(false) && GainsMatch(.35f)
                        && AllPlaying(true), "Unmute preserves 35% and the active clips.");
                    Slider().value = 0f;
                    CheckUi(0f, false, "zero-volume-is-independent-of-mute");
                    CheckResult("zero-volume-silences-all-without-enabling-mute", GainsMatch(0f) && AllSourcesMatchMute(false)
                        && !SfxPreferences.IsMuted, "Zero on the slider changes gain alone.");
                    Slider().value = .64f;
                    SfxPreferences.SetVolume(float.NaN);
                    SfxPreferences.SetVolume(float.PositiveInfinity);
                    SfxPreferences.SetVolume(float.NegativeInfinity);
                    CheckResult("nonfinite-volume-values-are-rejected", Near(SfxPreferences.Volume, .64f)
                        && GainsMatch(.64f), "NaN and infinity cannot poison persisted values, UI, or source gain.");
                    SfxPreferences.SetVolume(2f);
                    CheckUi(1f, false, "volume-clamp-high");
                    SfxPreferences.SetVolume(-2f);
                    CheckUi(0f, false, "volume-clamp-low");
                    Slider().value = .64f;
                }
                finally { SfxPreferences.Changed -= CountChanged; }
                Time.timeScale = 0f;
                ElectricSfxPlayer.SetGameplayPaused(true);
                pausedTime = Time.fixedTime;
                SetPhase(Phase.Paused);
                return;
            }
            if (phase == Phase.Paused)
            {
                if (!After(.15d)) return;
                AudioSource ui = Reference<AudioSource>(Sfx(), "uiSource");
                bool gameplayStopped = true;
                foreach (AudioSource source in Gameplay()) gameplayStopped &= !source.isPlaying;
                CheckResult("gameplay-sources-are-paused-before-adjustment", gameplayStopped && Time.timeScale == 0f
                    && Near(Time.fixedTime, pausedTime) && ui.isPlaying, "Gameplay clips pause while the existing UI source continues.");
                Slider().value = .42f;
                Mute().isOn = true;
                CheckResult("paused-sources-receive-new-volume-and-mute", GainsMatch(.42f) && AllSourcesMatchMute(true),
                    "All seven existing sources update immediately even when six are paused.");
                Mute().isOn = false;
                CheckUi(.42f, false, "settings-work-at-zero-timescale");
                Time.timeScale = 1f;
                ElectricSfxPlayer.SetGameplayPaused(false);
                SetPhase(Phase.Resume);
                return;
            }
            if (phase == Phase.Resume)
            {
                if (!After(.1d)) return;
                CheckResult("unpause-retains-updated-volume-and-mute", AllPlaying(true) && GainsMatch(.42f)
                    && AllSourcesMatchMute(false), "Existing paused clips resume at the adjusted 42% without resetting their cue balance.");
                CheckReopenAndSave();
                SetPhase(Phase.Finish);
                Finish(true);
                return;
            }
            if (phase == Phase.AwaitStage)
            {
                if (!AwaitLoad(StagePath)) return;
                ElectricStageSession2D session = FindInScene<ElectricStageSession2D>();
                if (session == null || !session.IsInitialized) return;
                CheckPersistence("stage-load");
                PauseMenuController pause = FindInScene<PauseMenuController>();
                Require(pause != null, "Gameplay scene has no saved pause controller.");
                pause.Pause();
                CheckResult("pause-ui-remains-muted-at-saved-volume", pause.IsPaused && AllSourcesMatchMute(true)
                    && Near(SfxPreferences.Volume, .37f), "The actual pause-open UI sound obeys persisted preferences.");
                PrepareLoad(MenuPath);
                Click(Reference<Button>(pause, "mainMenuButton"));
                CheckAcceptedAsyncLoad();
                SetPhase(Phase.AwaitMenu);
                return;
            }
            if (phase == Phase.AwaitMenu)
            {
                if (!AwaitLoad(MenuPath)) return;
                MainMenuController menu = FindInScene<MainMenuController>();
                if (menu == null || !Reference<Button>(menu, "settingsButton").IsInteractable()) return;
                CheckPersistence("return-menu");
                Click(Reference<Button>(menu, "settingsButton"));
                CheckUi(.37f, true, "returned-menu-ui");
                CheckResult("returned-view-subscribes-once", ViewListenerCount() == 1,
                    "The replacement settings view refreshes the persisted values and does not retain a destroyed view listener.");
                Click(Reference<Button>(menu, "closeSettingsButton"));
                RestoreOriginalPreferences();
                CheckResult("original-two-playerprefs-keys-restored", BackupRestoredExactly(),
                    "Original values and presence restored; unrelated PlayerPrefs and company/product are untouched.");
                SetPhase(Phase.Finish);
                Finish(true);
            }
        }
        catch (Exception error) { Fail(error); }
    }

    private static void PreparePlayingSources()
    {
        baseGains.Clear();
        bool noMusic = true;
        int existingCount = 0;
        foreach (AudioSource source in Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None))
        { existingCount++; noMusic &= !source.loop && !source.gameObject.name.ToLowerInvariant().Contains("bgm"); }
        CheckResult("settings-add-no-music-or-extra-audio-source", noMusic && existingCount == 7,
            "The authored menu has only its six gameplay SFX sources and one UI source, with no looping BGM.");
        foreach (AudioSource source in AllSources()) { source.Stop(); source.loop = true; }
        ElectricSfxPlayer.SetGameplayPaused(false);
        foreach (SfxCue cue in new[] { SfxCue.Bounce, SfxCue.Charge, SfxCue.Star, SfxCue.Launch, SfxCue.Record, SfxCue.LearningComplete })
            Require(ElectricSfxPlayer.Play(cue), "Could not start real existing source for " + cue + ".");
        AudioSource ui = Reference<AudioSource>(Sfx(), "uiSource");
        // The opening settings click already loaded the actual UiClick clip; restart that authored source as a loop fixture.
        Require(ui.clip != null, "Saved settings button did not select its UI click sound.");
        ui.Play();
        foreach (AudioSource source in AllSources())
        { Require(source.clip != null && source.volume > 0f, "Existing source has no real cue gain/clip."); baseGains[source] = source.volume; }
        CheckResult("seven-authored-source-gains-retain-cue-balance", baseGains.Count == 7 && HasDifferentGains(),
            "Each actual gameplay/UI cue's original masterVolume × cue.volume gain is recorded; the test uses existing clips only.");
    }
    private static bool HasDifferentGains()
    {
        float first = -1f;
        foreach (float gain in baseGains.Values) { if (first < 0f) first = gain; else if (!Near(first, gain)) return true; }
        return false;
    }
    private static void CheckReopenAndSave()
    {
        foreach (AudioSource source in AllSources()) { source.loop = false; source.Stop(); }
        MainMenuController menu = FindInScene<MainMenuController>();
        for (int i = 0; i < 3; i++)
        {
            Click(Reference<Button>(menu, "closeSettingsButton"));
            CheckResult("closed-view-unsubscribes-" + i, ViewListenerCount() == 0,
                "Closing the actual settings window removes its Changed subscription.");
            Click(Reference<Button>(menu, "settingsButton"));
            CheckUi(.42f, false, "reopened-settings-" + i);
            CheckResult("reopened-view-subscribes-once-" + i, ViewListenerCount() == 1,
                "Repeated enable/disable does not duplicate the active preferences listener.");
        }
        changes = 0;
        SfxPreferences.Changed += CountChanged;
        try
        {
            Slider().value = .37f;
            Mute().isOn = true;
            CheckResult("reopened-controls-emit-one-change-per-user-edit", changes == 2,
                "One slider edit plus one mute edit emits exactly two preference changes after three reopen cycles.");
        }
        finally { SfxPreferences.Changed -= CountChanged; }
        CheckUi(.37f, true, "durable-final-ui");
        Click(Reference<Button>(menu, "closeSettingsButton"));
        SfxPreferences.Save();
        CheckResult("writer-leaves-durable-37-percent-muted-values", Near(PlayerPrefs.GetFloat(SfxPreferences.VolumeKey, -1f), .37f)
            && PlayerPrefs.GetInt(SfxPreferences.MuteKey, -1) == 1, "The second Unity process will verify these persisted values without resetting them.");
    }
    private static void CheckPersistence(string label)
    {
        ElectricSfxPlayer[] all = Object.FindObjectsByType<ElectricSfxPlayer>(FindObjectsSortMode.None);
        CheckResult(label + "-retains-singleton-and-saved-preferences", all.Length == 1 && Sfx().GetInstanceID() == initialSfxId
            && Near(SfxPreferences.Volume, .37f) && SfxPreferences.IsMuted && AllSourcesMatchMute(true)
            && Near(PlayerPrefs.GetFloat(SfxPreferences.VolumeKey, -1f), .37f) && PlayerPrefs.GetInt(SfxPreferences.MuteKey, -1) == 1,
            "The same persistent seven-source SFX service stays muted at 37% across the actual async scene change.");
    }
    private static void PrepareLoad(string target)
    {
        SessionState.SetString(Prefix + "Target", target);
        SessionState.SetBool(Prefix + "Loaded", false);
        SessionState.SetBool(Prefix + "FadeOut", false);
        SessionState.SetBool(Prefix + "FadeIn", false);
    }
    private static void CheckAcceptedAsyncLoad()
    {
        SceneTransitionController transition = Object.FindFirstObjectByType<SceneTransitionController>();
        AsyncOperation operation = Reference<AsyncOperation>(transition, "pendingLoad");
        CheckResult("accepted-async-load-" + Path.GetFileNameWithoutExtension(SessionState.GetString(Prefix + "Target", "")),
            SceneTransitionController.IsBusy && operation != null && !operation.allowSceneActivation && Time.timeScale == 0f,
            "The actual saved button starts async loading with delayed activation while time is paused.");
    }
    private static bool AwaitLoad(string path)
    {
        SceneTransitionController transition = Object.FindFirstObjectByType<SceneTransitionController>();
        if (transition == null) return false;
        CanvasGroup fade = Reference<CanvasGroup>(transition, "fadeOverlay");
        if (SceneTransitionController.IsBusy)
        {
            if (fade.alpha > .01f && fade.alpha < .99f)
                SessionState.SetBool(Prefix + (SessionState.GetBool(Prefix + "Loaded", false) ? "FadeIn" : "FadeOut"), true);
            return false;
        }
        if (SceneManager.GetActiveScene().path != path || !SessionState.GetBool(Prefix + "Loaded", false)) return false;
        CheckResult("async-fade-completes-" + Path.GetFileNameWithoutExtension(path),
            Near(Time.timeScale, 1f) && Near(fade.alpha, 0f) && !fade.blocksRaycasts
            && SessionState.GetBool(Prefix + "FadeIn", false) && SessionState.GetBool(Prefix + "FadeOut", false),
            "Observed fade-out, real sceneLoaded behind opaque fade, fade-in, and normal time restoration.");
        return true;
    }
    private static void SceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!SessionState.GetBool(Prefix + "Active", false) || !EditorApplication.isPlaying
            || scene.path != SessionState.GetString(Prefix + "Target", "")) return;
        SessionState.SetBool(Prefix + "Loaded", true);
        CanvasGroup fade = Reference<CanvasGroup>(Object.FindFirstObjectByType<SceneTransitionController>(), "fadeOverlay");
        CheckResult("incoming-scene-starts-muted-behind-fade-" + Path.GetFileNameWithoutExtension(scene.path),
            SceneTransitionController.IsBusy && Near(Time.timeScale, 0f) && fade.alpha >= .999f && AllSourcesMatchMute(true),
            "Saved mute is already applied during the actual sceneLoaded callback before scene reveal.");
    }
    private static void CheckUi(float volume, bool muted, string name)
    {
        SfxSettingsView view = FindInScene<SfxSettingsView>();
        CheckResult(name, view != null && view.isActiveAndEnabled && Near(Slider().value, volume)
            && Reference<Text>(view, "volumeValueText").text == Mathf.RoundToInt(volume * 100f) + "%"
            && Mute().isOn == muted && Near(SfxPreferences.Volume, volume) && SfxPreferences.IsMuted == muted,
            "Saved slider, percent text, mute toggle and preferences agree: " + Mathf.RoundToInt(volume * 100f) + "%, muted=" + muted + ".");
    }
    private static int ViewListenerCount()
    {
        FieldInfo field = typeof(SfxPreferences).GetField("Changed", BindingFlags.Static | BindingFlags.NonPublic);
        Require(field != null, "Preferences Changed event backing field is unavailable.");
        Delegate callback = field.GetValue(null) as Delegate;
        int count = 0;
        if (callback != null) foreach (Delegate entry in callback.GetInvocationList()) if (entry.Target is SfxSettingsView) count++;
        return count;
    }
    private static void RestoreOriginalPreferences()
    {
        Require(File.Exists(BackupPath), "No preferences backup exists to restore.");
        Backup original = JsonUtility.FromJson<Backup>(File.ReadAllText(BackupPath));
        Require(original.company == Application.companyName && original.product == Application.productName,
            "Refusing to restore preferences under a different company/product.");
        if (original.hasVolume) PlayerPrefs.SetFloat(SfxPreferences.VolumeKey, original.volume); else PlayerPrefs.DeleteKey(SfxPreferences.VolumeKey);
        if (original.hasMute) PlayerPrefs.SetInt(SfxPreferences.MuteKey, original.mute); else PlayerPrefs.DeleteKey(SfxPreferences.MuteKey);
        PlayerPrefs.Save();
        original.restored = true;
        File.WriteAllText(BackupPath, JsonUtility.ToJson(original, true));
    }
    private static bool BackupRestoredExactly()
    {
        Backup original = JsonUtility.FromJson<Backup>(File.ReadAllText(BackupPath));
        return original.restored && PlayerPrefs.HasKey(SfxPreferences.VolumeKey) == original.hasVolume
            && PlayerPrefs.HasKey(SfxPreferences.MuteKey) == original.hasMute
            && (!original.hasVolume || Near(PlayerPrefs.GetFloat(SfxPreferences.VolumeKey), original.volume))
            && (!original.hasMute || PlayerPrefs.GetInt(SfxPreferences.MuteKey) == original.mute);
    }
    private static bool GainsMatch(float volume)
    {
        foreach (var entry in baseGains) if (entry.Key == null || !Near(entry.Key.volume, entry.Value * volume)) return false;
        return baseGains.Count == 7;
    }
    private static bool AllPlaying(bool expected)
    { foreach (AudioSource source in AllSources()) if (source.isPlaying != expected) return false; return true; }
    private static bool AllSourcesMatchMute(bool expected)
    { int count = 0; foreach (AudioSource source in AllSources()) { if (source == null || source.mute != expected) return false; count++; } return count == 7; }
    private static IEnumerable<AudioSource> AllSources()
    { foreach (AudioSource source in Gameplay()) yield return source; yield return Reference<AudioSource>(Sfx(), "uiSource"); }
    private static AudioSource[] Gameplay() => Reference<AudioSource[]>(Sfx(), "gameplaySources");
    private static ElectricSfxPlayer Sfx()
    { foreach (ElectricSfxPlayer player in Object.FindObjectsByType<ElectricSfxPlayer>(FindObjectsSortMode.None)) if (player.gameObject.scene.name == "DontDestroyOnLoad") return player; return Object.FindFirstObjectByType<ElectricSfxPlayer>(); }
    private static Slider Slider() => Reference<Slider>(FindInScene<SfxSettingsView>(), "volumeSlider");
    private static Toggle Mute() => Reference<Toggle>(FindInScene<SfxSettingsView>(), "muteToggle");
    private static T FindInScene<T>() where T : Component
    { foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects()) foreach (T found in root.GetComponentsInChildren<T>(true)) return found; return null; }
    private static T Reference<T>(object owner, string name) where T : class
    {
        Require(owner != null, "Missing owner for " + name + ".");
        FieldInfo field = owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Require(field != null, "Missing field " + name + ".");
        return field.GetValue(owner) as T;
    }
    private static void Click(Button button)
    { Require(button != null && button.IsActive() && button.IsInteractable() && EventSystem.current != null, "Saved button unavailable: " + (button == null ? "missing" : button.name)); button.onClick.Invoke(); }
    private static bool Near(float a, float b) => Mathf.Abs(a - b) < .0001f;
    private static void CountChanged() => changes++;
    private static void SetPhase(Phase phase)
    { SessionState.SetInt(Prefix + "Phase", (int)phase); SessionState.SetFloat(Prefix + "PhaseTime", (float)EditorApplication.timeSinceStartup); }
    private static bool After(double seconds) => EditorApplication.timeSinceStartup - SessionState.GetFloat(Prefix + "PhaseTime", 0f) >= seconds;
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static Report Load() => JsonUtility.FromJson<Report>(File.ReadAllText(ReportPath));
    private static void Save(Report report)
    { Directory.CreateDirectory(Path.GetDirectoryName(ReportPath)); File.WriteAllText(ReportPath, JsonUtility.ToJson(report, true)); }
    private static void CheckResult(string name, bool passed, string details)
    { Report report = Load(); report.checks.Add(new Check { name = name, passed = passed, details = details }); Save(report); Require(passed, "Failed check: " + name + ". " + details); }
    private static void OnLog(string message, string stack, LogType type)
    { if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return; Report report = Load(); report.errors.Add(message + "\n" + stack); Save(report); }
    private static void PlayModeChanged(PlayModeStateChange state)
    { if (SessionState.GetBool(Prefix + "Active", false) && state == PlayModeStateChange.EnteredEditMode) Fail(new InvalidOperationException("Play Mode ended before audio settings verification completed.")); }
    private static void Fail(Exception error)
    { Report report = Load(); report.errors.Add(error.ToString()); Save(report); Finish(false); }
    private static void Finish(bool success)
    {
        SessionState.SetBool(Prefix + "Active", false);
        SfxPreferences.Changed -= CountChanged;
        Report report = Load();
        report.status = success && report.errors.Count == 0 ? "passed" : "failed";
        Save(report);
        SessionState.SetInt(Prefix + "ExitCode", report.status == "passed" ? 0 : 1);
        SessionState.SetBool(Prefix + "Exiting", true);
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        Application.logMessageReceived -= OnLog;
        SceneManager.sceneLoaded -= SceneLoaded;
        Time.timeScale = 1f;
        if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
        EditorApplication.update -= ExitWhenReady;
        EditorApplication.update += ExitWhenReady;
    }
    private static void ExitWhenReady()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        // OnDisable in a settings view may Save its cached values; restore once more after domain teardown.
        if (IsRead && File.Exists(BackupPath)) RestoreOriginalPreferences();
        SessionState.SetBool(Prefix + "Exiting", false);
        EditorApplication.Exit(SessionState.GetInt(Prefix + "ExitCode", 1));
    }
}
