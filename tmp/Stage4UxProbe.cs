#if STAGE4_UX_QA
// Isolated opt-in actual-input Stage 4 route validator. Authored map/physics stay untouched.
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
public sealed class Stage4UxProbe : MonoBehaviour
{
    [Serializable] sealed class Check { public string name, details; public bool passed; }
    [Serializable] sealed class Trace
    {
        public float seconds,input,charge,verticalVelocity,ascent,descent,brake;
        public string phase,instruction,measurement;
        public Vector2 position,field,force;
        public bool inside,ascentFlag,descentFlag,brakeFlag;
        public int waypoint,retries,stars;
    }
    [Serializable] sealed class Report
    {
        public string status="running",startedUtc,finishedUtc,mode,graphics;
        public string method="Saved Stage 4 scene. Virtual Keyboard/Mouse -> real enabled InputSystem, PlayerController, LeverMover and UI EventSystem. Normal physics/ground/star/goal callbacks. No player position/velocity/force writes, synthetic collisions or flag mutations. Only actual R/Backspace game commands reset/recover.";
        public bool progressRestored,sourceFixed=true,physicsUnchanged=true,actualStar,actualGoal,resultVisible,menuReturned;
        public bool fieldAscentObserved,neutralDescentObserved,negativeBrakeObserved;
        public int mouseDragSamples,uiChargeClicks,groundEvents,retries,width,height,prematureRecommendationChecks;
        public float maximumSourceDisplacement,elapsedSeconds,fieldAscentMeters,neutralDescentMeters,negativeBrakeSeconds;
        public List<string> landings=new List<string>(),screenshots=new List<string>(),errors=new List<string>();
        public List<Check> checks=new List<Check>(); public List<Trace> trace=new List<Trace>();
    }
    Report report; string reportPath; QaProgressPreferences progress;
    Keyboard keyboard; Mouse mouse; readonly HashSet<Key> commandKeys=new HashSet<Key>();
    Vector2 mousePosition; bool mouseDown,finished,begun,driving,handlingRecovery,waypointLanded;
    float input,began,routeBegan,nextTrace,ascentStart=float.NaN,descentStart=float.NaN,brakeRun;
    int waypoint; string phase="UI checks";
    ElectricStageSelector selector; ElectricLearningController2D lesson; ElectricStageSession2D session;
    ElectricStar2D star; ElectricGoal2D goal; UniformField2D field; PlayerMovement movement; Rigidbody2D body;
    MonoBehaviour guide; Transform lower,upper; Vector3 lowerStart,upperStart; BoxCollider2D zone;
    Vector2 zoneSize; float fieldVoltage,maximumForce,mass,gravity,jump,speed,acceleration,deceleration;
    readonly List<Collider2D> shelves=new List<Collider2D>(); readonly ContactPoint2D[] contacts=new ContactPoint2D[32];
    InputSettings.UpdateMode oldMode; InputSettings.BackgroundBehavior oldBackground;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
        if(Application.isEditor||!Environment.GetCommandLineArgs().Contains("--qa-stage4-ux"))return;
        string path=Argument("--qa-report=");if(string.IsNullOrWhiteSpace(path)||!Path.IsPathRooted(path))return;
        var go=new GameObject("Stage 4 UX QA input observer");DontDestroyOnLoad(go);
        go.AddComponent<Stage4UxProbe>().Initialize(path,Argument("--qa-case=")??"default");
    }
    static string Argument(string prefix)=>Environment.GetCommandLineArgs().FirstOrDefault(a=>a.StartsWith(prefix,StringComparison.Ordinal))?.Substring(prefix.Length);
    void Initialize(string path,string mode)
    {
        reportPath=path;progress=QaProgressPreferences.Capture(path+".ProgressBackup.json");
        report=new Report{startedUtc=DateTime.UtcNow.ToString("O"),mode=mode,graphics=SystemInfo.graphicsDeviceName};began=Time.realtimeSinceStartup;
        Application.runInBackground=true;Application.targetFrameRate=60;QualitySettings.vSyncCount=0;
        oldMode=InputSystem.settings.updateMode;oldBackground=InputSystem.settings.backgroundBehavior;
        InputSystem.settings.updateMode=InputSettings.UpdateMode.ProcessEventsManually;InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
        keyboard=InputSystem.AddDevice<Keyboard>("Stage4UxKeyboard");keyboard.MakeCurrent();mouse=InputSystem.AddDevice<Mouse>("Stage4UxMouse");mouse.MakeCurrent();
        Application.logMessageReceived+=OnLog;Write();StartCoroutine(Guard(Run()));
    }
    void Update()
    {
        if(finished||report==null)return;
        try
        {
            if(Time.realtimeSinceStartup-began>150)throw new TimeoutException("QA real-time watchdog.");
            var keys=new List<Key>(commandKeys);if(input<-.5f)keys.Add(Key.A);else if(input>.5f)keys.Add(Key.D);
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys.ToArray()));
            InputSystem.QueueStateEvent(mouse,new MouseState{position=mousePosition,buttons=(ushort)(mouseDown?1:0)});InputSystem.Update();
        }
        catch(Exception e){Fail(e);}
    }
    void FixedUpdate()
    {
        if(finished||!begun||body==null||goal.IsCleared)return;
        try
        {
            Observe();if(driving&&!handlingRecovery)Drive();
            if(driving&&Time.fixedTime-routeBegan>70)throw new TimeoutException("Saved map route exceeded 70 simulation seconds.");
            if(Time.fixedTime>=nextTrace)
            {
                nextTrace=Time.fixedTime+.12f;field.TryGetSample(out var sample);
                report.trace.Add(new Trace{seconds=Time.fixedTime-routeBegan,input=input,position=body.position,verticalVelocity=body.linearVelocity.y,
                    waypoint=waypoint,phase=phase,instruction=Prop<string>(guide,"CurrentInstruction"),measurement=Prop<string>(guide,"CurrentMeasurement"),
                    charge=selector.Ball.SignedCharge,field=sample.Field,force=sample.Force,inside=sample.IsInRange,
                    ascent=report.fieldAscentMeters,descent=report.neutralDescentMeters,brake=report.negativeBrakeSeconds,
                    ascentFlag=lesson.HasFieldAscent,descentFlag=lesson.HasNeutralFieldDescent,brakeFlag=lesson.HasRestoredNegativeField,
                    retries=session.RetryCount,stars=selector.Ball.Stars});Write();
            }
        }
        catch(Exception e){Fail(e);}
    }
    IEnumerator Run()
    {
        yield return Until(()=>SceneTransitionController.IsAvailable&&!SceneTransitionController.IsBusy,15,"Scene service");
        Need(SceneTransitionController.LoadScene(ElectricStageProgression.StageScenePath(4)),"Initial saved Stage 4 transition accepted");
        yield return Until(()=>!SceneTransitionController.IsBusy&&(selector=FindFirstObjectByType<ElectricStageSelector>())!=null
            &&selector.SelectedStage==4&&(session=selector.GetComponent<ElectricStageSession2D>()).IsInitialized,15,"Saved Stage 4 initialization");
        lesson=selector.GetComponent<ElectricLearningController2D>();body=selector.Ball.Body;movement=selector.Ball.GetComponent<PlayerMovement>();
        field=selector.GetStageComponent<UniformField2D>(4);star=selector.GetStageComponent<ElectricStar2D>(4);goal=selector.GetStageComponent<ElectricGoal2D>(4);
        guide=FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include,FindObjectsSortMode.None).FirstOrDefault(v=>v.GetType().Name=="StageFourGuidanceView");
        Need(guide!=null,"Stage4 guidance component exists");lower=Field<Transform>(field,"lowerPlate");upper=Field<Transform>(field,"upperPlate");lowerStart=lower.position;upperStart=upper.position;
        zone=field.GetComponent<BoxCollider2D>();zoneSize=zone.size;fieldVoltage=Field<float>(field,"voltage");maximumForce=Field<float>(field,"maximumForce");
        mass=body.mass;gravity=body.gravityScale;jump=Field<float>(movement,"jumpForce");acceleration=Field<float>(movement,"acceleration");deceleration=Field<float>(movement,"deceleration");
        speed=Field<float>(movement,"maxMoveSpeed");if(speed<=0)speed=Field<float>(movement,"moveSpeed");
        foreach(var collider in FindObjectsByType<Collider2D>(FindObjectsSortMode.None))
            if(collider.enabled&&!collider.isTrigger&&collider.name.StartsWith("Route ",StringComparison.Ordinal)&&!collider.name.Contains("Marker"))shelves.Add(collider);
        shelves.Sort((a,b)=>string.CompareOrdinal(a.name,b.name));Need(shelves.Count==4,"Four original route shelves preserved");
        movement.LandedOnGround+=_=>report.groundEvents++;star.OnCollected.AddListener(()=>{report.actualStar=true;Write();});goal.OnCleared.AddListener(()=>{report.actualGoal=true;Write();});
        yield return Frames(6);Need(Prop<bool>(guide,"IsGuidanceVisible"),"Initial Stage4 guidance visible");
        Need(field.FieldDirection.y<-.5f,"Initial field points down");ValidateLayout("initial");yield return Capture("initial");
        yield return KeyTap(Key.Escape);yield return Frames(3);var pause=FindFirstObjectByType<PauseMenuController>();
        Need(pause!=null&&pause.IsPaused&&!Prop<bool>(guide,"IsGuidanceVisible")&&!Prop<bool>(guide,"IsFieldGuideVisible")&&!Prop<bool>(guide,"IsForceArrowVisible"),"Pause hides Stage4 controls and field arrows");
        yield return Capture("pause");yield return KeyTap(Key.Escape);yield return Frames(3);
        yield return Click(Prop<Button>(guide,"DetailsButton"));yield return new WaitForSecondsRealtime(.4f);
        Need(FindObjectsByType<CollapsiblePanel>(FindObjectsSortMode.None).Any(p=>p.IsOpen&&p.PausesForReading)&&!Prop<bool>(guide,"IsGuidanceVisible"),"Details opens existing safely paused help without overlays");
        ValidateOpenHelp();yield return Capture("details");yield return KeyTap(Key.Escape);yield return Frames(4);
        yield return Click(Prop<Button>(guide,"NeutralButton"));report.uiChargeClicks++;yield return Frames(2);CheckCharge(0,"Neutral UI button selects real zero charge");
        yield return KeyTap(Key.Z);CheckCharge(1,"Z selects positive charge");
        yield return Click(Prop<Button>(guide,"NegativeButton"));report.uiChargeClicks++;yield return Frames(2);CheckCharge(-1,"Negative UI button selects actual negative charge");
        yield return KeyTap(Key.C);CheckCharge(0,"C selects neutral charge");
        yield return Click(Prop<Button>(guide,"PositiveButton"));report.uiChargeClicks++;yield return Frames(2);CheckCharge(1,"Positive UI button selects actual positive charge");
        yield return KeyTap(Key.X);CheckCharge(-1,"X selects negative charge");yield return KeyTap(Key.Z);
        yield return DragLever(true);yield return Frames(3);
        Need(field.FieldDirection.y>.5f,"Actual mouse lever reverses field upward");
        Need(Vector3.Distance(lower.position,lowerStart)<.00001f&&Vector3.Distance(upper.position,upperStart)<.00001f,"Mouse reversal preserves both authored plate positions");
        Need(Prop<string>(guide,"CurrentInstruction").Contains("레버")||Prop<string>(guide,"CurrentInstruction").Contains("아래"),"Reversed field gives actionable restoration guidance",Prop<string>(guide,"CurrentInstruction"));
        yield return Capture("reversed-field");yield return KeyTap(Key.R);yield return Frames(6);
        var mover=Field<global::LeverMover>(selector.Lever.Reader,"lever");
        Need(Mathf.Abs(mover.Angle)<.001f&&Mathf.Abs(mover.Value)<.001f&&field.FieldDirection.y<-.5f,"R physically centers lever and restores downward field");
        Need(!lesson.HasFieldAscent&&!lesson.HasNeutralFieldDescent&&!lesson.HasRestoredNegativeField&&selector.Ball.Stars==0,"R resets only explicit attempt records");
        CheckCharge(1,"Restart keeps original positive spawn state");
        begun=true;routeBegan=Time.fixedTime;phase="lift";
        yield return SelectCharge(-1);driving=true;ValidateLayout("negative-rise");yield return Capture("negative-rise");
        yield return Until(()=>waypoint>=2&&star.IsCollected,30,"Actual field ascent, original upper shelves and star");
        Need(lesson.HasFieldAscent&&report.fieldAscentObserved,"At least one metre of actual negative-charge field ascent observed");
        if(report.mode=="recovery")
        {
            handlingRecovery=true;input=0;int retries=session.RetryCount;int stars=selector.Ball.Stars;float signed=selector.Ball.SignedCharge;float lever=selector.Lever.Value;
            yield return KeyTap(Key.Backspace);yield return Frames(3);
            Need(session.RetryCount==retries+1&&lesson.HasFieldAscent&&selector.Ball.Stars==stars&&star.IsCollected,"Backspace keeps earned ascent and star after real platform landing");
            Need(Mathf.Approximately(selector.Ball.SignedCharge,signed)&&Mathf.Approximately(selector.Lever.Value,lever),"Checkpoint keeps selected charge and lever position");
            handlingRecovery=false;
        }
        yield return SelectCharge(0);yield return Capture("neutral-descent");
        yield return Until(()=>phase=="exit"&&Prop<bool>(lesson,"IsFieldBrakeRecommended"),35,"Guide recommends actual final return braking window");
        Need(report.neutralDescentObserved,"Actual neutral field descent at least one metre observed");
        Need(Prop<float>(guide,"RecommendedCharge")<0,"UI recommends negative charge exactly at final return braking approach");
        Need(report.prematureRecommendationChecks>0,"Right-side neutral return was observed without premature negative recommendation","checks="+report.prematureRecommendationChecks);
        yield return SelectCharge(-1);yield return Capture("negative-brake");
        yield return Until(()=>goal.IsCleared,20,"Actual negative brake and natural goal callback");
        driving=false;input=0;yield return Frames(5);
        Need(report.actualStar&&report.actualGoal&&lesson.CurrentLearningGoalSatisfied,"Real star, goal and all original learning requirements satisfied");
        Need(lesson.HasFieldAscent&&lesson.HasNeutralFieldDescent&&lesson.HasRestoredNegativeField&&report.fieldAscentObserved&&report.neutralDescentObserved&&report.negativeBrakeObserved,"Measured ascent/descent/brake agree with lesson flags");
        Need(report.landings.Count==4,"All four authored route shelves received actual top contacts",string.Join(",",report.landings));
        Need(report.sourceFixed&&report.physicsUnchanged,"Plates, field voltage, force cap, zone and player physics remain unchanged");
        report.retries=session.RetryCount;Need(report.retries==(report.mode=="recovery"?1:0),"Only requested recovery occurred","retries="+report.retries);
        report.resultVisible=FindFirstObjectByType<ElectricGameCompleteView>()?.IsResultOpen==true;
        Need(report.resultVisible&&!Prop<bool>(guide,"IsGuidanceVisible")&&!Prop<bool>(guide,"IsFieldGuideVisible")&&!Prop<bool>(guide,"IsForceArrowVisible"),"Natural result screen hides Stage4 overlays");
