// Temporary standalone QA fixture. Copy only into the isolated clone's Assets.
// It is inactive unless --qa-beginner-controls is present; it never changes production assets or preferences.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using sung_wook;
using Object = UnityEngine.Object;

public sealed class QaBeginnerControlProbe : MonoBehaviour
{
    [Serializable] private sealed class Check { public string name; public bool passed; public string details; }
    [Serializable] private sealed class Report
    {
        public string status = "running";
        public string startedUtc;
        public string scope = "Windows standalone fixture QA using the saved Stage_02 player, controller, charge visuals and ContactChargingStation. The participants are relocated away from the map. Temporary invisible colliders isolate floor/wall/ceiling contacts. Movement APIs and actual InputSystem keyboard edges are exercised. These checks are not a manual playthrough or a route-clear proof. No production assets or preference keys are changed.";
        public List<Check> checks = new List<Check>();
        public List<string> errors = new List<string>();
        public List<string> measurements = new List<string>();
    }

    private Report report;
    private string reportPath;
    private float started;
    private bool ending;
    private ElectricBall2D ball;
    private PlayerMovement movement;
    private PlayerController controller;
    private PlayerInputReader input;
    private ContactCharger2D charger;
    private ElectricStageSelector selector;
    private StageLever2D lever;
    private Keyboard keyboard;
    private Mouse mouse;
    private Mouse previousMouse;
    private InputSettings.UpdateMode previousUpdateMode;
    private InputSettings.BackgroundBehavior previousBackgroundBehavior;
    private Vector2 stationPose = new Vector2(1000f, 1000f);
    private readonly List<string> runtimeErrors = new List<string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (Application.isEditor || Array.IndexOf(Environment.GetCommandLineArgs(), "--qa-beginner-controls") < 0) return;
        var root = new GameObject("Temporary Control QA Probe");
        DontDestroyOnLoad(root);
        var probe = root.AddComponent<QaBeginnerControlProbe>();
        probe.Initialize();
    }

    private void Initialize()
    {
        Application.runInBackground = true;
        started = Time.realtimeSinceStartup;
        foreach (string argument in Environment.GetCommandLineArgs())
            if (argument.StartsWith("--qa-report=", StringComparison.Ordinal)) reportPath = argument.Substring(12);
        report = new Report { startedUtc = DateTime.UtcNow.ToString("O") };
        Application.logMessageReceived += OnLog;
        try
        {
            Require(!string.IsNullOrEmpty(reportPath) && Path.IsPathRooted(reportPath), "An absolute --qa-report path is required.");
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
            Flush();
            StartCoroutine(Guarded(Run()));
        }
        catch (Exception exception) { Fail(exception); }
    }

    private IEnumerator Run()
    {
        float deadline = Time.realtimeSinceStartup + 15f;
        while ((!SceneTransitionController.IsAvailable || SceneTransitionController.IsBusy) && Time.realtimeSinceStartup < deadline) yield return null;
        AsyncOperation loading = SceneManager.LoadSceneAsync("Stage_02", LoadSceneMode.Single);
        Require(loading != null, "Saved Stage_02 must be included in the player build.");
        while (!loading.isDone) yield return null;
        yield return null;
        yield return null;
        selector = Object.FindFirstObjectByType<ElectricStageSelector>();
        Require(selector != null && selector.Ball != null && selector.Lever != null, "Saved scene participants are missing.");
        ball = selector.Ball;
        movement = ball.GetComponent<PlayerMovement>();
        controller = ball.GetComponent<PlayerController>();
        input = ball.GetComponent<PlayerInputReader>();
        lever = selector.Lever;
        charger = selector.GetStageComponent<ContactCharger2D>(2);
        Require(movement != null && controller != null && input != null && charger != null, "Saved player stack/station is missing.");
        CheckResult("one-player-and-one-movement-owner", Object.FindObjectsByType<ElectricBall2D>(FindObjectsSortMode.None).Length == 1
            && ball.GetComponents<PlayerMovement>().Length == 1 && ball.GetComponents<PlayerController>().Length == 1,
            "The active saved scene owns a single player/controller/movement stack.");
        CheckResult("station-shares-current-ball-and-lever", Field<ElectricBall2D>(charger, "ball") == ball && Field<StageLever2D>(charger, "lever") == lever,
            "Bootstrap references resolve to the current saved player and lever.");
        previousUpdateMode = InputSystem.settings.updateMode;
        previousBackgroundBehavior = InputSystem.settings.backgroundBehavior;
        InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        yield return MouseLeverInput();
        controller.enabled = false; // isolate each test command from ordinary Update; invoke the actual method explicitly for keyboard tests.
        Time.timeScale = 1f;
        ball.Body.gravityScale = 0f;
        ball.Body.constraints = RigidbodyConstraints2D.FreezeRotation;
        SetField(charger, "moveSpeed", 0f);
        Rigidbody2D station = charger.GetComponent<Rigidbody2D>();
        station.position = stationPose;
        charger.enabled = false;
        Pose(new Vector2(1000f, 1010f));
        keyboard = InputSystem.AddDevice<Keyboard>("QA control keyboard");
        keyboard.MakeCurrent();
        Release();
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.D));
        InputSystem.Update();
        Method(controller, "Update");
        float horizontalOnly = Field<float>(movement, "moveX");
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.D, Key.W));
        InputSystem.Update();
        Method(controller, "Update");
        float horizontalWithUp = Field<float>(movement, "moveX");
        CheckResult("unused-vertical-key-does-not-reduce-horizontal-input", Near(horizontalOnly, horizontalWithUp),
            "D alone horizontal=" + horizontalOnly + "; D+W horizontal=" + horizontalWithUp + ". Vertical movement is not consumed by PlayerController.");
        report.measurements.Add("Actual InputSystem Move vector D horizontal=" + horizontalOnly + "; D+W horizontal=" + horizontalWithUp + "; corresponding horizontal target speeds=" + (horizontalOnly * 7f) + " / " + (horizontalWithUp * 7f) + " units/s.");
        Release();
        yield return InputAxisRegression();
        yield return SpaceAndForce();
        yield return ContactSynchronization();
        yield return MovementAndContacts();
        Release();
        InputSystem.RemoveDevice(keyboard);
        keyboard = null;
        InputSystem.settings.updateMode = previousUpdateMode;
        InputSystem.settings.backgroundBehavior = previousBackgroundBehavior;
        CheckResult("no-runtime-error-or-exception", runtimeErrors.Count == 0, string.Join("\n", runtimeErrors));
        Complete();
    }

    private IEnumerator InputAxisRegression()
    {
        // Exercise the saved reader's generated Controls and the real controller input handoff.
        KeyboardVectorCase("d-w", new Vector2(1f, 1f), Key.D, Key.W);
        KeyboardVectorCase("d-s", new Vector2(1f, -1f), Key.D, Key.S);
        KeyboardVectorCase("a-w", new Vector2(-1f, 1f), Key.A, Key.W);
        KeyboardVectorCase("a-s", new Vector2(-1f, -1f), Key.A, Key.S);
        KeyboardVectorCase("right-up", new Vector2(1f, 1f), Key.RightArrow, Key.UpArrow);
        KeyboardVectorCase("right-down", new Vector2(1f, -1f), Key.RightArrow, Key.DownArrow);
        KeyboardVectorCase("left-up", new Vector2(-1f, 1f), Key.LeftArrow, Key.UpArrow);
        KeyboardVectorCase("left-down", new Vector2(-1f, -1f), Key.LeftArrow, Key.DownArrow);
        KeyboardVectorCase("wasd-horizontal-opposites", Vector2.zero, Key.A, Key.D);
        KeyboardVectorCase("wasd-vertical-opposites", Vector2.zero, Key.W, Key.S);
        KeyboardVectorCase("wasd-all-opposites", Vector2.zero, Key.A, Key.D, Key.W, Key.S);
        KeyboardVectorCase("arrows-horizontal-opposites", Vector2.zero, Key.LeftArrow, Key.RightArrow);
        KeyboardVectorCase("arrows-vertical-opposites", Vector2.zero, Key.UpArrow, Key.DownArrow);
        KeyboardVectorCase("arrows-all-opposites", Vector2.zero, Key.LeftArrow, Key.RightArrow, Key.UpArrow, Key.DownArrow);
        KeyboardVectorCase("mixed-horizontal-opposites", Vector2.zero, Key.A, Key.RightArrow);
        KeyboardVectorCase("vertical-only", Vector2.up, Key.W);
        Release();
        Gamepad previousGamepad = Gamepad.current;
        Gamepad gamepad = InputSystem.AddDevice<Gamepad>("QA analog control gamepad");
        gamepad.MakeCurrent();
        try
        {
            AnalogVectorCase(gamepad, "quarter-horizontal", new Vector2(0.25f, 0f));
            AnalogVectorCase(gamepad, "half-horizontal", new Vector2(0.5f, 0f));
            AnalogVectorCase(gamepad, "quarter-diagonal", new Vector2(0.25f, 0.25f));
            AnalogVectorCase(gamepad, "half-diagonal", new Vector2(0.5f, 0.5f));
            AnalogVectorCase(gamepad, "unequal-negative-diagonal", new Vector2(-0.5f, 0.25f));
            AnalogVectorCase(gamepad, "stick-release", Vector2.zero);
        }
        finally
        {
            InputSystem.RemoveDevice(gamepad);
            if (previousGamepad != null && previousGamepad.added) previousGamepad.MakeCurrent();
        }
        Release();
        yield break;
    }

    private void KeyboardVectorCase(string label, Vector2 expected, params Key[] keys)
    {
        Release();
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
        InputSystem.Update();
        Method(controller, "Update");
        Vector2 actual = input.MoveVector;
        float horizontal = Field<float>(movement, "moveX");
        CheckResult("keyboard-" + label + "-keeps-independent-axes", (actual - expected).sqrMagnitude < 0.000001f && Near(horizontal, expected.x),
            "Actual InputSystem event -> saved PlayerInputReader generated Controls -> PlayerController: expected=" + expected + "; MoveVector=" + actual + "; horizontal=" + horizontal + ".");
    }

    private void AnalogVectorCase(Gamepad gamepad, string label, Vector2 raw)
    {
        InputSystem.QueueStateEvent(gamepad, new GamepadState { leftStick = raw });
        InputSystem.Update();
        Method(controller, "Update");
        // The existing direct leftStick binding uses Unity's radial stick deadzone. Compute
        // the expected proportional vector independently; do not normalize it to keyboard gain.
        float magnitude = raw.magnitude;
        float strength = Mathf.InverseLerp(InputSystem.settings.defaultDeadzoneMin, InputSystem.settings.defaultDeadzoneMax, magnitude);
        Vector2 expected = magnitude > 0f ? raw / magnitude * strength : Vector2.zero;
        Vector2 actual = input.MoveVector;
        float horizontal = Field<float>(movement, "moveX");
        CheckResult("gamepad-" + label + "-preserves-analog-strength", (actual - expected).sqrMagnitude < 0.000001f && Near(horizontal, expected.x),
            "Raw stick=" + raw + "; expected existing radial-deadzone vector=" + expected + "; MoveVector=" + actual + "; horizontal=" + horizontal + ".");
    }

    private IEnumerator MouseLeverInput()
    {
        // Use the saved collider, camera, pivot, reader and actual mouse-input code. No angle/value setter is used.
        lever.UseLeverInput();
        global::LeverMover mover = Field<global::LeverMover>(lever.Reader, "lever");
        Transform pivot = Field<Transform>(mover, "pivot");
        Collider2D grabArea = Field<Collider2D>(mover, "grabArea");
        Camera camera = Field<Camera>(mover, "cam");
        Require(mover != null && pivot != null && grabArea != null && camera != null, "Saved physical mouse lever references are missing.");
        previousMouse = Mouse.current;
        mouse = InputSystem.AddDevice<Mouse>("QA lever mouse");
        mouse.MakeCurrent();
        MouseAt(camera, grabArea.bounds.center, false);
        MouseAt(camera, grabArea.bounds.center, true);
        MouseAt(camera, pivot.position + Vector3.left * 2f, true);
        CheckResult("actual-mouse-grab-and-left-drag-reaches-positive-charge", Near(lever.SignedValue, 1f, 0.001f),
            "Virtual mouse press hits the saved grab collider; actual LeverMover.Update / LeverReader yields signed value=" + lever.SignedValue);
        MouseAt(camera, pivot.position + Vector3.right * 2f, true);
        CheckResult("actual-mouse-right-drag-reaches-negative-charge", Near(lever.SignedValue, -1f, 0.001f),
            "Actual drag yields signed value=" + lever.SignedValue);
        MouseAt(camera, pivot.position + Vector3.up * 2f, true);
        CheckResult("actual-mouse-upper-drag-centers-neutral-charge", Near(lever.SignedValue, 0f, 0.001f),
            "Actual drag above the pivot yields signed value=" + lever.SignedValue);
        MouseAt(camera, pivot.position + Vector3.left * 2f, true);
        MouseAt(camera, pivot.position + Vector3.right * 20f, false);
        MouseAt(camera, pivot.position + Vector3.up * 2f, false);
        CheckResult("actual-mouse-release-outside-stops-dragging", Near(lever.SignedValue, 1f, 0.001f) && !Field<bool>(mover, "dragging"),
            "Release outside the collider and subsequent movement preserve the released +1 position.");
        MouseAt(camera, grabArea.bounds.center, true);
        MouseAt(camera, pivot.position + Vector3.left * 2f, true);
        mover.enabled = false;
        mover.enabled = true;
        InputSystem.Update(); // advance the held input edge before the resume-like lifecycle check.
        MouseAt(camera, pivot.position + Vector3.right * 2f, true);
        CheckResult("lever-disable-enable-clears-held-drag-until-new-press", Near(lever.SignedValue, 1f, 0.001f) && !Field<bool>(mover, "dragging"),
            "An isolated pause-like mover disable/enable clears dragging; a continuously held button does not resume a stale drag.");
        MouseAt(camera, pivot.position + Vector3.up * 2f, false);
        InputSystem.RemoveDevice(mouse);
        mouse = null;
        if (previousMouse != null && previousMouse.added) previousMouse.MakeCurrent();
        yield break;
    }

    private void MouseAt(Camera camera, Vector3 world, bool pressed)
    {
        Vector3 screen = camera.WorldToScreenPoint(world);
        InputSystem.QueueStateEvent(mouse, new MouseState { position = new Vector2(screen.x, screen.y), buttons = (ushort)(pressed ? 1 : 0) });
        InputSystem.Update();
        Method(Field<global::LeverMover>(lever.Reader, "lever"), "Update");
    }

    private IEnumerator SpaceAndForce()
    {
        lever.SetSignedValue(1f);
        charger.enabled = true;
        Pose(stationPose + Vector2.right * 3f);
        ball.Body.constraints = RigidbodyConstraints2D.FreezePositionY | RigidbodyConstraints2D.FreezeRotation;
        ball.SetChargeMagnitude(0f);
        ball.SetNeutral();
        ChangePlayerVisual visual = ball.GetComponent<ChangePlayerVisual>();
        SpriteRenderer overlay = Field<SpriteRenderer>(visual, "chargeRenderer");
        Sprite positive = Field<Sprite>(visual, "positiveSprite"), negative = Field<Sprite>(visual, "negativeSprite");
        Require(visual != null && overlay != null && positive != null && negative != null, "Saved Plus/Minus visual references are missing.");
        int spriteCount = ball.GetComponentsInChildren<SpriteRenderer>(true).Length;
        PressSpace(PlayerStateType.Positive, 1f, "positive");
        Sample("positive-3m", 1f, 3f);
        Pose(stationPose + Vector2.right);
        Sample("positive-1m", 1f, 1f);
        Pose(stationPose + Vector2.right * 3f);
        yield return Seconds(0.28f);
        CheckResult("positive-texture-and-repulsive-physics", overlay.enabled && overlay.sprite == positive && overlay.color.a > 0.95f && ball.Body.linearVelocity.x > 0f,
            "Plus alpha=" + overlay.color.a + "; vx=" + ball.Body.linearVelocity.x);
        Pose(stationPose + Vector2.right * 3f);
        PressSpace(PlayerStateType.Negative, -1f, "negative");
        Sample("negative-3m", -1f, 3f);
        bool sawIntermediate = false;
        float end = Time.time + 0.55f, visualWatchdog = Time.realtimeSinceStartup + 5f;
        while (Time.time < end)
        {
            Require(Time.realtimeSinceStartup < visualWatchdog, "Negative transition exceeded watchdog.");
            if (overlay.color.a > 0.01f && overlay.color.a < 0.99f) sawIntermediate = true;
            yield return null;
        }
        CheckResult("negative-crosses-neutral-fades-and-attracts", sawIntermediate && overlay.enabled && overlay.sprite == negative && overlay.color.a > 0.95f && ball.Body.linearVelocity.x < 0f,
            "Intermediate fade=" + sawIntermediate + "; Minus alpha=" + overlay.color.a + "; vx=" + ball.Body.linearVelocity.x);
        Pose(stationPose + Vector2.right * 3f);
        PressSpace(PlayerStateType.Neutral, 0f, "neutral");
        Sample("neutral-3m", 0f, 3f);
        yield return Seconds(0.28f);
        CheckResult("neutral-removes-overlay-and-electric-acceleration", !overlay.enabled && Mathf.Abs(ball.Body.linearVelocity.x) < 0.0001f,
            "Overlay enabled=" + overlay.enabled + "; vx=" + ball.Body.linearVelocity.x);
        CheckResult("no-runtime-player-renderers-created", ball.GetComponentsInChildren<SpriteRenderer>(true).Length == spriteCount,
            "The saved sprite renderer count remains " + spriteCount + ".");
        charger.enabled = false;
    }

    private IEnumerator ContactSynchronization()
    {
        Rigidbody2D station = charger.GetComponent<Rigidbody2D>();
        station.position = stationPose;
        ball.Body.constraints = RigidbodyConstraints2D.FreezeAll;
        ball.Body.gravityScale = 0f;
        charger.enabled = true;
        lever.SetSignedValue(0f);
        ball.SetChargeMagnitude(0f);
        ball.SetNeutral();
        Physics2D.SyncTransforms();
        Collider2D electrode = charger.GetComponent<Collider2D>();
        Collider2D playerCollider = ball.GetComponent<Collider2D>();
        Require(electrode != null && playerCollider != null, "Saved contact colliders are required.");
        Pose(new Vector2(electrode.bounds.max.x + playerCollider.bounds.extents.x - 0.04f, electrode.bounds.center.y));
        int events = 0;
        Action<ElectricBall2D> charged = _ => events++;
        charger.ChargedFromNeutral += charged;
        lever.SetSignedValue(0.5f);
        yield return FixedFrames(5);
        CheckResult("real-contact-partial-positive-charge", Near(ball.SignedCharge, 0.5f) && events == 1,
            "Saved colliders overlap in an isolated fixture. Signed charge=" + ball.SignedCharge + "; neutral charge events=" + events);
        lever.SetSignedValue(-1f);
        yield return FixedFrames(5);
        CheckResult("sustained-contact-tracks-opposite-source", Near(ball.SignedCharge, -1f) && events == 1,
            "OnCollisionStay tracks lever change without a second neutral-to-charged event. q=" + ball.SignedCharge + "; events=" + events);
        lever.SetSignedValue(0f);
        yield return FixedFrames(5);
        CheckResult("sustained-contact-neutralizes-at-center", ball.CurrentPolarity == Polarity.Neutral && Near(ball.SignedCharge, 0f) && Near(ball.ChargeMagnitude, 0f),
            "Centered source sets both magnitude and polarity to neutral.");
        lever.SetSignedValue(-0.25f);
        yield return FixedFrames(5);
        CheckResult("sustained-contact-recharges-once-from-neutral", Near(ball.SignedCharge, -0.25f) && events == 2,
            "Signed charge=" + ball.SignedCharge + "; neutral-to-charge events=" + events);
        Pose(stationPose + Vector2.right * 5f);
        yield return FixedFrames(3);
        lever.SetSignedValue(1f);
        yield return FixedFrames(3);
        CheckResult("separated-ball-retains-charge-after-lever-change", Near(ball.SignedCharge, -0.25f),
            "Changing the lever after real collision separation does not rewrite the free ball charge.");
        charger.ChargedFromNeutral -= charged;
        charger.enabled = false;
        ball.Body.constraints = RigidbodyConstraints2D.FreezeRotation;
    }

    private IEnumerator MovementAndContacts()
    {
        Pose(new Vector2(1000f, 1020f));
        ball.Body.gravityScale = 0f;
        movement.SetMovex(1f);
        yield return FixedFrames(85);
        float top = ball.Body.linearVelocity.x;
        CheckResult("horizontal-accelerates-to-saved-cap", Near(top, 7f, 0.02f), "Saved maxMoveSpeed=7; actual vx=" + top);
        Vector2 released = ball.Body.position;
        movement.SetMovex(0f);
        float stopStarted = Time.fixedTime;
        int stopFrames = 0;
        while (Mathf.Abs(ball.Body.linearVelocity.x) > 0.001f && stopFrames++ < 150) yield return new WaitForFixedUpdate();
        float stopSeconds = Time.fixedTime - stopStarted;
        float stopDistance = ball.Body.position.x - released.x;
        CheckResult("key-release-brakes-to-rest", Mathf.Abs(ball.Body.linearVelocity.x) < 0.001f,
            "After " + stopSeconds + " simulated seconds without electric force: vx=" + ball.Body.linearVelocity.x + ".");
        report.measurements.Add("Horizontal top speed=" + top + " units/s; acceleration=" + Field<float>(movement, "acceleration") + " units/s²; neutral braking=" + Field<float>(movement, "deceleration") + " units/s²; observed stopping distance=" + stopDistance + " units in " + stopSeconds + " s.");
        Pose(new Vector2(1000f, 1020f));
        movement.SetMovex(1f);
        yield return FixedFrames(80);
        movement.SetMovex(-1f);
        float reverseStarted = Time.fixedTime;
        float zeroCrossing = -1f;
        int reverseFrames = 0;
        while (ball.Body.linearVelocity.x > -6.999f && reverseFrames++ < 160)
        {
            yield return new WaitForFixedUpdate();
            if (zeroCrossing < 0f && ball.Body.linearVelocity.x <= 0f) zeroCrossing = Time.fixedTime - reverseStarted;
        }
        CheckResult("direction-reversal-eventually-reaches-negative-cap", ball.Body.linearVelocity.x < -6.99f,
            "Full-speed opposite input crosses zero after " + zeroCrossing + "s and reaches -7 after " + (Time.fixedTime - reverseStarted) + "s.");
        report.measurements.Add("A full-speed reversal from +7 to -7 units/s crosses zero in " + zeroCrossing + "s and completes in " + (Time.fixedTime - reverseStarted) + "s.");
        Pose(new Vector2(1000f, 1020f));
        Require(ball.LaunchImpulse(new Vector2(12f, 0f) * ball.Body.mass), "Fixture launch failed.");
        yield return FixedFrames(30);
        CheckResult("launch-preserves-external-momentum-without-input", Near(ball.Body.linearVelocity.x, 12f, 0.02f),
            "No input after launch preserves vx=" + ball.Body.linearVelocity.x + ".");
        movement.SetMovex(-1f);
        yield return FixedFrames(30);
        CheckResult("opposite-direction-can-brake-launch-momentum", ball.Body.linearVelocity.x < 10f && ball.Body.linearVelocity.x > 0f,
            "0.6 s opposite input brakes a 12 units/s launch to vx=" + ball.Body.linearVelocity.x + ".");
        movement.ResetMotion();
        CheckResult("reset-motion-clears-velocity-and-input", ball.Body.linearVelocity == Vector2.zero && Near(Field<float>(movement, "moveX"), 0f),
            "ResetMotion clears both velocity components and latched movement input.");
        var floor = new GameObject("Temporary QA Ground Collider");
        floor.tag = "Ground";
        // Keep collision fixtures away from the earlier ContactChargingStation at (1000,1000).
        // Disabling its MonoBehaviour does not disable its still-solid saved Collider2D.
        floor.transform.position = new Vector3(2000f, 1000f, 0f);
        var floorCollider = floor.AddComponent<BoxCollider2D>();
        floorCollider.size = new Vector2(8f, 0.3f);
        Pose(new Vector2(2000f, 1002f));
        ball.Body.gravityScale = 1f;
        ball.Body.WakeUp();
        Physics2D.SyncTransforms();
        bool bounced = false;
        float highestUpward = 0f;
        float lowestPosition = ball.Body.position.y;
        float bounceDeadline = Time.realtimeSinceStartup + 3f;
        while (Time.realtimeSinceStartup < bounceDeadline)
        {
            yield return new WaitForFixedUpdate();
            highestUpward = Mathf.Max(highestUpward, ball.Body.linearVelocity.y);
            lowestPosition = Mathf.Min(lowestPosition, ball.Body.position.y);
            if (highestUpward > 5f) { bounced = true; break; }
        }
        CheckResult("real-ground-collision-auto-bounces", bounced, "Isolated floor at " + floor.transform.position + "; real collision upward speed=" + highestUpward
            + "; minimum player y=" + lowestPosition + "; expected jump impulse/mass=" + (Field<float>(movement, "jumpForce") / ball.Body.mass)
            + "; body constraints=" + ball.Body.constraints + "; simulated=" + ball.Body.simulated);
        report.measurements.Add("Ground auto-bounce observed upward velocity=" + highestUpward + " units/s; saved mass=" + ball.Body.mass + " kg; default gravity=" + Physics2D.gravity);
        ball.Body.gravityScale = 0f;
        floorCollider.size = new Vector2(0.3f, 8f);
        Pose(new Vector2(1998f, 1000f));
        ball.Body.linearVelocity = new Vector2(4f, 0f);
        yield return FixedFrames(40);
        CheckResult("ground-tagged-sidewall-does-not-bounce-up", Mathf.Abs(ball.Body.linearVelocity.y) < 0.01f,
            "Side contact has no upward support normal; vy=" + ball.Body.linearVelocity.y);
        floorCollider.size = new Vector2(8f, 0.3f);
        Pose(new Vector2(2000f, 998f));
        ball.Body.linearVelocity = new Vector2(0f, 4f);
        yield return FixedFrames(40);
        CheckResult("ground-tagged-ceiling-does-not-auto-bounce", ball.Body.linearVelocity.y <= 0.1f,
            "Underside contact does not inject upward jump velocity; vy=" + ball.Body.linearVelocity.y);
        Object.Destroy(floor);
        Flush();
    }

    private void PressSpace(PlayerStateType expected, float signed, string name)
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Space));
        InputSystem.Update();
        bool pressEdge = input.StateChangePressed;
        Method(controller, "Update");
        CheckResult("space-" + name + "-changes-real-charge-immediately", pressEdge && ball.Player.CurrentState == expected && Near(ball.SignedCharge, signed),
            "Actual InputSystem edge -> PlayerController.Update -> Player.CycleState. state=" + ball.Player.CurrentState + "; signed charge=" + ball.SignedCharge);
        InputSystem.Update();
        bool noSecondEdge = !input.StateChangePressed;
        Method(controller, "Update");
        CheckResult("held-space-" + name + "-does-not-repeat", noSecondEdge && ball.Player.CurrentState == expected && Near(ball.SignedCharge, signed),
            "Second input step while held does not generate another state-change edge.");
        Release();
    }

    private void Sample(string label, float signed, float distance)
    {
        Require(charger.TryGetSample(out ElectricForceSample sample), "The saved contact source refused a force sample.");
        float expected = (float)(8.9875517862e9 * 0.00003 * 0.00003 / (distance * distance)) * signed;
        CheckResult(label + "-matches-si-coulomb-force", sample.UsesSIUnits && sample.IsInRange && !sample.IsLimited
            && Near(sample.ForceNewtons.x, expected, 0.002f) && Mathf.Abs(sample.ForceNewtons.y) < 0.001f,
            "r=" + sample.DistanceMeters + "m; q=" + sample.SignedChargeCoulombs + "C; F=" + sample.ForceNewtons + "N; expected x=" + expected + "N.");
        Vector2 before = ball.Body.linearVelocity;
        float beforeCharge = ball.SignedCharge;
        charger.TryGetSample(out ElectricForceSample repeated);
        CheckResult(label + "-measurement-is-read-only", before == ball.Body.linearVelocity && beforeCharge == ball.SignedCharge && repeated.Force == sample.Force,
            "Repeated provider reads do not apply force, charge the player or mutate the sample.");
    }

    private void Pose(Vector2 position)
    {
        movement.ResetMotion();
        ball.Body.position = position;
        Physics2D.SyncTransforms();
    }
    private void Release() { if (keyboard == null) return; InputSystem.QueueStateEvent(keyboard, new KeyboardState()); InputSystem.Update(); }
    private IEnumerator FixedFrames(int count) { for (int i = 0; i < count; i++) yield return new WaitForFixedUpdate(); }
    private IEnumerator Seconds(float duration)
    {
        // Production sprite interpolation uses scaled deltaTime, so its fixture waits on the same clock.
        // A stalled render frame must not consume the whole fade assertion's wall-time allowance.
        float end = Time.time + duration, watchdog = Time.realtimeSinceStartup + 5f;
        while (Time.time < end) { Require(Time.realtimeSinceStartup < watchdog, "Scaled visual wait exceeded watchdog."); yield return null; }
    }
    private static bool Near(float value, float expected, float tolerance = 0.0001f) => Mathf.Abs(value - expected) <= tolerance;
    private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(value);
    private static void SetField(object value, string name, object setting) => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(value, setting);
    private static void Method(object value, string name) => value.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(value, null);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private void CheckResult(string name, bool passed, string details) { report.checks.Add(new Check { name = name, passed = passed, details = details }); Flush(); }
    private void OnLog(string message, string stack, LogType type) { if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert) runtimeErrors.Add(message + "\n" + stack); }
    private void Update() { if (!ending && Time.realtimeSinceStartup - started > 120f) Fail(new TimeoutException("Control QA exceeded 120 seconds.")); }
    private IEnumerator Guarded(IEnumerator sequence)
    {
        var stack = new Stack<IEnumerator>(); stack.Push(sequence);
        while (stack.Count > 0 && !ending)
        {
            object current;
            try { IEnumerator top = stack.Peek(); if (!top.MoveNext()) { stack.Pop(); continue; } current = top.Current; }
            catch (Exception exception) { Fail(exception); yield break; }
            if (current is IEnumerator nested) stack.Push(nested); else yield return current;
        }
    }
    private void Flush() { if (!string.IsNullOrEmpty(reportPath)) File.WriteAllText(reportPath, JsonUtility.ToJson(report, true)); }
    private void Fail(Exception exception)
    {
        if (ending) return;
        report.errors.Add(exception.ToString());
        Complete();
    }
    private void Complete()
    {
        if (ending) return;
        ending = true;
        bool passed = report.errors.Count == 0 && runtimeErrors.Count == 0;
        foreach (Check check in report.checks) passed &= check.passed;
        report.status = passed ? "passed" : "failed";
        Flush();
        Application.logMessageReceived -= OnLog;
        Application.Quit(passed ? 0 : 1);
    }
}
