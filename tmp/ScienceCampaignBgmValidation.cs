// Temporary clone-only integration harness. Run writer, then reader in separate Unity
// processes with -batchmode -force-audio (without -quit). Recovery restores only BGM keys.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using sung_wook;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class BgmSettingsValidation
{
    const string Prefix = "Codex.BgmSettingsValidation.";
    const string Scenes = "Assets/@Project/@sung_wook/@Game/00_Scenes/@InGame/";
    const string Menu = Scenes + "MainMenu.unity";
    const string Output = "C:/Users/wook1/GitBub-Unity/Science-Project-2026/Logs/ScienceCampaign/BgmRegression";
    enum Phase { EnterPlay, Menu, Playback, AwaitStage, Paused, AwaitMenu, Finish }
    [Serializable] sealed class Check { public string name; public bool passed; public string details; }
    [Serializable] sealed class Report { public string status = "running", mode, startedUtc; public List<Check> checks = new List<Check>(); public List<string> errors = new List<string>(); }
    [Serializable] sealed class Backup { public bool hasVolume, hasMute, restored; public float volume; public int mute; public string company, product; }
    sealed class SfxState { public AudioSource source; public float volume; public bool muted; }
    static readonly List<SfxState> sfxSources = new List<SfxState>();
    static int bgmId, sample, stage;
    static float sfxVolume;
    static bool sfxMuted, sfxHasVolume, sfxHasMute;
    static float sfxSavedVolume;
    static int sfxSavedMute;
    static bool Read => SessionState.GetBool(Prefix + "Read", false);
    static string ReportPath => Path.Combine(Output, Read ? "Read.json" : "Write.json");
    static string BackupPath => Path.GetFullPath(Path.Combine(Application.dataPath, "../tmp/BgmSettingsPlayerPrefsBackup.json"));
    static string StagePath(int number) => Scenes + "Stage/Stage_" + number.ToString("00") + ".unity";

    static BgmSettingsValidation()
    {
        if (!Application.isBatchMode) return;
        if (SessionState.GetBool(Prefix + "Active", false)) Subscribe();
        if (SessionState.GetBool(Prefix + "Exiting", false)) EditorApplication.update += ExitWhenReady;
    }
    public static void RunWriteBatch() => Start(false);
    public static void RunReadBatch() => Start(true);
    public static void RestoreBackupBatch() { RequireIsolated(); Restore(); EditorApplication.Exit(0); }
    static void Start(bool read)
    {
        RequireIsolated();
        SessionState.SetBool(Prefix + "Read", read);
        Save(new Report { mode = read ? "Separate-process persistence and six-scene settings verification" : "Authored UI events, real BGM playback, SFX independence and six-scene continuity", startedUtc = DateTime.UtcNow.ToString("O") });
        try
        {
            if (read)
                CheckResult("second-process-loads-written-preferences", File.Exists(BackupPath) && PlayerPrefs.HasKey(BGMPreferences.VolumeKey)
                    && Near(PlayerPrefs.GetFloat(BGMPreferences.VolumeKey), .37f) && PlayerPrefs.GetInt(BGMPreferences.MuteKey) == 1,
                    "Before Play Mode, the second process reads 37% and muted=1 saved by the first process.");
            else
            {
                Require(!File.Exists(BackupPath) || JsonUtility.FromJson<Backup>(File.ReadAllText(BackupPath)).restored, "An unrestored BGM backup exists. Run RestoreBackupBatch first.");
                var backup = new Backup { hasVolume = PlayerPrefs.HasKey(BGMPreferences.VolumeKey), volume = PlayerPrefs.GetFloat(BGMPreferences.VolumeKey, 1f),
                    hasMute = PlayerPrefs.HasKey(BGMPreferences.MuteKey), mute = PlayerPrefs.GetInt(BGMPreferences.MuteKey), company = Application.companyName, product = Application.productName };
                Directory.CreateDirectory(Path.GetDirectoryName(BackupPath));
                File.WriteAllText(BackupPath, JsonUtility.ToJson(backup, true));
                PlayerPrefs.DeleteKey(BGMPreferences.VolumeKey); PlayerPrefs.DeleteKey(BGMPreferences.MuteKey); PlayerPrefs.Save();
            }
            SessionState.SetBool(Prefix + "Active", true); SessionState.SetBool(Prefix + "Exiting", false);
            SessionState.SetBool(Prefix + "EnterRequested", false); SessionState.SetString(Prefix + "Started", DateTime.UtcNow.ToString("O"));
            SetPhase(Phase.EnterPlay); EditorSceneManager.OpenScene(Menu, OpenSceneMode.Single); Subscribe();
        }
        catch (Exception error) { Fail(error); }
    }
    static void RequireIsolated() => Require(Application.isBatchMode && Application.dataPath.Replace('\\', '/').Contains("/Temp/CodexUnityVisualBuild/"), "Run only in the isolated temporary Unity clone.");
    static void Subscribe()
    {
        EditorApplication.update -= Tick; EditorApplication.update += Tick;
        Application.logMessageReceived -= OnLog; Application.logMessageReceived += OnLog;
    }
    static void Tick()
    {
        if (!SessionState.GetBool(Prefix + "Active", false)) return;
        try
        {
            Require((DateTime.UtcNow - DateTime.Parse(SessionState.GetString(Prefix + "Started", ""), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)).TotalSeconds < 240, "BGM integration watchdog exceeded 240 seconds.");
            Require(Load().errors.Count == 0, "Unity logged an error during BGM verification.");
            Phase phase = (Phase)SessionState.GetInt(Prefix + "Phase", 0);
            if (!EditorApplication.isPlaying)
            {
                if (phase == Phase.EnterPlay && !SessionState.GetBool(Prefix + "EnterRequested", false) && !EditorApplication.isCompiling && !EditorApplication.isUpdating && !EditorApplication.isPlayingOrWillChangePlaymode)
                { SessionState.SetBool(Prefix + "EnterRequested", true); EditorApplication.EnterPlaymode(); }
                return;
            }
            if (phase == Phase.EnterPlay) { SetPhase(Phase.Menu); return; }
            if (phase == Phase.Menu)
            {
                if (SceneTransitionController.IsBusy || !After(.3)) return;
                MainMenuController menu = Find<MainMenuController>(); if (menu == null) return;
                BGM[] music = Object.FindObjectsByType<BGM>(FindObjectsSortMode.None);
                Require(music.Length == 1, "The menu must have exactly one BGM component."); bgmId = music[0].GetInstanceID();
                AudioSource audio = Music();
                CheckResult("authored-music-is-loaded-looping-and-playing", audio.clip != null && audio.clip.loadState == AudioDataLoadState.Loaded && audio.loop && audio.isPlaying && audio.clip.samples > 0,
                    "The authored clip is loaded and playback is active; this verifies playback state and samples, not audible output amplitude.");
                Click(Ref<Button>(menu, "settingsButton")); CaptureSfx(true); CheckUi(Read ? .37f : 1f, Read, "initial-menu");
                if (!Read)
                {
                    CheckResult("opening-settings-does-not-write-default-keys", !PlayerPrefs.HasKey(BGMPreferences.VolumeKey) && !PlayerPrefs.HasKey(BGMPreferences.MuteKey), "No-notify UI refresh leaves missing preferences absent.");
                    Slider().value = .37f; CheckUi(.37f, false, "slider-37-percent");
                    Mute().isOn = true; CheckUi(.37f, true, "mute-retains-volume");
                    Mute().isOn = false; CheckUi(.37f, false, "unmute-restores-gain");
                    Slider().value = .81f; CheckUi(.81f, false, "slider-81-percent");
                    Slider().value = 0f; CheckUi(0f, false, "zero-does-not-enable-mute");
                    BGMPreferences.SetVolume(2f); CheckUi(1f, false, "clamp-high");
                    BGMPreferences.SetVolume(-2f); CheckUi(0f, false, "clamp-low");
                    Slider().value = .81f;
                    BGMPreferences.SetVolume(float.NaN); BGMPreferences.SetVolume(float.PositiveInfinity); BGMPreferences.SetVolume(float.NegativeInfinity);
                    CheckUi(.81f, false, "nonfinite-values-rejected"); Slider().value = .37f; Mute().isOn = true;
                }
                CheckSfx("menu-controls-leave-sfx-unchanged"); sample = audio.timeSamples; SetPhase(Phase.Playback); return;
            }
            if (phase == Phase.Playback)
            {
                if (!After(.4)) return;
                CheckResult("muting-retains-running-playback", Music().isPlaying && Music().mute && Music().timeSamples != sample, "Muted audio sample position advances without stopping or restarting the clip.");
                MainMenuController menu = Find<MainMenuController>(); Click(Ref<Button>(menu, "closeSettingsButton"));
                stage = 1; sample = Music().timeSamples; Click(Ref<Button>(menu, "startButton"));
                Require(SceneTransitionController.IsBusy, "The saved Start button did not start the tutorial transition."); SetPhase(Phase.AwaitStage); return;
            }
            if (phase == Phase.AwaitStage)
            {
                if (!Arrived(StagePath(stage))) return;
                CheckSingleton("stage-" + stage + "-preserves-music");
                PauseMenuController pause = Find<PauseMenuController>(); Require(pause != null, "Saved stage has no pause controller."); pause.Pause(); if (!pause.IsPaused) return;
                Click(Ref<Button>(pause, "settingsButton")); Require(pause.IsSettingsOpen && Near(Time.timeScale, 0f), "The actual pause settings must open at timeScale zero.");
                CheckUi(.37f, true, "stage-" + stage + "-saved-settings");
                CaptureSfx();
                Slider().value = .81f; Mute().isOn = false; CheckUi(.81f, false, "stage-" + stage + "-paused-controls");
                Slider().value = .37f; Mute().isOn = true; CheckSfx("stage-" + stage + "-sfx-independent");
                sample = Music().timeSamples; SetPhase(Phase.Paused); return;
            }
            if (phase == Phase.Paused)
            {
                if (!After(.4)) return;
                CheckResult("stage-" + stage + "-music-continues-while-paused", Near(Time.timeScale, 0f) && Music().isPlaying && Music().timeSamples != sample,
                    "Music sample position advances while the saved pause menu holds game time at zero.");
                PauseMenuController pause = Find<PauseMenuController>(); Click(Ref<Button>(pause, "closeSettingsButton"));
                sample = Music().timeSamples; Click(Ref<Button>(pause, "mainMenuButton")); Require(SceneTransitionController.IsBusy, "The actual pause Main Menu button did not accept the transition.");
                SetPhase(Phase.AwaitMenu); return;
            }
            if (phase == Phase.AwaitMenu)
            {
                if (!Arrived(Menu)) return;
                MainMenuController menu = Find<MainMenuController>();
                if (menu == null || !Ref<Button>(menu, "settingsButton").IsActive() || !Ref<Button>(menu, "settingsButton").IsInteractable()) return;
                CheckSingleton("return-from-stage-" + stage);
                Click(Ref<Button>(menu, "settingsButton")); CheckUi(.37f, true, "returned-menu-" + stage); Click(Ref<Button>(menu, "closeSettingsButton"));
                if (++stage <= 5)
                {
                    sample = Music().timeSamples; Require(SceneTransitionController.LoadScene(StagePath(stage)), "Next saved stage transition was refused."); SetPhase(Phase.AwaitStage);
                }
                else
                {
                    BGMPreferences.Save(); CheckResult("final-preferences-persisted", Near(PlayerPrefs.GetFloat(BGMPreferences.VolumeKey), .37f) && PlayerPrefs.GetInt(BGMPreferences.MuteKey) == 1, "The writer leaves durable 37% and mute=1 for a separate Unity process.");
                    Finish(true);
                }
            }
        }
        catch (Exception error) { Fail(error); }
    }
    static void CheckSingleton(string name)
    {
        BGM[] all = Object.FindObjectsByType<BGM>(FindObjectsSortMode.None); AudioSource audio = Music(); int musicSources = 0;
        foreach (AudioSource source in Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None)) if (source.clip == audio.clip) musicSources++;
        CheckResult(name, all.Length == 1 && all[0].GetInstanceID() == bgmId && all[0].gameObject.scene.name == "DontDestroyOnLoad"
            && musicSources == 1 && audio.isPlaying && audio.timeSamples > sample && Near(audio.volume, all[0].Volume * .37f) && audio.mute,
            "The identical persistent BGM instance and its sole clip source continue across the real scene load with 37% and mute retained.");
    }
    static void CheckUi(float volume, bool muted, string name)
    {
        BGMSettingView view = Find<BGMSettingView>(); AudioSource audio = Music();
        CheckResult(name, view != null && view.isActiveAndEnabled && Near(Slider().minValue, 0f) && Near(Slider().maxValue, 1f)
            && !Slider().wholeNumbers && Near(Slider().value, volume) && Mute().isOn == muted
            && Ref<Text>(view, "volumeValueText").text == Mathf.RoundToInt(volume * 100f) + "%"
            && Near(BGMPreferences.Volume, volume) && BGMPreferences.IsMuted == muted
            && Near(audio.volume, audio.GetComponent<BGM>().Volume * volume) && audio.mute == muted,
            "Authored UI references, percent text, independent mute, saved preferences and actual AudioSource gain agree immediately.");
    }
    static void CaptureSfx(bool preferences = false)
    {
        sfxSources.Clear(); ElectricSfxPlayer player = Object.FindFirstObjectByType<ElectricSfxPlayer>(); Require(player != null, "No existing SFX player.");
        foreach (AudioSource audio in player.GetComponentsInChildren<AudioSource>(true)) sfxSources.Add(new SfxState { source = audio, volume = audio.volume, muted = audio.mute });
        Require(sfxSources.Count > 0, "No existing SFX sources.");
        if (!preferences) return;
        sfxVolume = SfxPreferences.Volume; sfxMuted = SfxPreferences.IsMuted;
        sfxHasVolume = PlayerPrefs.HasKey(SfxPreferences.VolumeKey); sfxHasMute = PlayerPrefs.HasKey(SfxPreferences.MuteKey);
        sfxSavedVolume = PlayerPrefs.GetFloat(SfxPreferences.VolumeKey); sfxSavedMute = PlayerPrefs.GetInt(SfxPreferences.MuteKey);
    }
    static void CheckSfx(string name)
    {
        bool same = Near(SfxPreferences.Volume, sfxVolume) && SfxPreferences.IsMuted == sfxMuted
            && PlayerPrefs.HasKey(SfxPreferences.VolumeKey) == sfxHasVolume && PlayerPrefs.HasKey(SfxPreferences.MuteKey) == sfxHasMute
            && (!sfxHasVolume || Near(PlayerPrefs.GetFloat(SfxPreferences.VolumeKey), sfxSavedVolume)) && (!sfxHasMute || PlayerPrefs.GetInt(SfxPreferences.MuteKey) == sfxSavedMute);
        foreach (SfxState state in sfxSources) same &= state.source != null && Near(state.source.volume, state.volume) && state.source.mute == state.muted;
        CheckResult(name, same, "BGM edits preserve existing SFX source gains/mute and leave both SFX preference keys unchanged.");
    }
    static AudioSource Music() { BGM music = Object.FindFirstObjectByType<BGM>(); Require(music != null, "No BGM instance."); AudioSource source = music.GetComponent<AudioSource>(); Require(source != null, "BGM has no AudioSource."); return source; }
    static Slider Slider() => Ref<Slider>(Find<BGMSettingView>(), "volumeSlider");
    static Toggle Mute() => Ref<Toggle>(Find<BGMSettingView>(), "muteToggle");
    static T Find<T>() where T : Component
    {
        T inactive = null;
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects()) foreach (T found in root.GetComponentsInChildren<T>(true))
        {
            if (found.gameObject.activeInHierarchy && (!(found is Behaviour behaviour) || behaviour.enabled)) return found;
            if (inactive == null) inactive = found;
        }
        return inactive;
    }
    static T Ref<T>(object owner, string field) where T : class { Require(owner != null, "No owner for " + field); FieldInfo info = owner.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public); Require(info != null, "Missing field " + field); T value = info.GetValue(owner) as T; Require(value != null, "Unwired field " + field); return value; }
    static void Click(Button button) { Require(button != null && button.IsActive() && button.IsInteractable(), "Saved button unavailable."); button.onClick.Invoke(); }
    static bool Arrived(string path) => !SceneTransitionController.IsBusy && SceneManager.GetActiveScene().path == path && After(.2);
    static bool Near(float a, float b) => Mathf.Abs(a - b) < .0001f;
    static bool After(double seconds) => EditorApplication.timeSinceStartup - SessionState.GetFloat(Prefix + "PhaseTime", 0f) >= seconds;
    static void SetPhase(Phase phase) { SessionState.SetInt(Prefix + "Phase", (int)phase); SessionState.SetFloat(Prefix + "PhaseTime", (float)EditorApplication.timeSinceStartup); }
    static void Restore()
    {
        Require(File.Exists(BackupPath), "No BGM preferences backup."); Backup original = JsonUtility.FromJson<Backup>(File.ReadAllText(BackupPath));
        Require(original.company == Application.companyName && original.product == Application.productName, "BGM backup company/product mismatch.");
        if (original.hasVolume) PlayerPrefs.SetFloat(BGMPreferences.VolumeKey, original.volume); else PlayerPrefs.DeleteKey(BGMPreferences.VolumeKey);
        if (original.hasMute) PlayerPrefs.SetInt(BGMPreferences.MuteKey, original.mute); else PlayerPrefs.DeleteKey(BGMPreferences.MuteKey);
        PlayerPrefs.Save(); Require(PlayerPrefs.HasKey(BGMPreferences.VolumeKey) == original.hasVolume && PlayerPrefs.HasKey(BGMPreferences.MuteKey) == original.hasMute
            && (!original.hasVolume || PlayerPrefs.GetFloat(BGMPreferences.VolumeKey) == original.volume) && (!original.hasMute || PlayerPrefs.GetInt(BGMPreferences.MuteKey) == original.mute), "Original BGM preferences did not restore exactly.");
        original.restored = true; File.WriteAllText(BackupPath, JsonUtility.ToJson(original, true));
    }
    static Report Load() => JsonUtility.FromJson<Report>(File.ReadAllText(ReportPath));
    static void Save(Report report) { Directory.CreateDirectory(Output); File.WriteAllText(ReportPath, JsonUtility.ToJson(report, true)); }
    static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    static void CheckResult(string name, bool passed, string details) { Report report = Load(); report.checks.Add(new Check { name = name, passed = passed, details = details }); Save(report); Require(passed, name + ": " + details); }
    static void OnLog(string message, string stack, LogType type) { if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return; Report report = Load(); report.errors.Add(message + "\n" + stack); Save(report); }
    static void Fail(Exception error) { Report report = Load(); report.errors.Add(error.ToString()); Save(report); Finish(false); }
    static void Finish(bool success)
    {
        SessionState.SetBool(Prefix + "Active", false); Report report = Load(); report.status = success && report.errors.Count == 0 ? "passed" : "failed"; Save(report);
        SessionState.SetBool(Prefix + "Restore", Read || report.status != "passed"); SessionState.SetInt(Prefix + "ExitCode", report.status == "passed" ? 0 : 1);
        SessionState.SetBool(Prefix + "Exiting", true); EditorApplication.update -= Tick; Application.logMessageReceived -= OnLog; Time.timeScale = 1f;
        if (EditorApplication.isPlaying) EditorApplication.isPlaying = false; EditorApplication.update -= ExitWhenReady; EditorApplication.update += ExitWhenReady;
    }
    static void ExitWhenReady()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (SessionState.GetBool(Prefix + "Restore", false) && File.Exists(BackupPath)) { Restore(); CheckResult("original-bgm-preferences-restored", true, "After Play Mode teardown, original BGM key presence and exact typed values were restored; SFX keys were never modified."); }
        SessionState.SetBool(Prefix + "Exiting", false); EditorApplication.Exit(SessionState.GetInt(Prefix + "ExitCode", 1));
    }
}