#if STAGE5_UX_QA
        Need(FindFirstObjectByType<ElectricGameCompleteView>().GetComponentsInChildren<TMP_Text>(false).All(t=>!t.text.Contains("선택 풀이")),"Stage4 result omits chosen-plan summary");
#endif
        yield return Capture("result");yield return new WaitForSecondsRealtime(.6f);
        yield return Click(Field<Button>(FindFirstObjectByType<ElectricGameCompleteView>(),"mainMenuButton"));
        yield return Until(()=>SceneManager.GetActiveScene().path==ElectricGameCompleteView.MainMenuPath&&!SceneTransitionController.IsBusy,12,"Actual result button returns to menu");
        report.menuReturned=true;Need(Time.timeScale>0,"Main menu restores time scale");yield return Capture("menu");Finish();
    }
    IEnumerator SelectCharge(float charge)
    {
        if(report.mode=="keyboard")yield return KeyTap(charge<0?Key.X:charge>0?Key.Z:Key.C);
        else {yield return Click(Prop<Button>(guide,charge<0?"NegativeButton":charge>0?"PositiveButton":"NeutralButton"));report.uiChargeClicks++;}
        yield return Frames(2);CheckCharge(charge,"Route charge input applied");
    }
    void CheckCharge(float expected,string name)
    {
        Need(Mathf.Approximately(selector.Ball.SignedCharge,expected)&&Mathf.Approximately(Prop<float>(guide,"SelectedCharge"),expected),name);
        var outlines=Field<Outline[]>(guide,"selectedOutlines");int selected=expected>0?0:expected<0?1:2;
        Need(outlines.Count(o=>o.enabled)==1&&outlines[selected].enabled,"Exactly one matching polarity button is selected");
        if(field.TryGetSample(out var sample)&&sample.IsInRange)
            Need(expected==0?sample.Force.sqrMagnitude<.00001f:Vector2.Dot(sample.Force,sample.Field)*Mathf.Sign(expected)>0,"Actual field force obeys F=qE",sample.Force.ToString());
    }
    IEnumerator DragLever(bool reverse)
    {
        var mover=Field<global::LeverMover>(selector.Lever.Reader,"lever");var grab=Field<Collider2D>(mover,"grabArea");var pivot=Field<Transform>(mover,"pivot");
        mousePosition=Camera.main.WorldToScreenPoint(grab.bounds.center);mouseDown=false;yield return Frames(2);mouseDown=true;yield return Frames(2);
        Need(Field<bool>(mover,"dragging"),"Actual mouse grabs original lever collider");Vector3 from=grab.bounds.center,to=pivot.position+(reverse?Vector3.left:Vector3.right)*1.2f+Vector3.up*.001f;
        for(int i=1;i<=8;i++){mousePosition=Camera.main.WorldToScreenPoint(Vector3.Lerp(from,to,i/8f));report.mouseDragSamples++;yield return Frames(1);}
        mouseDown=false;yield return Frames(3);Need(!Field<bool>(mover,"dragging"),"Mouse release ends lever drag");
    }
    void Drive()
    {
        if(phase=="lift"){Steer(-3);if(lesson.NegativeChallengeSatisfied&&body.position.y>=.8f)phase="route";return;}
        if(phase=="route")
        {
            var shelf=shelves[waypoint];float x=shelf.bounds.center.x;Steer(x);
            bool near=Mathf.Abs(body.position.x-x)<shelf.bounds.extents.x*.65f;
            bool top=body.position.y>=shelf.bounds.max.y&&body.position.y<shelf.bounds.max.y+1f;
            bool starShelf=Mathf.Abs(x-star.transform.position.x)<.5f;
            if(waypointLanded&&near&&top&&(!starShelf||star.IsCollected)){waypoint++;waypointLanded=false;if(waypoint==shelves.Count)phase="exit";}return;
        }
        if(phase=="exit")Steer(goal.transform.position.x);
    }
    void Observe()
    {
        report.maximumSourceDisplacement=Mathf.Max(report.maximumSourceDisplacement,Vector3.Distance(lower.position,lowerStart),Vector3.Distance(upper.position,upperStart));
        report.sourceFixed&=report.maximumSourceDisplacement<.00001f;
        report.physicsUnchanged&=Mathf.Approximately(body.mass,mass)&&Mathf.Approximately(body.gravityScale,gravity)&&Mathf.Approximately(Field<float>(movement,"jumpForce"),jump)
            &&Mathf.Approximately(Field<float>(movement,"acceleration"),acceleration)&&Mathf.Approximately(Field<float>(movement,"deceleration"),deceleration)
            &&Mathf.Approximately(Field<float>(field,"voltage"),fieldVoltage)&&Mathf.Approximately(Field<float>(field,"maximumForce"),maximumForce)&&zone.size==zoneSize;
        field.TryGetSample(out var sample);bool down=sample.IsInRange&&sample.Field.y<-.01f;
        if(lesson.HasNeutralFieldDescent&&!lesson.HasRestoredNegativeField&&body.position.x>2.2f&&selector.Ball.CurrentPolarity==Polarity.Neutral)
        {
            if(Prop<bool>(lesson,"IsFieldBrakeRecommended")||Prop<float>(guide,"RecommendedCharge")<-.1f)
                throw new InvalidOperationException("Premature negative-charge recommendation on original right return path.");
            report.prematureRecommendationChecks++;
        }
        float progress=Prop<float>(guide,"CurrentStepProgress");if(progress<0||progress>1)throw new InvalidOperationException("Stage4 observed progress outside 0..1.");
        bool ascending=down&&sample.SignedCharge<0&&sample.Force.y>.01f&&body.linearVelocity.y>.25f;
        if(ascending){if(float.IsNaN(ascentStart))ascentStart=body.position.y;report.fieldAscentMeters=Mathf.Max(report.fieldAscentMeters,body.position.y-ascentStart);report.fieldAscentObserved|=report.fieldAscentMeters>=1;}else ascentStart=float.NaN;
        bool falling=report.fieldAscentObserved&&star.IsCollected&&down&&selector.Ball.CurrentPolarity==Polarity.Neutral&&Mathf.Abs(sample.Force.y)<.001f&&body.linearVelocity.y<-.25f;
        if(falling){if(float.IsNaN(descentStart))descentStart=body.position.y;report.neutralDescentMeters=Mathf.Max(report.neutralDescentMeters,descentStart-body.position.y);report.neutralDescentObserved|=report.neutralDescentMeters>=1;}else descentStart=float.NaN;
        bool brake=report.neutralDescentObserved&&down&&sample.SignedCharge<0&&sample.Force.y>.01f&&body.linearVelocity.y<-.25f;
        brakeRun=brake?brakeRun+Time.fixedDeltaTime:0;report.negativeBrakeSeconds=Mathf.Max(report.negativeBrakeSeconds,brakeRun);report.negativeBrakeObserved|=report.negativeBrakeSeconds>=.1f-.0001f;
        int count=body.GetContacts(contacts);for(int i=0;i<count;i++)
        {
            var c=contacts[i];var other=c.collider.attachedRigidbody==body?c.otherCollider:c.collider;
            bool top=Mathf.Abs(c.normal.y)>.6f&&body.position.y>c.point.y+.25f;
            if(top&&other!=null&&shelves.Contains(other)){if(!report.landings.Contains(other.name))report.landings.Add(other.name);if(waypoint<shelves.Count&&other==shelves[waypoint])waypointLanded=true;}
        }
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
    void Steer(float x)
    {
        float error=x-body.position.x, velocity=body.linearVelocity.x;
        float limit=Mathf.Min(speed,Mathf.Sqrt(2f*acceleration*Mathf.Abs(error)));
        float desired=Mathf.Clamp(error*2.5f-velocity*.45f,-limit,limit);
        input=Mathf.Abs(desired-velocity)>.15f?Mathf.Sign(desired-velocity):0;
        if(Mathf.Abs(error)<.08f&&Mathf.Abs(velocity)<.15f)input=0;
    }
    void ValidateLayout(string phase)
    {
        Canvas.ForceUpdateCanvases(); report.width=Screen.width; report.height=Screen.height;
        foreach(var text in guide.GetComponentsInChildren<TMP_Text>(true))
        {
            if(!text.gameObject.activeInHierarchy||text.text.Length==0)continue;
            text.ForceMeshUpdate(); Need(!text.isTextOverflowing,"Guidance text fits: "+phase+" / "+text.name,text.text);
        }
        foreach(var name in new[]{"PositiveButton","NegativeButton","NeutralButton","DetailsButton"})
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
