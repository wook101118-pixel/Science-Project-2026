// Clone-only, opt-in screenshot recorder. It adds no game UI or gameplay behavior.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using sung_wook;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

public sealed class LiveDemoCapture : MonoBehaviour
{
    private const string Folder = "C:/Users/wook1/GitBub-Unity/Science-Project-2026/Logs/LiveDemo";
    [Serializable] private sealed class Capture
    {
        public string timestampUtc, scene, image, json;
        public int sequence, width, height;
        public float timeScale, signedCharge;
        public bool hasPlayer, isPaused, isResultOpen, isSettingsOpen, isPracticeOpen, transitionBusy;
        public Vector3 playerPosition;
        public Color hexBackgroundColor;
        public List<string> errors;
    }
    private readonly List<string> errors = new List<string>();
    [Serializable] private sealed class DeviceSnapshot
    {
        public string name, layout;
        public int id;
        public bool enabled, added;
    }
    [Serializable] private sealed class KeySnapshot
    {
        public string key;
        public bool pressed, pressedThisFrame, releasedThisFrame;
        public int observedPressFrames, observedReleaseFrames;
        public float lastEventValue;
    }
    [Serializable] private sealed class ComponentSnapshot
    {
        public string name, type;
        public bool enabled, activeInHierarchy;
        public Vector3 position;
        public Vector2 moveVector;
        public float leverAngle, leverValue;
    }
    [Serializable] private sealed class InputDiagnostics
    {
        public string timestampUtc, scene, updateMode, lastEventType, lastKeyboardEventType, lastMouseEventType;
        public bool applicationFocused, keyboardPresent, mousePresent;
        public float realtime, timeScale;
        public int frame, updateSamples, eventCount, keyboardEventCount, mouseEventCount;
        public int mousePressFrames, mouseReleaseFrames;
        public double lastEventTime, lastKeyboardEventTime, lastMouseEventTime;
        public DeviceSnapshot keyboard, mouse;
        public Vector2 mousePosition;
        public bool mouseLeftPressed, mouseLeftPressedThisFrame, mouseLeftReleasedThisFrame;
        public List<KeySnapshot> keys = new List<KeySnapshot>();
        public List<ComponentSnapshot> components = new List<ComponentSnapshot>();
        public List<string> errors;
    }
    private readonly Dictionary<Key, int> pressFrames = new Dictionary<Key, int>();
    private readonly Dictionary<Key, int> releaseFrames = new Dictionary<Key, int>();
    private readonly Dictionary<Key, float> eventValues = new Dictionary<Key, float>();
    private readonly Key[] observedKeys = { Key.Space, Key.Escape, Key.F8, Key.A, Key.D, Key.LeftArrow, Key.RightArrow };
    private int updateSamples, eventCount, keyboardEventCount, mouseEventCount, mousePressFrames, mouseReleaseFrames;
    private string lastEventType = "", lastKeyboardEventType = "", lastMouseEventType = "";
    private double lastEventTime, lastKeyboardEventTime, lastMouseEventTime;
    private float nextDiagnostics, nextAutomaticCapture;
    private int sequence;
    private bool capturing;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (Application.isEditor || Array.IndexOf(Environment.GetCommandLineArgs(), "--live-demo") < 0) return;
        var host = new GameObject("Opt-in Live Demo Capture"); Object.DontDestroyOnLoad(host); host.AddComponent<LiveDemoCapture>();
    }
    private void Awake()
    {
        Directory.CreateDirectory(Folder);
        Application.logMessageReceived += OnLog;
        InputSystem.onEvent += ObserveEvent;
        nextAutomaticCapture = Time.realtimeSinceStartup + 3f;
        WriteDiagnostics();
    }
    private void OnDestroy()
    {
        Application.logMessageReceived -= OnLog;
        InputSystem.onEvent -= ObserveEvent;
    }
    private void Update()
    {
        updateSamples++;
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
            foreach (Key key in observedKeys)
            {
                KeyControl control = keyboard[key];
                if (control.wasPressedThisFrame) pressFrames[key] = Count(pressFrames, key) + 1;
                if (control.wasReleasedThisFrame) releaseFrames[key] = Count(releaseFrames, key) + 1;
            }
        Mouse mouse = Mouse.current;
        if (mouse != null)
        {
            if (mouse.leftButton.wasPressedThisFrame) mousePressFrames++;
            if (mouse.leftButton.wasReleasedThisFrame) mouseReleaseFrames++;
        }
        if (Time.realtimeSinceStartup >= nextDiagnostics)
        {
            nextDiagnostics = Time.realtimeSinceStartup + 1f;
            WriteDiagnostics();
        }
        bool captureKey = keyboard != null && keyboard.f8Key.wasPressedThisFrame;
        if (!capturing && (captureKey || Time.realtimeSinceStartup >= nextAutomaticCapture))
        {
            nextAutomaticCapture = Time.realtimeSinceStartup + 3f;
            StartCoroutine(SaveCapture());
        }
    }
    private static int Count(Dictionary<Key, int> values, Key key) => values.TryGetValue(key, out int count) ? count : 0;
    private void ObserveEvent(InputEventPtr inputEvent, InputDevice device)
    {
        eventCount++;
        lastEventType = inputEvent.type.ToString();
        lastEventTime = inputEvent.time;
        if (device is Keyboard keyboard)
        {
            keyboardEventCount++;
            lastKeyboardEventType = lastEventType;
            lastKeyboardEventTime = lastEventTime;
            if (inputEvent.IsA<StateEvent>() || inputEvent.IsA<DeltaStateEvent>())
                foreach (Key key in observedKeys)
                    if (keyboard[key].ReadValueFromEvent(inputEvent, out float value)) eventValues[key] = value;
        }
        if (device is Mouse)
        {
            mouseEventCount++;
            lastMouseEventType = lastEventType;
            lastMouseEventTime = lastEventTime;
        }
    }
    private static DeviceSnapshot SnapshotDevice(InputDevice device) => device == null ? null : new DeviceSnapshot
    { name = device.name, layout = device.layout, id = device.deviceId, enabled = device.enabled, added = device.added };
    private void WriteDiagnostics()
    {
        var record = new InputDiagnostics {
            timestampUtc = DateTime.UtcNow.ToString("O"), scene = SceneManager.GetActiveScene().path,
            applicationFocused = Application.isFocused, updateMode = InputSystem.settings.updateMode.ToString(),
            realtime = Time.realtimeSinceStartup, timeScale = Time.timeScale, frame = Time.frameCount,
            updateSamples = updateSamples, keyboardPresent = Keyboard.current != null, mousePresent = Mouse.current != null,
            keyboard = SnapshotDevice(Keyboard.current), mouse = SnapshotDevice(Mouse.current),
            eventCount = eventCount, keyboardEventCount = keyboardEventCount, mouseEventCount = mouseEventCount,
            lastEventType = lastEventType, lastKeyboardEventType = lastKeyboardEventType, lastMouseEventType = lastMouseEventType,
            lastEventTime = lastEventTime, lastKeyboardEventTime = lastKeyboardEventTime, lastMouseEventTime = lastMouseEventTime,
            mousePressFrames = mousePressFrames, mouseReleaseFrames = mouseReleaseFrames, errors = new List<string>(errors)
        };
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
            foreach (Key key in observedKeys)
            {
                KeyControl control = keyboard[key];
                record.keys.Add(new KeySnapshot {
                    key = key.ToString(), pressed = control.isPressed,
                    pressedThisFrame = control.wasPressedThisFrame, releasedThisFrame = control.wasReleasedThisFrame,
                    observedPressFrames = Count(pressFrames, key), observedReleaseFrames = Count(releaseFrames, key),
                    lastEventValue = eventValues.TryGetValue(key, out float value) ? value : -1f
                });
            }
        Mouse mouse = Mouse.current;
        if (mouse != null)
        {
            record.mousePosition = mouse.position.ReadValue(); record.mouseLeftPressed = mouse.leftButton.isPressed;
            record.mouseLeftPressedThisFrame = mouse.leftButton.wasPressedThisFrame;
            record.mouseLeftReleasedThisFrame = mouse.leftButton.wasReleasedThisFrame;
        }
        foreach (PlayerController component in Object.FindObjectsByType<PlayerController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            record.components.Add(SnapshotComponent(component));
        foreach (PlayerInputReader component in Object.FindObjectsByType<PlayerInputReader>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            ComponentSnapshot item = SnapshotComponent(component); item.moveVector = component.MoveVector; record.components.Add(item);
        }
        foreach (global::LeverMover component in Object.FindObjectsByType<global::LeverMover>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            ComponentSnapshot item = SnapshotComponent(component); item.leverAngle = component.Angle; item.leverValue = component.Value; record.components.Add(item);
        }
        File.WriteAllText(Path.Combine(Folder, "InputDiagnostics.json"), JsonUtility.ToJson(record, true));
    }
    private static ComponentSnapshot SnapshotComponent(MonoBehaviour component) => new ComponentSnapshot {
        name = component.name, type = component.GetType().Name, enabled = component.enabled,
        activeInHierarchy = component.gameObject.activeInHierarchy, position = component.transform.position
    };
    private IEnumerator SaveCapture()
    {
        capturing = true; yield return new WaitForEndOfFrame();
        try
        {
            sequence++;
            string label = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + sequence.ToString("00") + "-" + SceneManager.GetActiveScene().name;
            var record = new Capture {
                timestampUtc = DateTime.UtcNow.ToString("O"), sequence = sequence,
                scene = SceneManager.GetActiveScene().path, width = Screen.width, height = Screen.height,
                image = Path.Combine(Folder, label + ".png"), json = Path.Combine(Folder, label + ".json"),
                timeScale = Time.timeScale, transitionBusy = SceneTransitionController.IsBusy, errors = new List<string>(errors)
            };
            ElectricBall2D ball = Object.FindFirstObjectByType<ElectricBall2D>();
            if (ball != null) { record.hasPlayer = true; record.playerPosition = ball.transform.position; record.signedCharge = ball.SignedCharge; }
            PauseMenuController pause = Object.FindFirstObjectByType<PauseMenuController>(); record.isPaused = pause != null && pause.IsPaused;
            ElectricGameCompleteView result = Object.FindFirstObjectByType<ElectricGameCompleteView>(); record.isResultOpen = result != null && result.IsResultOpen;
            MainMenuController menu = Object.FindFirstObjectByType<MainMenuController>();
            if (menu != null) { record.isSettingsOpen = menu.IsSettingsOpen; record.isPracticeOpen = menu.IsPracticeOpen; }
            foreach (Tilemap map in Object.FindObjectsByType<Tilemap>(FindObjectsSortMode.None)) if (map.name == "Background") record.hexBackgroundColor = map.color;
            Texture2D picture = ScreenCapture.CaptureScreenshotAsTexture();
            if (picture == null) throw new InvalidOperationException("Live demo ScreenCapture returned no pixels.");
            File.WriteAllBytes(record.image, picture.EncodeToPNG()); Object.Destroy(picture);
            string json = JsonUtility.ToJson(record, true); File.WriteAllText(record.json, json); File.WriteAllText(Path.Combine(Folder, "Latest.json"), json);
        }
        finally { capturing = false; }
    }
    private void OnLog(string message, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message + "\n" + stack);
    }
}
