#if STAGE3_UX_QA
// Isolated-player opt-in validation; every interaction goes through real InputSystem devices.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using sung_wook;

[DefaultExecutionOrder(-1000)]
public sealed class Stage3UxProbe : MonoBehaviour
{
    [Serializable] sealed class Check { public string name, details; public bool passed; }
    [Serializable] sealed class Trace
    {
        public float seconds, input, forceRatio, stoneBottom;
        public string phase, instruction, measurement;
        public Vector2 position, velocity;
        public int waypoint, retries, stars;
    }
    [Serializable] sealed class Report
    {
        public string status = "running", startedUtc, finishedUtc, mode, graphics;
        public string method = "Saved scene, real virtual Keyboard and Mouse devices through InputSystem, original enabled player/lever/input/UI components, normal Unity physics. No route-position/velocity/force edits or fake callbacks. Only the game's actual Backspace recovery is used in recovery cases.";
        public bool progressRestored, sourceFixed = true, physicsUnchanged = true, actualStar, actualGoal, stonePassWait, resultVisible, menuReturned;
        public int keyboardBaselineEdges, mouseDragSamples, uiLaunchClicks, launchEvents, groundEvents, retries, width, height;
        public float maximumSourceDisplacement, forceRatio, elapsedSeconds;
        public List<string> landings = new List<string>(), screenshots = new List<string>(), errors = new List<string>();
        public List<Check> checks = new List<Check>();
        public List<Trace> trace = new List<Trace>();
    }
    Report report;
    string reportPath;
    QaProgressPreferences progress;
    Keyboard keyboard;
    Mouse mouse;
    readonly HashSet<Key> commandKeys = new HashSet<Key>();
    Vector2 mousePosition;
    bool mouseDown, finished, begun, driving, handlingRecovery, waypointLanded;
    float input, began, routeBegan, nextTrace, normalBounceRise;
    int waypoint;
    ElectricStageSelector selector;
    ElectricLearningController2D lesson;
    ElectricStageSession2D session;
    ElectricExperimentPlan2D plan;
    ElectricStar2D star;
    ElectricGoal2D goal;
    DistanceLauncher2D launcher;
    PlayerMovement movement;
    Rigidbody2D body;
    MonoBehaviour guide;
    Transform source;
    Vector3 sourceStart;
    float mass, gravity, jump, speed, acceleration, deceleration;
    readonly List<Collider2D> shelves = new List<Collider2D>();
    readonly ContactPoint2D[] contacts = new ContactPoint2D[32];
    Collider2D middleStone;
    InputSettings.UpdateMode oldMode;
    InputSettings.BackgroundBehavior oldBackground;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
        if (Application.isEditor || !Environment.GetCommandLineArgs().Contains("--qa-stage3-ux")) return;
        string path = Argument("--qa-report=");
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)) return;
        var go = new GameObject("Stage 3 UX QA input observer"); DontDestroyOnLoad(go);
        go.AddComponent<Stage3UxProbe>().Initialize(path, Argument("--qa-case=") ?? "default");
    }
    static string Argument(string prefix) => Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal))?.Substring(prefix.Length);
    void Initialize(string path, string mode)
    {
        reportPath = path; progress = QaProgressPreferences.Capture(path + ".ProgressBackup.json");
        report = new Report { startedUtc = DateTime.UtcNow.ToString("O"), mode = mode, graphics = SystemInfo.graphicsDeviceName };
        began = Time.realtimeSinceStartup;
        Application.runInBackground = true; Application.targetFrameRate = 60; QualitySettings.vSyncCount = 0;
        oldMode = InputSystem.settings.updateMode; oldBackground = InputSystem.settings.backgroundBehavior;
        InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        keyboard = InputSystem.AddDevice<Keyboard>("Stage3UxKeyboard"); keyboard.MakeCurrent();
        mouse = InputSystem.AddDevice<Mouse>("Stage3UxMouse"); mouse.MakeCurrent();
        Application.logMessageReceived += OnLog;
        Write(); StartCoroutine(Guard(Run()));
    }
    void Update()
    {
        if (finished || report == null) return;
        try
        {
            if (Time.realtimeSinceStartup - began > 150f) throw new TimeoutException("QA real-time watchdog.");
            var keys = new List<Key>(commandKeys);
            if (input < -.5f) keys.Add(Key.A); else if (input > .5f) keys.Add(Key.D);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys.ToArray()));
            InputSystem.QueueStateEvent(mouse, new MouseState { position = mousePosition, buttons = (ushort)(mouseDown ? 1 : 0) });
            InputSystem.Update();
        }
        catch (Exception e) { Fail(e); }
    }
    void FixedUpdate()
    {
        if (finished || !begun || body == null || goal.IsCleared) return;
        try
        {
            Observe();
            if (driving && !handlingRecovery) Drive();
            if (driving && Time.fixedTime - routeBegan > 70f) throw new TimeoutException("Original map route exceeded 70 simulation seconds.");
            if (Time.fixedTime >= nextTrace)
            {
                nextTrace = Time.fixedTime + .12f;
                report.trace.Add(new Trace { seconds = Time.fixedTime - routeBegan, input = input,
                    position = body.position, velocity = body.linearVelocity, waypoint = waypoint,
                    phase = driving ? "route" : lesson.IsPreparingTrial ? "preparing" : "UI checks",
                    instruction = Prop<string>(guide, "CurrentInstruction"), measurement = Prop<string>(guide, "CurrentMeasurement"),
                    forceRatio = CurrentRatio(), retries = session.RetryCount, stars = selector.Ball.Stars,
                    stoneBottom = middleStone != null ? middleStone.bounds.min.y : 0f });
                Write();
            }
        }
        catch (Exception e) { Fail(e); }
    }
    IEnumerator Run()
    {
        yield return Until(() => SceneTransitionController.IsAvailable && !SceneTransitionController.IsBusy, 15, "Initial scene service");
        Need(SceneTransitionController.LoadScene(ElectricStageProgression.StageScenePath(3)), "Initial saved Stage 3 transition accepted");
        yield return Until(() => !SceneTransitionController.IsBusy && (selector = FindFirstObjectByType<ElectricStageSelector>()) != null
            && selector.SelectedStage == 3 && (session = selector.GetComponent<ElectricStageSession2D>()).IsInitialized, 15, "Saved Stage 3 initialization");
        lesson = selector.GetComponent<ElectricLearningController2D>(); plan = selector.GetComponent<ElectricExperimentPlan2D>();
        launcher = selector.GetStageComponent<DistanceLauncher2D>(3); body = selector.Ball.Body;
        movement = selector.Ball.GetComponent<PlayerMovement>(); star = selector.GetStageComponent<ElectricStar2D>(3); goal = selector.GetStageComponent<ElectricGoal2D>(3);
        guide = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(v => v.GetType().GetProperty("IsGuidanceVisible") != null);
        Need(guide != null, "Stage3 guidance component exists");
        source = Field<Transform>(launcher, "source"); sourceStart = source.position;
        mass = body.mass; gravity = body.gravityScale; jump = Field<float>(movement, "jumpForce"); acceleration = Field<float>(movement, "acceleration"); deceleration = Field<float>(movement, "deceleration");
        speed = Field<float>(movement, "maxMoveSpeed"); if (speed <= 0) speed = Field<float>(movement, "moveSpeed");
        normalBounceRise = Mathf.Pow(jump / mass, 2f) / (2f * Mathf.Abs(Physics2D.gravity.y) * gravity);
        foreach (var collider in FindObjectsByType<Collider2D>(FindObjectsSortMode.None))
        {
            if (!collider.enabled || collider.isTrigger || collider.attachedRigidbody == body) continue;
            if (collider.name.StartsWith("Route ", StringComparison.Ordinal) && !collider.name.Contains("Marker")) shelves.Add(collider);
            if (collider.name == "Obstacle Blue Falling Stone 02") middleStone = collider;
        }
        shelves.Sort((a,b) => string.CompareOrdinal(a.name,b.name));
        Need(shelves.Count == 3 && middleStone != null, "Original three shelves and moving stone preserved");
        movement.LandedOnGround += _ => report.groundEvents++;
        star.OnCollected.AddListener(() => { report.actualStar = true; Write(); });
        goal.OnCleared.AddListener(() => { report.actualGoal = true; Write(); });
        launcher.Launched += _ => { report.launchEvents++; Write(); };
        begun = true; routeBegan = Time.fixedTime;
        yield return Frames(5);
        Need(lesson.HasBaseline && lesson.IsPreparingTrial, "Automatic baseline and held start without B");
        Need(Mathf.Approximately(lesson.BaselineSample.SignedCharge, 1f) && Mathf.Approximately(lesson.BaselineSample.SourceCharge, .5f), "Automatic q=1 Q=.5 baseline");
        Need(Prop<bool>(guide,"IsGuidanceVisible") && Prop<bool>(guide,"IsDragHintVisible") && Prop<bool>(guide,"IsForceArrowVisible"), "Initial guidance, lever hint and force arrow visible");
        Need(!Prop<bool>(guide,"CanLaunch") && !Prop<Button>(guide,"LaunchButton").IsInteractable(), "Unchanged launch button disabled");
        ValidateLayout("initial"); yield return Capture("initial");
        yield return KeyTap(Key.E); yield return Frames(5);
        Need(lesson.IsPreparingTrial && report.launchEvents == 0, "Unchanged E launch rejected without losing automatic baseline");
        yield return KeyTap(Key.Escape); yield return Frames(4);
        var pause = FindFirstObjectByType<PauseMenuController>();
        Need(pause != null && pause.IsPaused && !Prop<bool>(guide,"IsGuidanceVisible"), "Pause hides guidance and force overlays");
        yield return Capture("pause"); yield return KeyTap(Key.Escape); yield return Frames(4);
        Need(!pause.IsPaused && Prop<bool>(guide,"IsGuidanceVisible"), "Resume restores guidance");
        yield return Click(Prop<Button>(guide,"DetailsButton")); yield return new WaitForSecondsRealtime(.4f);
        Need(FindObjectsByType<CollapsiblePanel>(FindObjectsSortMode.None).Any(p => p.IsOpen && p.PausesForReading)
            && !Prop<bool>(guide,"IsGuidanceVisible"), "Details opens existing paused help without guidance overlap");
        ValidateOpenHelp(); yield return Capture("details"); yield return KeyTap(Key.Escape); yield return Frames(6);
        Need(Time.timeScale > 0 && Prop<bool>(guide,"IsGuidanceVisible"), "Help closes and guidance resumes");
#if STAGE5_UX_QA
        Need(shelves.All(s=>s.shapeCount>0), "Latest saved Stage3 route shelves have actual collision geometry", string.Join("; ",shelves.Select(s=>s.name+" shapes="+s.shapeCount+" bounds="+s.bounds)));
#endif
        if (report.mode == "advanced")
        {
            Need(plan.SelectPlan(1), "Original advanced q-only plan remains selectable");
            yield return KeyTap(Key.N); yield return KeyTap(Key.Z); yield return KeyTap(Key.B);
            Need(Mathf.Approximately(lesson.BaselineSample.SourceCharge, 1) && Mathf.Approximately(lesson.BaselineSample.SignedCharge,.5f), "Advanced explicit q=.5 Q=1 baseline");
            yield return KeyTap(Key.E); yield return Frames(3);
            Need(lesson.IsPreparingTrial&&report.launchEvents==0&&!Prop<bool>(guide,"CanLaunch"), "Advanced unchanged early E remains blocked");
            Need(lesson.RecentFeedback.Contains("X")&&lesson.RecentFeedback.Contains("공"), "Advanced early E explains changing ball charge with X", lesson.RecentFeedback);
            yield return Capture("advanced-early-e");
            yield return KeyTap(Key.X);
        }
        else yield return DragLever();
        yield return Frames(6);
        report.forceRatio = CurrentRatio();
        Need(Mathf.Abs(report.forceRatio - 2f) < .05f, "Actual fixed-distance Coulomb force doubles", "ratio=" + report.forceRatio);
        Need(Mathf.Abs(Prop<float>(guide,"ForceRatio")-report.forceRatio)<.001f, "Displayed ratio agrees with actual physics sample");
        Need(Prop<bool>(guide,"CanLaunch") && Prop<Button>(guide,"LaunchButton").IsInteractable(), "Changed comparison enables UI launch");
        Need(!Prop<bool>(guide,"IsDragHintVisible"), "Changed Q releases initial drag hint");
        Need(Prop<string>(guide,"CurrentMeasurement").Contains("2"), "Visible measurement reports the changed force ratio", Prop<string>(guide,"CurrentMeasurement"));
        ValidateLayout("ready"); yield return Capture("ready");
        if (report.mode == "default")
        {
            yield return Click(Prop<Button>(guide,"RetryButton")); yield return Frames(5);
            CheckFreshTrialHandle();
            yield return DragLever(); yield return Frames(4);
            Need(Prop<bool>(guide,"CanLaunch"), "Physical lever can be dragged again after new experiment");
        }
        yield return Click(Prop<Button>(guide,"LaunchButton")); report.uiLaunchClicks++;
        yield return Until(() => report.launchEvents > 0 && lesson.ComparisonChallengeSatisfied && !lesson.IsPreparingTrial, 3, "Actual UI launch and qualified comparison");
        Need(lesson.SingleVariableComparisonSatisfied, "Only selected charge variable changed");
        if (report.mode == "recovery")
        {
            var qualified = lesson.QualifiedResultSample; int retries = session.RetryCount;
            yield return KeyTap(Key.Backspace); yield return Frames(5);
            Need(session.RetryCount == retries + 1 && lesson.IsPreparingTrial && lesson.ComparisonChallengeSatisfied,
                "Backspace before first landing restores held start and keeps successful comparison");
            Need(SameSample(qualified, lesson.QualifiedResultSample), "Qualified comparison snapshot survives start recovery");
            yield return KeyTap(Key.T); yield return Frames(5);
            CheckFreshTrialHandle();
            Need(!lesson.ComparisonChallengeSatisfied && !lesson.HasResult && selector.Ball.Stars==0, "Explicit T starts a fresh comparison and clears prior result");
            yield return DragLever(); yield return Frames(4);
            yield return Click(Prop<Button>(guide,"LaunchButton")); report.uiLaunchClicks++;
            yield return Until(() => report.launchEvents == 2 && !lesson.IsPreparingTrial, 3, "UI relaunch after retained comparison");
        }
        driving = true; routeBegan = Time.fixedTime;
        if (report.mode == "recovery")
        {
            yield return Until(() => report.landings.Contains(shelves[0].name), 15, "Real first landing before checkpoint recovery");
            handlingRecovery = true; input = 0;
            var result = lesson.QualifiedResultSample; int retries = session.RetryCount;
            yield return KeyTap(Key.Backspace); yield return Frames(4);
            Need(session.RetryCount == retries + 1 && !lesson.IsPreparingTrial && lesson.ComparisonChallengeSatisfied,
                "Backspace after real landing uses saved platform checkpoint");
            Need(SameSample(result, lesson.QualifiedResultSample) && body.position.x > .1f, "Qualified snapshot and landing-side location retained");
            handlingRecovery = false;
            yield return Until(() => star.IsCollected, 30, "Natural star collection before preservation recovery");
            handlingRecovery = true; input = 0;
            int stars=selector.Ball.Stars; retries=session.RetryCount; result=lesson.QualifiedResultSample;
            yield return KeyTap(Key.Backspace); yield return Frames(4);
            Need(session.RetryCount==retries+1 && selector.Ball.Stars==stars && star.IsCollected && SameSample(result,lesson.QualifiedResultSample), "Checkpoint recovery preserves collected star and successful comparison");
            handlingRecovery = false;
        }
        yield return Until(() => goal.IsCleared, 75, "Natural original map completion");
        driving = false; input = 0; yield return Frames(5);
        Need(report.actualStar && report.actualGoal && lesson.CurrentLearningGoalSatisfied, "Real star/goal callbacks and learning gate satisfied");
        Need(report.landings.Count >= 3 && report.stonePassWait, "All three original shelves receive actual top contacts and moving stone passage is timed");
        Need(plan.PlanSatisfied && (plan.CompletedPlans & (1 << plan.SelectedPlan)) != 0, "Selected comparison plan recorded by real goal callback");
        Need(Prop<bool>(guide,"HasLandedAfterLaunch"), "Guidance observed actual landing");
        report.resultVisible = FindFirstObjectByType<ElectricGameCompleteView>()?.IsResultOpen == true;
        Need(report.resultVisible && !Prop<bool>(guide,"IsGuidanceVisible"), "Real result screen has no guidance overlay");
#if STAGE5_UX_QA
        Need(FindFirstObjectByType<ElectricGameCompleteView>().GetComponentsInChildren<TMP_Text>(false).All(t=>!t.text.Contains("선택 풀이")), "Stage3 result omits chosen-plan summary while preserving its earned record");
#endif
        Need(report.mode == "advanced" || report.keyboardBaselineEdges == 0, "Default completion never needs B baseline key");
        Need(report.sourceFixed && report.physicsUnchanged, "Electrode remains fixed and all original physics settings unchanged");
        report.retries = session.RetryCount;
        Need(report.mode == "recovery" ? report.retries == 3 : report.retries == 0, "Only requested recoveries occurred", "retries=" + report.retries);
        yield return Capture("result"); yield return new WaitForSecondsRealtime(.6f);
        var resultView = FindFirstObjectByType<ElectricGameCompleteView>();
        yield return Click(Field<Button>(resultView, "mainMenuButton"));
        yield return Until(() => SceneManager.GetActiveScene().path == ElectricGameCompleteView.MainMenuPath && !SceneTransitionController.IsBusy, 12, "Result returns to main menu");
        report.menuReturned = true; Need(Time.timeScale > 0, "Main menu restores time scale");
        yield return Capture("menu"); Finish();
    }
    IEnumerator DragLever()
    {
        var reader = selector.Lever.Reader;
        Need(reader != null, "Saved lever reader connected");
        var mover = Field<global::LeverMover>(reader,"lever");
        Collider2D grab = Field<Collider2D>(mover, "grabArea"); Transform pivot = Field<Transform>(mover, "pivot");
        mousePosition = Camera.main.WorldToScreenPoint(grab.bounds.center); mouseDown = false; yield return Frames(2);
        Need(mousePosition.x >= 0 && mousePosition.x <= Screen.width && mousePosition.y >= 0 && mousePosition.y <= Screen.height,
            "Highlighted real lever grab area is inside current viewport", mousePosition.ToString());
        mouseDown = true; yield return Frames(3);
        Need(Field<bool>(mover,"dragging"), "Actual mouse press grabs saved lever collider");
        Vector3 from = grab.bounds.center; Vector3 to = pivot.position + Vector3.left * 1.2f + Vector3.up * .001f;
        for (int step = 1; step <= 12; step++) { mousePosition = Camera.main.WorldToScreenPoint(Vector3.Lerp(from,to,step/12f)); report.mouseDragSamples++; yield return Frames(2); }
        mouseDown = false; yield return Frames(4);
        Need(!Field<bool>(mover,"dragging") && selector.Lever.Value > .98f, "Actual mouse drag reaches Q lever endpoint and releases", "lever=" + selector.Lever.Value);
    }
    void CheckFreshTrialHandle()
    {
        var reader=selector.Lever.Reader;
        var mover=Field<global::LeverMover>(reader,"lever");
        Need(lesson.IsPreparingTrial&&lesson.HasBaseline&&Mathf.Approximately(lesson.BaselineSample.SignedCharge,1f)&&Mathf.Approximately(lesson.BaselineSample.SourceCharge,.5f), "New experiment restores automatic baseline");
        Need(Mathf.Abs(mover.Angle)<.001f&&Mathf.Abs(mover.Value)<.001f&&selector.Lever.Value<.001f&&!Field<bool>(selector.Lever,"useManualValue"), "New experiment physically centers actual lever and keeps mouse input active");
    }
    IEnumerator Click(Button button)
    {
        Need(button != null && button.IsActive() && button.IsInteractable(), "UI button available: " + (button != null ? button.name : "missing"));
        Canvas.ForceUpdateCanvases(); var rect = (RectTransform)button.transform; Canvas canvas = button.GetComponentInParent<Canvas>().rootCanvas;
        Vector3 world = rect.TransformPoint(rect.rect.center);
        mousePosition = RectTransformUtility.WorldToScreenPoint(canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera,world);
        var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=mousePosition},hits);
        Need(hits.Count>0 && (hits[0].gameObject==button.gameObject || hits[0].gameObject.transform.IsChildOf(button.transform)), "Button is top actual EventSystem hit: " + button.name, hits.Count>0?hits[0].gameObject.name:"none");
        mouseDown = false; yield return Frames(2); mouseDown = true; yield return Frames(2); mouseDown = false; yield return Frames(3);
    }
    IEnumerator KeyTap(Key key)
    {
        if (key==Key.B) report.keyboardBaselineEdges++;
        commandKeys.Add(key); yield return Frames(2); commandKeys.Remove(key); yield return Frames(3);
    }
    static IEnumerator Frames(int count) { while(count-->0) yield return null; }
    IEnumerator Until(Func<bool> predicate,float seconds,string description)
    {
        float until=Time.realtimeSinceStartup+seconds;
        while(!predicate()) { if(Time.realtimeSinceStartup>until) throw new TimeoutException(description); yield return null; }
    }
    IEnumerator Guard(IEnumerator initial)
    {
        var stack=new Stack<IEnumerator>(); stack.Push(initial);
        while(stack.Count>0&&!finished)
        {
            object value=null; bool next=false;
            try { next=stack.Peek().MoveNext(); if(next)value=stack.Peek().Current; }
            catch(Exception e) { Fail(e); yield break; }
            if(!next){stack.Pop();continue;}
            if(value is IEnumerator nested){stack.Push(nested);continue;}
            yield return value;
        }
    }
    void Drive()
    {
        if(waypoint>=shelves.Count){Steer(star.IsCollected?goal.transform.position.x:star.transform.position.x);return;}
        Collider2D shelf=shelves[waypoint];
        if(waypoint==2&&!report.stonePassWait)
        {
            Steer(4.3f);
            float radius=selector.Ball.GetComponent<Collider2D>().bounds.extents.y;
            float safeBottom=shelves[1].bounds.max.y+radius*2f+normalBounceRise+.1f;
            bool stoneSafe=middleStone.bounds.min.y>safeBottom&&(middleStone.attachedRigidbody==null||middleStone.attachedRigidbody.linearVelocity.y>=-.01f);
            bool earlyBounce=body.linearVelocity.y>4.5f&&body.position.y<shelves[1].bounds.max.y+radius+1.2f;
            if(!stoneSafe||!earlyBounce)return; report.stonePassWait=true;
        }
        float x=waypoint==1?4.25f:shelf.bounds.center.x; Steer(x);
        bool near=Mathf.Abs(body.position.x-x)<shelf.bounds.extents.x*.65f;
        bool top=body.position.y>=shelf.bounds.max.y&&body.position.y<shelf.bounds.max.y+1f;
        bool starShelf=Mathf.Abs(shelf.bounds.center.x-star.transform.position.x)<.5f;
        if(waypointLanded&&near&&top&&(!starShelf||star.IsCollected)){waypoint++;waypointLanded=false;}
    }
    void Steer(float x)
    {
        float error=x-body.position.x, velocity=body.linearVelocity.x;
        float limit=Mathf.Min(speed,Mathf.Sqrt(2f*acceleration*Mathf.Abs(error)));
        float desired=Mathf.Clamp(error*2.5f-velocity*.45f,-limit,limit);
        input=Mathf.Abs(desired-velocity)>.15f?Mathf.Sign(desired-velocity):0;
        if(Mathf.Abs(error)<.08f&&Mathf.Abs(velocity)<.15f)input=0;
    }
    void Observe()
    {
        report.maximumSourceDisplacement=Mathf.Max(report.maximumSourceDisplacement,Vector3.Distance(source.position,sourceStart));
        report.sourceFixed &= report.maximumSourceDisplacement < .00001f;
        report.physicsUnchanged &= Mathf.Approximately(body.mass,mass)&&Mathf.Approximately(body.gravityScale,gravity)
            &&Mathf.Approximately(Field<float>(movement,"jumpForce"),jump)&&Mathf.Approximately(Field<float>(movement,"acceleration"),acceleration)
            &&Mathf.Approximately(Field<float>(movement,"deceleration"),deceleration);
        int count=body.GetContacts(contacts);
        for(int i=0;i<count;i++)
        {
            var c=contacts[i]; Collider2D other=c.collider.attachedRigidbody==body?c.otherCollider:c.collider;
            bool top=Mathf.Abs(c.normal.y)>.6f&&body.position.y>c.point.y+.25f;
            if(top&&other!=null&&shelves.Contains(other))
            {
                if(!report.landings.Contains(other.name))report.landings.Add(other.name);
                if(waypoint<shelves.Count&&other==shelves[waypoint])waypointLanded=true;
            }
        }
    }
    float CurrentRatio() => lesson!=null&&lesson.HasBaseline&&lesson.TryGetCurrentSample(out var sample)&&lesson.BaselineSample.ForceMagnitude>0?sample.ForceMagnitude/lesson.BaselineSample.ForceMagnitude:0;
    static bool SameSample(ElectricForceSample a,ElectricForceSample b)=>a.Position==b.Position&&Mathf.Approximately(a.Distance,b.Distance)&&Mathf.Approximately(a.SignedCharge,b.SignedCharge)&&Mathf.Approximately(a.SourceCharge,b.SourceCharge)&&a.Force==b.Force;
    void ValidateLayout(string phase)
    {
        Canvas.ForceUpdateCanvases(); report.width=Screen.width; report.height=Screen.height;
        foreach(var text in guide.GetComponentsInChildren<TMP_Text>(true))
        {
            if(!text.gameObject.activeInHierarchy||text.text.Length==0)continue;
            text.ForceMeshUpdate(); Need(!text.isTextOverflowing,"Guidance text fits: "+phase+" / "+text.name,text.text);
        }
        foreach(var name in new[]{"LaunchButton","RetryButton","DetailsButton"})
        {
            Button b=Prop<Button>(guide,name); var corners=new Vector3[4]; ((RectTransform)b.transform).GetWorldCorners(corners);
            Canvas canvas=b.GetComponentInParent<Canvas>().rootCanvas;
            var points=corners.Select(c=>RectTransformUtility.WorldToScreenPoint(canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera,c)).ToArray();
            Need(points.All(p=>p.x>=-1&&p.y>=-1&&p.x<=Screen.width+1&&p.y<=Screen.height+1),"Guidance button inside viewport: "+phase+" / "+name);
        }
    }
    void ValidateOpenHelp()
    {
        Canvas.ForceUpdateCanvases();
        var help=FindObjectsByType<CollapsiblePanel>(FindObjectsSortMode.None).First(p=>p.IsOpen);
        var content=Field<LayoutElement>(help,"content");
        float expected=Field<float>(help,"openHeight");
        Need(Mathf.Abs(content.preferredHeight-expected)<.1f,"Help opening animation fully settled","height="+content.preferredHeight+", expected="+expected);
        var hud=FindFirstObjectByType<ElectricStageHudView>();
        var text=Field<TMP_Text>(hud,"objectiveBodyText");text.ForceMeshUpdate();
        Need(!text.isTextOverflowing,"Expanded help body has no TMP overflow",text.text);
        Canvas canvas=text.GetComponentInParent<Canvas>().rootCanvas;
        Camera camera=canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera;
        Rect mask=ScreenRect((RectTransform)content.transform,camera), toggle=ScreenRect((RectTransform)help.ToggleButton.transform,camera);
        int glyphs=0;float minY=float.PositiveInfinity,maxY=float.NegativeInfinity;
        for(int index=0;index<text.textInfo.characterCount;index++)
        {
            var character=text.textInfo.characterInfo[index];
            if(!character.isVisible)continue;
            Vector2 a=RectTransformUtility.WorldToScreenPoint(camera,text.transform.TransformPoint(character.bottomLeft));
            Vector2 b=RectTransformUtility.WorldToScreenPoint(camera,text.transform.TransformPoint(character.topRight));
            minY=Mathf.Min(minY,a.y);maxY=Mathf.Max(maxY,b.y);glyphs++;
            bool inside=a.x>=mask.xMin-1&&b.x<=mask.xMax+1&&a.y>=mask.yMin-1&&b.y<=mask.yMax+1;
            if(!inside)throw new InvalidOperationException("Help glyph outside settled Content mask: "+character.character+" "+a+".."+b+" mask="+mask);
            bool overlap=b.x>toggle.xMin+1&&a.x<toggle.xMax-1&&b.y>toggle.yMin+1&&a.y<toggle.yMax-1;
            if(overlap)throw new InvalidOperationException("Help glyph overlaps close tab: "+character.character);
        }
        Need(glyphs>20,"All expanded help body glyphs stay inside mask and above close tab","glyphs="+glyphs+", y="+minY+".."+maxY+", Content="+mask+", close="+toggle);
        Need(Prop<string>(guide,"CurrentInstruction").Contains("원리")||Prop<string>(guide,"CurrentInstruction").Contains("읽"),"Help header explains reading state",Prop<string>(guide,"CurrentInstruction"));
    }
    static Rect ScreenRect(RectTransform rect,Camera camera)
    {
        var corners=new Vector3[4];rect.GetWorldCorners(corners);
        Vector2 a=RectTransformUtility.WorldToScreenPoint(camera,corners[0]);
        Vector2 b=RectTransformUtility.WorldToScreenPoint(camera,corners[2]);
        return Rect.MinMaxRect(a.x,a.y,b.x,b.y);
    }
    IEnumerator Capture(string label)
    {
        if(Application.isBatchMode)yield break;
        yield return new WaitForEndOfFrame(); var texture=ScreenCapture.CaptureScreenshotAsTexture();
        string path=Path.Combine(Path.GetDirectoryName(reportPath),Path.GetFileNameWithoutExtension(reportPath)+"-"+label+".png");
        File.WriteAllBytes(path,texture.EncodeToPNG()); Destroy(texture); report.screenshots.Add(path); Write();
    }
    static T Prop<T>(object value,string name)=>(T)(value?.GetType().GetProperty(name)?.GetValue(value)??throw new MissingMemberException(value?.GetType().Name,name));
    static T Field<T>(object value,string name)=>(T)(value?.GetType().GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)?.GetValue(value)??throw new MissingFieldException(value?.GetType().Name,name));
    void Need(bool ok,string name,string details="")
    {
        report.checks.Add(new Check{name=name,passed=ok,details=details});Write();
        if(!ok)throw new InvalidOperationException(name+": "+details);
    }
    void OnLog(string message,string stack,LogType type)
    {
        if(!finished&&(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)){report.errors.Add(message+"\n"+stack);Write();}
    }
    void Fail(Exception e){if(finished)return;report.errors.Add(e.ToString());Finish();}
    void Write(){report.elapsedSeconds=Time.realtimeSinceStartup-began;Directory.CreateDirectory(Path.GetDirectoryName(reportPath));File.WriteAllText(reportPath,JsonUtility.ToJson(report,true));}
    void Finish()
    {
        if(finished)return;finished=true;
        try{progress.Restore();report.progressRestored=true;}catch(Exception e){report.errors.Add(e.ToString());}
        if(keyboard!=null&&keyboard.added)InputSystem.RemoveDevice(keyboard);if(mouse!=null&&mouse.added)InputSystem.RemoveDevice(mouse);
        InputSystem.settings.updateMode=oldMode;InputSystem.settings.backgroundBehavior=oldBackground;
        report.status=report.errors.Count==0&&report.actualGoal&&report.menuReturned?"passed":"failed";
        report.finishedUtc=DateTime.UtcNow.ToString("O");Write();Application.Quit(report.status=="passed"?0:1);
    }
}
#endif
