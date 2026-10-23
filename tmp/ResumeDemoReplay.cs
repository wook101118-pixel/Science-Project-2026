// Isolated replay recorder. It feeds runtime InputSystem devices through the saved game handlers.
// It does not change gameplay components, transforms, settings, preferences, or UI layout.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using sung_wook;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

[DefaultExecutionOrder(-5000)]
public sealed class ResumeDemoReplay : MonoBehaviour
{
    private const string Folder = "C:/Users/wook1/GitBub-Unity/Science-Project-2026/Logs/ResumeDemoReplay";
    [Serializable] private sealed class Electrode
    {
        public string name;
        public Vector3 transformPosition;
        public Vector2 rigidbodyPosition;
        public float charge;
    }
    [Serializable] private sealed class BallSprite
    {
        public string name, sprite;
        public Vector2 spritePixels;
        public Vector3 scale, boundsSize;
        public Color color;
    }
    [Serializable] private sealed class Sample
    {
        public string phase, timestampUtc, scene, image;
        public int frame;
        public float realtime, timeScale, signedPlayerCharge, leverAngle, leverValue;
        public bool paused, transitionBusy;
        public Vector3 playerPosition;
        public List<Electrode> electrodes = new List<Electrode>();
        public List<BallSprite> sprites = new List<BallSprite>();
    }
    [Serializable] private sealed class Audit
    {
        public string status = "running", startedUtc = DateTime.UtcNow.ToString("O"), finishedUtc;
        public string method = "Programmatic Unity InputSystem replay through actual saved PlayerController, LeverMover and PauseMenuController; saved Button.onClick routes; actual ScreenCapture frames.";
        public int fps = 10, width, height, capturedFrames;
        public bool positivePlayerObserved, pauseObserved, returnedToMainMenu, electrodePositionInvariant;
        public float maximumElectrodeDisplacement, minimumLeverAngle, maximumLeverAngle;
        public List<Sample> phases = new List<Sample>();
        public List<string> errors = new List<string>();
        public List<string> uiRoutes = new List<string>();
        public List<float> frameTimes = new List<float>();
    }
    private Audit audit = new Audit();
    private Mouse replayMouse;
    private Keyboard replayKeyboard;
    private global::LeverMover mover;
    private Collider2D grab;
    private Transform pivot;
    private readonly Dictionary<int, Vector3> sourcePositions = new Dictionary<int, Vector3>();
    private bool recording;
    private string pendingPhase;
    private float nextFrame;
    private InputSettings.UpdateMode previousUpdateMode;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (Application.isEditor || Array.IndexOf(Environment.GetCommandLineArgs(), "--record-live-demo") < 0) return;
        var host = new GameObject("Opt-in Runtime Input Replay Recorder");
        Object.DontDestroyOnLoad(host); host.AddComponent<ResumeDemoReplay>();
    }
    private void Awake()
    {
        Directory.CreateDirectory(Folder); Directory.CreateDirectory(Folder + "/Frames");
        Application.logMessageReceived += OnLog;
    }
    private void OnDestroy()
    {
        Application.logMessageReceived -= OnLog;
        InputSystem.settings.updateMode = previousUpdateMode;
    }
    private void Start()
    {
        previousUpdateMode = InputSystem.settings.updateMode;
        InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
        replayKeyboard = InputSystem.AddDevice<Keyboard>("ReplayKeyboard");
        replayMouse = InputSystem.AddDevice<Mouse>("ReplayMouse");
        recording = true; StartCoroutine(CaptureFrames()); StartCoroutine(DriveReplay());
    }
    private void Update()
    {
        if (replayMouse == null || replayKeyboard == null) return;
        replayMouse.MakeCurrent(); replayKeyboard.MakeCurrent();
        InputSystem.Update();
    }
    private IEnumerator DriveReplay()
    {
        var stack = new Stack<IEnumerator>(); stack.Push(RunReplay());
        while (stack.Count > 0)
        {
            object next = null; bool hasNext = false;
            try { hasNext = stack.Peek().MoveNext(); if (hasNext) next = stack.Peek().Current; }
            catch (Exception error) { audit.errors.Add(error.ToString()); stack.Clear(); break; }
            if (!hasNext) { stack.Pop(); continue; }
            if (next is IEnumerator nested) { stack.Push(nested); continue; }
            yield return next;
        }
        yield return new WaitForEndOfFrame();
        recording = false;
        audit.electrodePositionInvariant = audit.maximumElectrodeDisplacement < 0.001f;
        audit.status = audit.positivePlayerObserved && audit.pauseObserved && audit.returnedToMainMenu
            && audit.electrodePositionInvariant && audit.minimumLeverAngle < -45f && audit.maximumLeverAngle > 45f
            && audit.errors.Count == 0 ? "passed" : "failed";
        audit.finishedUtc = DateTime.UtcNow.ToString("O"); SaveAudit();
        Application.Quit(audit.status == "passed" ? 0 : 1);
    }
    private IEnumerator RunReplay()
    {
        yield return WaitForScene("MainMenu", 15f);
        pendingPhase = "01-main-menu-before"; yield return Hold(2f);
        MainMenuController menu = Object.FindFirstObjectByType<MainMenuController>();
        Require(menu != null, "Saved main menu controller missing.");
        yield return ClickSavedButton(SavedField<Button>(menu, "startButton"), "Start", "MainMenu");
        yield return WaitForScene("Stage_01", 20f); yield return Hold(1f);
        mover = Object.FindFirstObjectByType<global::LeverMover>();
        Require(mover != null && mover.isActiveAndEnabled, "Saved LeverMover is missing or disabled.");
        grab = SavedField<Collider2D>(mover, "grabArea"); pivot = SavedField<Transform>(mover, "pivot");
        foreach (ContactCharger2D source in Object.FindObjectsByType<ContactCharger2D>(FindObjectsSortMode.None))
            sourcePositions[source.GetInstanceID()] = source.transform.position;
        Require(sourcePositions.Count > 0, "No active electrode found.");
        pendingPhase = "02-round-player-neutral"; yield return Hold(2f);
        yield return PressKey(Key.Space);
        ElectricBall2D ball = Object.FindFirstObjectByType<ElectricBall2D>();
        audit.positivePlayerObserved = ball != null && ball.SignedCharge > 0f;
        Require(audit.positivePlayerObserved, "Actual Space edge did not produce positive player charge.");
        pendingPhase = "03-round-player-positive"; yield return Hold(2f);
        Vector3 startGrab = grab.bounds.center;
        yield return HoldMouseWorld(startGrab, true, 0.35f);
        yield return DragToAngle(70f, 2f);
        pendingPhase = "04-lever-left-electrode-fixed";
        yield return HoldAngle(70f, 2f);
        yield return DragToAngle(-70f, 3f);
        pendingPhase = "05-lever-right-electrode-fixed";
        yield return HoldAngle(-70f, 2f);
        QueueMouse(Camera.main.WorldToScreenPoint(pivot.position + Vector3.right), false);
        yield return Hold(1f);
        yield return PressKey(Key.Escape);
        PauseMenuController pause = Object.FindFirstObjectByType<PauseMenuController>();
        audit.pauseObserved = pause != null && pause.IsPaused;
        Require(audit.pauseObserved, "Actual Escape edge did not open saved pause menu.");
        pendingPhase = "06-pause-menu"; yield return Hold(2f);
        yield return ClickSavedButton(SavedField<Button>(pause, "mainMenuButton"), "ReturnToMainMenu", "Stage_01");
        yield return WaitForScene("MainMenu", 20f);
        audit.returnedToMainMenu = true;
        pendingPhase = "07-returned-main-menu"; yield return Hold(3f);
    }
    private IEnumerator WaitForScene(string name, float timeout)
    {
        float deadline = Time.realtimeSinceStartup + timeout;
        while (SceneManager.GetActiveScene().name != name || SceneTransitionController.IsBusy)
        {
            if (Time.realtimeSinceStartup > deadline) throw new InvalidOperationException("Timed out waiting for scene " + name);
            yield return null;
        }
    }
    private IEnumerator Hold(float seconds)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < deadline) { ObserveSources(); yield return null; }
    }
    private IEnumerator PressKey(Key key)
    {
        replayKeyboard.MakeCurrent(); InputSystem.QueueStateEvent(replayKeyboard, new KeyboardState(key));
        yield return Hold(0.25f);
        replayKeyboard.MakeCurrent(); InputSystem.QueueStateEvent(replayKeyboard, new KeyboardState());
        yield return Hold(0.45f);
    }
    private IEnumerator ClickSavedButton(Button button, string route, string oldScene)
    {
        Require(button != null && button.interactable && button.gameObject.activeInHierarchy, "Saved button unavailable: " + route);
        RectTransform rectangle = button.transform as RectTransform;
        Canvas canvas = button.GetComponentInParent<Canvas>().rootCanvas;
        Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        Vector2 screen = RectTransformUtility.WorldToScreenPoint(camera, rectangle.TransformPoint(rectangle.rect.center));
        QueueMouse(screen, false); yield return Hold(0.3f);
        QueueMouse(screen, true); yield return Hold(0.3f);
        QueueMouse(screen, false); yield return Hold(0.7f);
        if (SceneManager.GetActiveScene().name == oldScene && !SceneTransitionController.IsBusy)
        {
            audit.uiRoutes.Add(route + ": runtime pointer replay did not transition; invoked the saved Button.onClick handler as fallback.");
            button.onClick.Invoke();
        }
        else audit.uiRoutes.Add(route + ": actual InputSystemUIInputModule pointer press/release on saved Button.");
    }
    private IEnumerator HoldMouseWorld(Vector3 world, bool pressed, float seconds)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < deadline)
        {
            QueueMouse(Camera.main.WorldToScreenPoint(world), pressed); ObserveSources(); yield return null;
        }
    }
    private IEnumerator DragToAngle(float target, float seconds)
    {
        float begin = Time.realtimeSinceStartup, from = mover.Angle;
        while (Time.realtimeSinceStartup - begin < seconds)
        {
            float angle = Mathf.Lerp(from, target, (Time.realtimeSinceStartup - begin) / seconds);
            QueueMouseAtAngle(angle); ObserveSources(); yield return null;
        }
        QueueMouseAtAngle(target); yield return null;
    }
    private IEnumerator HoldAngle(float angle, float seconds)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < deadline) { QueueMouseAtAngle(angle); ObserveSources(); yield return null; }
    }
    private void QueueMouseAtAngle(float angle)
    {
        float radians = angle * Mathf.Deg2Rad;
        Vector3 point = pivot.position + new Vector3(-Mathf.Sin(radians), Mathf.Cos(radians), 0f) * 1.1f;
        QueueMouse(Camera.main.WorldToScreenPoint(point), true);
    }
    private void QueueMouse(Vector3 screen, bool pressed)
    {
        replayMouse.MakeCurrent();
        var state = new MouseState { position = new Vector2(screen.x, screen.y) };
        if (pressed) state = state.WithButton(MouseButton.Left);
        InputSystem.QueueStateEvent(replayMouse, state);
    }
    private void ObserveSources()
    {
        if (mover != null)
        {
            audit.minimumLeverAngle = Mathf.Min(audit.minimumLeverAngle, mover.Angle);
            audit.maximumLeverAngle = Mathf.Max(audit.maximumLeverAngle, mover.Angle);
        }
        foreach (ContactCharger2D source in Object.FindObjectsByType<ContactCharger2D>(FindObjectsSortMode.None))
            if (sourcePositions.TryGetValue(source.GetInstanceID(), out Vector3 initial))
                audit.maximumElectrodeDisplacement = Mathf.Max(audit.maximumElectrodeDisplacement,
                    Vector3.Distance(initial, source.transform.position));
    }
    private IEnumerator CaptureFrames()
    {
        while (recording)
        {
            yield return new WaitForEndOfFrame();
            if (!recording || Time.realtimeSinceStartup < nextFrame) continue;
            nextFrame = Time.realtimeSinceStartup + 0.1f;
            Texture2D picture = ScreenCapture.CaptureScreenshotAsTexture();
            if (picture == null) { audit.errors.Add("ScreenCapture returned no pixels."); continue; }
            audit.width = picture.width; audit.height = picture.height;
            byte[] png = picture.EncodeToPNG(); Object.Destroy(picture);
            string framePath = Folder + "/Frames/" + audit.capturedFrames.ToString("D5") + ".png";
            File.WriteAllBytes(framePath, png); audit.capturedFrames++; audit.frameTimes.Add(Time.realtimeSinceStartup);
            if (pendingPhase != null)
            {
                string label = pendingPhase; pendingPhase = null;
                string phasePath = Folder + "/" + label + ".png"; File.WriteAllBytes(phasePath, png);
                Sample sample = ObservePhase(label, phasePath); audit.phases.Add(sample);
                File.WriteAllText(Folder + "/" + label + ".json", JsonUtility.ToJson(sample, true)); SaveAudit();
            }
        }
    }
    private Sample ObservePhase(string label, string image)
    {
        var sample = new Sample { phase = label, image = image, timestampUtc = DateTime.UtcNow.ToString("O"),
            scene = SceneManager.GetActiveScene().path, frame = Time.frameCount, realtime = Time.realtimeSinceStartup,
            timeScale = Time.timeScale, transitionBusy = SceneTransitionController.IsBusy };
        ElectricBall2D ball = Object.FindFirstObjectByType<ElectricBall2D>();
        if (ball != null)
        {
            sample.playerPosition = ball.transform.position; sample.signedPlayerCharge = ball.SignedCharge;
            foreach (SpriteRenderer renderer in ball.GetComponentsInChildren<SpriteRenderer>())
                if (renderer.enabled && renderer.sprite != null)
                    sample.sprites.Add(new BallSprite { name = renderer.name, sprite = renderer.sprite.name,
                        spritePixels = renderer.sprite.rect.size, scale = renderer.transform.lossyScale,
                        boundsSize = renderer.bounds.size, color = renderer.color });
        }
        global::LeverMover activeMover = Object.FindFirstObjectByType<global::LeverMover>();
        if (activeMover != null) { sample.leverAngle = activeMover.Angle; sample.leverValue = activeMover.Value; }
        PauseMenuController pause = Object.FindFirstObjectByType<PauseMenuController>(); sample.paused = pause != null && pause.IsPaused;
        foreach (ContactCharger2D source in Object.FindObjectsByType<ContactCharger2D>(FindObjectsSortMode.None))
            sample.electrodes.Add(new Electrode { name = source.name, transformPosition = source.transform.position,
                rigidbodyPosition = source.GetComponent<Rigidbody2D>().position, charge = source.SignedCharge });
        return sample;
    }
    private static T SavedField<T>(object owner, string field) where T : class
    {
        T value = owner.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(owner) as T;
        Require(value != null, "Saved reference missing: " + field); return value;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private void SaveAudit() => File.WriteAllText(Folder + "/ReplayAudit.json", JsonUtility.ToJson(audit, true));
    private void OnLog(string message, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) audit.errors.Add(message + "\n" + stack);
    }
}
