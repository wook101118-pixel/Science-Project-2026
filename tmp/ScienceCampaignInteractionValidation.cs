// Clone-only saved-scene interaction regression. Run -batchmode -force-d3d11, without -quit.
// Mouse input is virtual Input System device data consumed by the original LeverMover.Update.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using sung_wook;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class ScienceCampaignInteractionValidation
{
    const string Prefix = "Codex.ScienceCampaignInteraction.";
    const string Output = "C:/Users/wook1/GitBub-Unity/Science-Project-2026/Logs/ScienceCampaign/Interaction.json";
    [Serializable] sealed class CheckResult { public int stage; public string name, details; public bool passed; }
    [Serializable] sealed class Visual { public int stage; public string name; public bool enabled, active, inViewport; public Vector3 center, size; public int sortingOrder; public float editorAspect, outputAspect; }
    [Serializable] sealed class Report {
        public string status = "running", startedUtc, finishedUtc;
        public bool progressUnchanged;
        public string inputMethod = "QueueStateEvent on a temporary Mouse, Dynamic InputSystem.Update, saved LeverMover.Update invoked once per event. Existing grab collider, camera projection, pivot and reader are used. No lever value or angle setter is used.";
        public List<CheckResult> checks = new List<CheckResult>(); public List<Visual> visuals = new List<Visual>(); public List<string> errors = new List<string>();
    }
    static Mouse mouse, previousMouse;
    static InputSettings.EditorInputBehaviorInPlayMode previousEditorInput;
    static InputSettings.BackgroundBehavior previousBackground;
    static InputSettings.UpdateMode previousUpdateMode;
    static bool previousRunInBackground;
    static StageLever2D lever;
    static global::LeverMover mover;
    static Transform pivot, source;
    static Vector3 sourceBefore;
    static float initialVoltage, sourceChargeBefore;
    static Vector2 fieldBefore;
    static Camera camera;
    static int stage;
    static ScienceCampaignInteractionValidation() {
        if (!Application.isBatchMode) return;
        if (SessionState.GetBool(Prefix + "Active", false)) Subscribe();
        if (SessionState.GetBool(Prefix + "ExitPending", false)) EditorApplication.update += ExitWhenReady;
    }
    public static void RunBatch() {
        try {
            if (!Application.isBatchMode || Path.GetFullPath(Application.dataPath).Replace('\\','/') != "C:/Users/wook1/AppData/Local/Temp/CodexUnityVisualBuild/Assets")
                throw new InvalidOperationException("Only the isolated clone may run this harness.");
            Save(new Report { startedUtc = DateTime.UtcNow.ToString("O") });
            SessionState.SetString(Prefix + "Started", DateTime.UtcNow.ToString("O"));
            SessionState.SetBool(Prefix + "UnlockExists", PlayerPrefs.HasKey(ElectricStageProgression.UnlockKey));
            SessionState.SetInt(Prefix + "UnlockValue", PlayerPrefs.GetInt(ElectricStageProgression.UnlockKey, 1));
            SessionState.SetBool(Prefix + "Active", true); SessionState.SetBool(Prefix + "EnterRequested", false);
            SessionState.SetInt(Prefix + "Stage", 1); SessionState.SetInt(Prefix + "Phase", 0);
            EditorSceneManager.OpenScene(ElectricStageProgression.StageScenePath(1)); Subscribe();
        } catch (Exception e) { Fail(e); }
    }
    static void Subscribe() { EditorApplication.update -= Tick; EditorApplication.update += Tick; Application.logMessageReceived -= OnLog; Application.logMessageReceived += OnLog; }
    static void Tick() {
        if (!SessionState.GetBool(Prefix + "Active", false)) return;
        try {
            if ((DateTime.UtcNow - DateTime.Parse(SessionState.GetString(Prefix + "Started", ""), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)).TotalSeconds > 180)
                throw new TimeoutException("Interaction regression exceeded 180 seconds.");
            if (!EditorApplication.isPlaying) {
                if (!SessionState.GetBool(Prefix + "EnterRequested", false) && !EditorApplication.isCompiling && !EditorApplication.isUpdating && !EditorApplication.isPlayingOrWillChangePlaymode) {
                    SessionState.SetBool(Prefix + "EnterRequested", true); EditorApplication.EnterPlaymode();
                }
                return;
            }
            stage = SessionState.GetInt(Prefix + "Stage", 1);
            if (SceneManager.GetActiveScene().path != ElectricStageProgression.StageScenePath(stage) || SceneTransitionController.IsBusy) return;
            ElectricStageSelector selector = Object.FindFirstObjectByType<ElectricStageSelector>();
            if (selector == null || selector.SelectedStage != stage || selector.GetComponent<ElectricStageSession2D>()?.IsInitialized != true) return;
            int phase = SessionState.GetInt(Prefix + "Phase", 0);
            if (phase == 0) {
                SessionState.SetInt(Prefix + "ReadyFrame", Time.frameCount); SessionState.SetInt(Prefix + "Phase", 1); return;
            }
            if (phase == 1) {
                if (Time.frameCount < SessionState.GetInt(Prefix + "ReadyFrame", 0) + 4) return;
                camera = Camera.main;
                Check("authored-camera-unchanged-after-start", camera != null && camera.orthographic && Mathf.Abs(camera.orthographicSize - 5.8f) < .001f
                    && Vector3.Distance(camera.transform.position, new Vector3(0, .5f, -10)) < .001f,
                    camera == null ? "No main camera" : "position=" + camera.transform.position + ", size=" + camera.orthographicSize + ", rect=" + camera.rect);
                RoundPlayer(selector.Ball);
                if (stage == 1) { Next(); return; }
                PrepareLever(selector);
                SessionState.SetFloat(Prefix + "ActedAt", Time.fixedTime); SessionState.SetInt(Prefix + "Phase", 2); return;
            }
            if (phase == 2) {
                if (Time.fixedTime < SessionState.GetFloat(Prefix + "ActedAt", 0) + .3f) return;
                VerifyEffects(selector);
                Next();
            }
        } catch (Exception e) { Fail(e); }
    }
    static void RoundPlayer(ElectricBall2D ball) {
        ChangePlayerVisual visual = ball.GetComponent<ChangePlayerVisual>();
        SpriteRenderer body = visual != null ? Field<SpriteRenderer>(visual, "bodyRenderer") : null;
        SpriteRenderer overlay = visual != null ? Field<SpriteRenderer>(visual, "chargeRenderer") : null;
        bool circle = body != null && body.sprite != null && (body.sprite.name.IndexOf("circle", StringComparison.OrdinalIgnoreCase) >= 0 || body.sprite.name.IndexOf("disc", StringComparison.OrdinalIgnoreCase) >= 0);
        Check("round-player-visible", circle && body.enabled && body.gameObject.activeInHierarchy && Mathf.Abs(body.bounds.size.x - body.bounds.size.y) < .025f && (overlay == null || !overlay.enabled),
            "body=" + (body != null ? body.name + ", sprite=" + body.sprite?.name + ", size=" + body.bounds.size : "missing") + ", rectangularOverlayEnabled=" + (overlay != null && overlay.enabled));
    }
    static void PrepareLever(ElectricStageSelector selector) {
        lever = Object.FindFirstObjectByType<StageLever2D>();
        if (lever == null || lever.Reader == null) throw new InvalidOperationException("Stage " + stage + " has no active saved lever.");
        mover = Field<global::LeverMover>(lever.Reader, "lever");
        pivot = Field<Transform>(mover, "pivot"); Collider2D grab = Field<Collider2D>(mover, "grabArea");
        Camera moverCamera = Field<Camera>(mover, "cam");
        if (mover == null || pivot == null || grab == null || moverCamera == null) throw new InvalidOperationException("Saved mouse lever references are incomplete.");
        Check("lever-input-is-active-and-physical", mover.isActiveAndEnabled && grab.isActiveAndEnabled && moverCamera == camera && !Field<bool>(lever, "useManualValue"),
            "Mover=" + mover.isActiveAndEnabled + ", grab=" + grab.isActiveAndEnabled + ", currentCamera=" + (moverCamera == camera) + ", manual=" + Field<bool>(lever,"useManualValue"));
        int visible = 0, total = 0;
        Report report = Load();
        // Batch Editor's GameView may be square. Evaluate the intended 1600x900 frame,
        // preserving the authored partial viewport, without saving or changing runtime state.
        float editorAspect = camera.aspect;
        float outputAspect = (1600f * camera.rect.width) / (900f * camera.rect.height);
        camera.aspect = outputAspect;
        foreach (SpriteRenderer renderer in lever.GetComponentsInChildren<SpriteRenderer>(true)) {
            if (!renderer.name.StartsWith("Lab Lever ", StringComparison.Ordinal)) continue;
            total++;
            Vector3 a = camera.WorldToViewportPoint(renderer.bounds.min), b = camera.WorldToViewportPoint(renderer.bounds.max);
            bool inside = a.z > 0 && a.x >= 0 && a.y >= 0 && b.x <= 1 && b.y <= 1;
            bool shown = renderer.enabled && renderer.gameObject.activeInHierarchy && renderer.sprite != null && renderer.color.a > .9f && inside;
            if (shown) visible++;
            report.visuals.Add(new Visual { stage = stage, name = renderer.name, enabled = renderer.enabled, active = renderer.gameObject.activeInHierarchy, inViewport = inside,
                center = renderer.bounds.center, size = renderer.bounds.size, sortingOrder = renderer.sortingOrder, editorAspect = editorAspect, outputAspect = outputAspect });
        }
        camera.aspect = editorAspect;
        Save(report);
        Check("all-four-authored-lever-parts-visible-in-1600x900-frame", visible == 4 && total == 4, "Visible=" + visible + "/" + total + ". Base, axis, arm and grip are active, opaque and inside the actual saved viewport at output resolution. Editor aspect=" + editorAspect + ", output aspect=" + outputAspect);
        source = null;
        if (stage == 2) source = selector.GetStageComponent<ContactCharger2D>(2).transform;
        if (stage == 3) { var launcher = selector.GetStageComponent<DistanceLauncher2D>(3); source = Field<Transform>(launcher, "source"); sourceChargeBefore = launcher.SourceChargeMagnitude; }
        if (stage == 4) fieldBefore = selector.GetStageComponent<UniformField2D>(4).FieldDirection;
        if (stage == 5) initialVoltage = selector.GetStageComponent<VoltageLauncher2D>(5).Voltage;
        if (source != null) sourceBefore = source.position;
        if (mouse == null) {
            previousMouse = Mouse.current;
            previousEditorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            previousBackground = InputSystem.settings.backgroundBehavior;
            previousUpdateMode = InputSystem.settings.updateMode;
            previousRunInBackground = Application.runInBackground;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
            mouse = InputSystem.AddDevice<Mouse>("Science campaign QA mouse");
        }
        mouse.MakeCurrent(); Application.runInBackground = true;
        Physics2D.SyncTransforms();
        Vector3 hit = grab.bounds.center;
        Check("saved-grab-collider-hit", grab.OverlapPoint(hit), "Grab center=" + hit + ", screen=" + camera.WorldToScreenPoint(hit));
        MouseAt(hit, false); MouseAt(hit, true);
        MouseAt(pivot.position + Vector3.left * .9f + Vector3.up * .02f, true);
        Check("actual-mouse-left-drag-changes-angle-and-reader", mover.Angle >= 60 && lever.Value >= .7f && lever.SignedValue >= .7f,
            "angle=" + mover.Angle + ", Value=" + lever.Value + ", SignedValue=" + lever.SignedValue);
        if (stage == 2) Check("contact-electrode-positive-sign-from-mouse", selector.GetStageComponent<ContactCharger2D>(2).SignedCharge > .7f, "SignedCharge=" + selector.GetStageComponent<ContactCharger2D>(2).SignedCharge);
        if (stage == 4) {
            Vector2 after = selector.GetStageComponent<UniformField2D>(4).FieldDirection;
            Check("mouse-drag-flips-uniform-field-up", fieldBefore.y < -.5f && after.y > .5f, "Before=" + fieldBefore + ", after=" + after);
        }
        MouseAt(pivot.position + Vector3.right * .9f + Vector3.up * .02f, true);
        Check("actual-mouse-right-drag-reaches-negative-end", mover.Angle <= -60 && lever.SignedValue <= -.7f, "angle=" + mover.Angle + ", signed=" + lever.SignedValue);
        if (stage == 2) Check("contact-electrode-negative-sign-from-mouse", selector.GetStageComponent<ContactCharger2D>(2).SignedCharge < -.7f, "SignedCharge=" + selector.GetStageComponent<ContactCharger2D>(2).SignedCharge);
        if (stage == 4) Check("mouse-drag-flips-uniform-field-down", selector.GetStageComponent<UniformField2D>(4).FieldDirection.y < -.5f, "Direction=" + selector.GetStageComponent<UniformField2D>(4).FieldDirection);
        MouseAt(pivot.position + Vector3.left * .9f + Vector3.up * .02f, true);
        MouseAt(pivot.position + Vector3.left * .9f + Vector3.up * .02f, false);
        float released = mover.Angle;
        MouseAt(pivot.position + Vector3.up, false);
        Check("mouse-release-stops-dragging", !Field<bool>(mover, "dragging") && Mathf.Abs(mover.Angle - released) < .001f, "Released angle=" + released + ", later angle=" + mover.Angle);
        if (source != null) Check("electrode-static-during-mouse-drag", Vector3.Distance(sourceBefore, source.position) < .000001f, "Before=" + sourceBefore + ", after=" + source.position);
    }
    static void VerifyEffects(ElectricStageSelector selector) {
        if (source != null) Check("electrode-static-after-fixed-updates", Vector3.Distance(sourceBefore, source.position) < .000001f, "Position=" + source.position);
        if (stage == 3) {
            float actual = selector.GetStageComponent<DistanceLauncher2D>(3).SourceChargeMagnitude;
            Check("mouse-lever-updates-source-charge", actual > sourceChargeBefore + .2f && actual >= .85f, "Q before=" + sourceChargeBefore + ", after=" + actual);
        }
        if (stage == 5) {
            float actual = selector.GetStageComponent<VoltageLauncher2D>(5).Voltage;
            Check("mouse-lever-charges-voltage-over-real-fixed-time", actual > initialVoltage + 3, "V before=" + initialVoltage + ", after=" + actual + ", elapsed=" + (Time.fixedTime - SessionState.GetFloat(Prefix + "ActedAt", 0)));
        }
    }
    static void MouseAt(Vector3 world, bool pressed) {
        Vector3 screen = camera.WorldToScreenPoint(world);
        InputSystem.QueueStateEvent(mouse, new MouseState { position = new Vector2(screen.x, screen.y), buttons = (ushort)(pressed ? 1 : 0) });
        MethodInfo update = typeof(InputSystem).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(InputUpdateType) }, null);
        if (update == null) throw new InvalidOperationException("Missing Input System Dynamic dispatcher.");
        update.Invoke(null, new object[] { InputUpdateType.Dynamic });
        typeof(global::LeverMover).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(mover, null);
    }
    static T Field<T>(object target, string name) {
        if (target == null) throw new InvalidOperationException("Missing object for field " + name);
        return (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(target);
    }
    static void Next() {
        if (stage == 5) { Finish(); return; }
        SessionState.SetInt(Prefix + "Stage", stage + 1); SessionState.SetInt(Prefix + "Phase", 0);
        SceneManager.LoadScene(ElectricStageProgression.StageScenePath(stage + 1), LoadSceneMode.Single);
    }
    static void Check(string name, bool passed, string details) { Report r = Load(); r.checks.Add(new CheckResult { stage = stage, name = name, passed = passed, details = details }); if (!passed) r.errors.Add("Stage " + stage + ": " + name + " — " + details); Save(r); }
    static Report Load() => File.Exists(Output) ? JsonUtility.FromJson<Report>(File.ReadAllText(Output)) : new Report();
    static void Save(Report report) { Directory.CreateDirectory(Path.GetDirectoryName(Output)); File.WriteAllText(Output, JsonUtility.ToJson(report, true)); }
    static void OnLog(string message, string stack, LogType type) { if (!SessionState.GetBool(Prefix + "Active", false) || (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)) return; Report r = Load(); r.errors.Add(message + "\n" + stack); Save(r); }
    static void Fail(Exception e) { Report r = Load(); r.errors.Add(e.ToString()); Save(r); Finish(); }
    static void Finish() {
        SessionState.SetBool(Prefix + "Active", false); EditorApplication.update -= Tick; Application.logMessageReceived -= OnLog;
        if (mouse != null) {
            if (mouse.added) InputSystem.RemoveDevice(mouse);
            InputSystem.settings.editorInputBehaviorInPlayMode = previousEditorInput;
            InputSystem.settings.backgroundBehavior = previousBackground;
            InputSystem.settings.updateMode = previousUpdateMode;
            Application.runInBackground = previousRunInBackground;
        }
        mouse = null;
        if (previousMouse != null && previousMouse.added) previousMouse.MakeCurrent();
        Report r = Load(); r.progressUnchanged = PlayerPrefs.HasKey(ElectricStageProgression.UnlockKey) == SessionState.GetBool(Prefix + "UnlockExists", false)
            && PlayerPrefs.GetInt(ElectricStageProgression.UnlockKey, 1) == SessionState.GetInt(Prefix + "UnlockValue", 1);
        if (!r.progressUnchanged) r.errors.Add("Unexpected progression preference change.");
        r.status = r.errors.Count == 0 && r.checks.Count >= 30 ? "passed" : "failed"; r.finishedUtc = DateTime.UtcNow.ToString("O"); Save(r);
        SessionState.SetInt(Prefix + "ExitCode", r.status == "passed" ? 0 : 1); SessionState.SetBool(Prefix + "ExitPending", true);
        EditorApplication.update -= ExitWhenReady; EditorApplication.update += ExitWhenReady;
        if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
    }
    static void ExitWhenReady() {
        if (!SessionState.GetBool(Prefix + "ExitPending", false)) { EditorApplication.update -= ExitWhenReady; return; }
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        SessionState.SetBool(Prefix + "ExitPending", false); EditorApplication.update -= ExitWhenReady;
        EditorApplication.Exit(SessionState.GetInt(Prefix + "ExitCode", 1));
    }
}

