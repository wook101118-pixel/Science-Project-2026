#if STAGE5_UX_QA
// Isolated opt-in actual-input Stage 5 route validator. Authored map/physics stay untouched.
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
public sealed class Stage5UxProbe : MonoBehaviour
{
    [Serializable] sealed class Check { public string name,details; public bool passed; }
    [Serializable] sealed class Trace
    {
        public float seconds,input,charge,voltage,launchVoltage,lever;
        public string phase,instruction,measurement; public Vector2 position,velocity;
        public bool inside,canLaunch,learning; public int retries,stars;
    }
    [Serializable] sealed class Report
    {
        public string status="running",startedUtc,finishedUtc,mode,graphics;
        public string method="Saved Stage 5 scene. Real enabled InputSystem Keyboard/Mouse, PlayerController, LeverMover and UI EventSystem; normal physics/contact/star/goal callbacks. No player position/velocity/force writes, fake collisions or learning mutations. Only actual R/Backspace game commands reset/recover.";
        public bool progressRestored,sourceFixed=true,physicsUnchanged=true,actualStar,actualGoal,resultVisible,menuReturned,realFirstLanding,realSecondLanding;
        public int mouseDragSamples,actualLaunches,groundEvents,retries,width,height;
        public float maximumSourceDisplacement,elapsedSeconds,launchVoltage,launchSpeed,expectedLaunchSpeed,peakY;
        public List<string> landings=new List<string>(),screenshots=new List<string>(),errors=new List<string>();
        public List<Check> checks=new List<Check>(); public List<Trace> trace=new List<Trace>();
    }
    Report report; string reportPath; QaProgressPreferences progress;
    Keyboard keyboard; Mouse mouse; readonly HashSet<Key> commandKeys=new HashSet<Key>();
    Vector2 mousePosition; bool mouseDown,finished,begun,driving,handlingRecovery;
    float input,began,routeBegan,nextTrace; string phase="UI checks";
    ElectricStageSelector selector; ElectricLearningController2D lesson; ElectricStageSession2D session;
    ElectricStar2D star; ElectricGoal2D goal; VoltageLauncher2D launcher; PlayerMovement movement; Rigidbody2D body;
    MonoBehaviour guide; Transform lower,upper; Vector3 lowerStart,upperStart; BoxCollider2D zone;
    Vector2 zoneSize; float maxVoltage,chargeRate,leakRate,targetVoltage,mass,gravity,jump,speed,acceleration,deceleration;
    readonly ContactPoint2D[] contacts=new ContactPoint2D[32];
    InputSettings.UpdateMode oldMode; InputSettings.BackgroundBehavior oldBackground;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
        if(Application.isEditor||!Environment.GetCommandLineArgs().Contains("--qa-stage5-ux"))return;
        string path=Argument("--qa-report=");if(string.IsNullOrWhiteSpace(path)||!Path.IsPathRooted(path))return;
        var go=new GameObject("Stage 5 UX QA input observer");DontDestroyOnLoad(go);
        go.AddComponent<Stage5UxProbe>().Initialize(path,Argument("--qa-case=")??"default");
    }
    static string Argument(string prefix)=>Environment.GetCommandLineArgs().FirstOrDefault(a=>a.StartsWith(prefix,StringComparison.Ordinal))?.Substring(prefix.Length);
    void Initialize(string path,string mode)
    {
        reportPath=path;progress=QaProgressPreferences.Capture(path+".ProgressBackup.json");
        report=new Report{startedUtc=DateTime.UtcNow.ToString("O"),mode=mode,graphics=SystemInfo.graphicsDeviceName};began=Time.realtimeSinceStartup;
        Application.runInBackground=true;Application.targetFrameRate=60;QualitySettings.vSyncCount=0;
        oldMode=InputSystem.settings.updateMode;oldBackground=InputSystem.settings.backgroundBehavior;
        InputSystem.settings.updateMode=InputSettings.UpdateMode.ProcessEventsManually;InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
        keyboard=InputSystem.AddDevice<Keyboard>("Stage5UxKeyboard");keyboard.MakeCurrent();mouse=InputSystem.AddDevice<Mouse>("Stage5UxMouse");mouse.MakeCurrent();
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
            if(driving&&Time.fixedTime-routeBegan>60)throw new TimeoutException("Saved route exceeded 60 simulation seconds.");
            if(Time.fixedTime>=nextTrace)
            {
                nextTrace=Time.fixedTime+.12f;
                report.trace.Add(new Trace{seconds=Time.fixedTime-routeBegan,input=input,position=body.position,velocity=body.linearVelocity,
                    phase=phase,instruction=Prop<string>(guide,"CurrentInstruction"),measurement=Prop<string>(guide,"CurrentMeasurement"),
                    charge=selector.Ball.SignedCharge,voltage=launcher.Voltage,launchVoltage=launcher.LastLaunchVoltage,lever=selector.Lever.Value,
                    inside=zone.OverlapPoint(body.worldCenterOfMass),canLaunch=Prop<bool>(launcher,"CanLaunch"),learning=lesson.VoltageChallengeSatisfied,
                    retries=session.RetryCount,stars=selector.Ball.Stars});Write();
            }
        }
        catch(Exception e){Fail(e);}
    }
    IEnumerator Run()
    {
        yield return Until(()=>SceneTransitionController.IsAvailable&&!SceneTransitionController.IsBusy,15,"Scene service");
        Need(SceneTransitionController.LoadScene(ElectricStageProgression.StageScenePath(5)),"Initial saved Stage 5 transition accepted");
        yield return Until(()=>!SceneTransitionController.IsBusy&&(selector=FindFirstObjectByType<ElectricStageSelector>())!=null
            &&selector.SelectedStage==5&&(session=selector.GetComponent<ElectricStageSession2D>()).IsInitialized,15,"Saved Stage 5 initialization");
        lesson=selector.GetComponent<ElectricLearningController2D>();body=selector.Ball.Body;movement=selector.Ball.GetComponent<PlayerMovement>();
        launcher=selector.GetStageComponent<VoltageLauncher2D>(5);star=selector.GetStageComponent<ElectricStar2D>(5);goal=selector.GetStageComponent<ElectricGoal2D>(5);
        guide=FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include,FindObjectsSortMode.None).FirstOrDefault(v=>v.GetType().Name=="StageFiveGuidanceView");
        Need(guide!=null,"Stage5 guidance component exists");lower=Field<Transform>(launcher,"lowerPlate");upper=Field<Transform>(launcher,"upperPlate");lowerStart=lower.position;upperStart=upper.position;
        zone=Field<BoxCollider2D>(launcher,"launchZone");zoneSize=zone.size;
        maxVoltage=Field<float>(launcher,"maximumVoltage");chargeRate=Field<float>(launcher,"chargeRate");leakRate=Field<float>(launcher,"leakRate");targetVoltage=Field<float>(launcher,"targetVoltage");
        mass=body.mass;gravity=body.gravityScale;jump=Field<float>(movement,"jumpForce");acceleration=Field<float>(movement,"acceleration");deceleration=Field<float>(movement,"deceleration");
        speed=Field<float>(movement,"maxMoveSpeed");if(speed<=0)speed=Field<float>(movement,"moveSpeed");
        Need(maxVoltage==100&&chargeRate==25&&leakRate==0&&targetVoltage==50,"Original voltage constants preserved");
        movement.LandedOnGround+=_=>report.groundEvents++;star.OnCollected.AddListener(()=>{report.actualStar=true;Write();});goal.OnCleared.AddListener(()=>{report.actualGoal=true;Write();});
        launcher.LaunchedWithVoltage+=v=>{report.actualLaunches++;report.launchVoltage=v;report.launchSpeed=body.linearVelocity.magnitude;report.expectedLaunchSpeed=ElectricMath.SpeedFromVoltage(selector.Ball.SignedCharge,v,mass);Write();};
        yield return Frames(6);Need(Prop<bool>(guide,"IsGuidanceVisible"),"Initial Stage5 guidance visible");ValidateLayout("initial");yield return Capture("initial");
        yield return KeyTap(Key.Escape);yield return Frames(3);var pause=FindFirstObjectByType<PauseMenuController>();
        Need(pause!=null&&pause.IsPaused&&!Prop<bool>(guide,"IsGuidanceVisible"),"Pause hides Stage5 guidance controls");yield return Capture("pause");
        yield return KeyTap(Key.Escape);yield return Frames(3);yield return Click(Prop<Button>(guide,"DetailsButton"));yield return new WaitForSecondsRealtime(.4f);
        Need(FindObjectsByType<CollapsiblePanel>(FindObjectsSortMode.None).Any(p=>p.IsOpen&&p.PausesForReading)&&!Prop<bool>(guide,"IsGuidanceVisible"),"Details safely pauses and hides guide");
        ValidateOpenHelp();yield return Capture("details");yield return KeyTap(Key.Escape);yield return Frames(4);
        yield return Until(()=>zone.OverlapPoint(body.worldCenterOfMass)&&body.position.y<zone.bounds.max.y-.6f,4,"Ball inside zone for zero-voltage trial");
        Need(!Prop<bool>(launcher,"CanLaunch")&&!Prop<Button>(guide,"LaunchButton").interactable,"Zero voltage blocks UI and common launch gate");
        yield return KeyTap(Key.E);Need(report.actualLaunches==0&&launcher.Voltage==0&&!launcher.HasLaunchSample,"E at 0 V does not launch or create a sample");
        Need(launcher.LastCommandFeedback.Contains("충전")||launcher.LastCommandFeedback.Contains("50"),"Zero voltage explains charging requirement",launcher.LastCommandFeedback);
        yield return DragLever(1);yield return Until(()=>launcher.Voltage>=10,5,"Real lever charges at original rate");yield return DragLever(0);
        float stored=launcher.Voltage;Need(stored>0&&stored<50,"Low-voltage trial has real stored charge",stored.ToString());
        yield return KeyTap(Key.E);Need(report.actualLaunches==0&&Mathf.Abs(launcher.Voltage-stored)<.001f,"E below 50 V rejects without consuming voltage");
        Need(!Prop<Button>(guide,"LaunchButton").interactable,"UI shares the below-target launch gate");yield return Capture("below-target");
        yield return KeyTap(Key.Space);Need(selector.Ball.SignedCharge<0,"Space selects actual negative polarity");
        yield return Until(()=>zone.OverlapPoint(body.worldCenterOfMass)&&body.position.y<zone.bounds.max.y-.6f,4,"Ball inside zone for polarity trial");yield return KeyTap(Key.E);
        Need(report.actualLaunches==0&&Mathf.Abs(launcher.Voltage-stored)<.001f&&!Prop<bool>(launcher,"CanLaunch"),"Wrong polarity rejects without consuming stored voltage");
        Need(Prop<string>(launcher,"LaunchBlockReason").Contains("양전하"),"Wrong polarity gives positive-charge restoration guidance",Prop<string>(launcher,"LaunchBlockReason"));
        int positiveClicks=0;var positiveButton=Prop<Button>(guide,"PositiveButton");positiveButton.onClick.AddListener(()=>positiveClicks++);
        yield return Capture("positive-before");yield return Click(positiveButton);yield return Frames(6);yield return Capture("positive-after");
        Need(selector.Ball.SignedCharge>0,"Positive UI restores actual positive polarity","clicks="+positiveClicks+", charge="+selector.Ball.SignedCharge+", visible="+Prop<bool>(guide,"IsGuidanceVisible")+", feedback="+launcher.LastCommandFeedback);
        input=-1;yield return Until(()=>body.position.x<zone.bounds.min.x-.6f,8,"Actual A input leaves launch zone");input=0;yield return KeyTap(Key.E);
        Need(report.actualLaunches==0&&Mathf.Abs(launcher.Voltage-stored)<.001f&&!Prop<bool>(launcher,"CanLaunch"),"Outside-zone E rejects without consuming voltage");
        Need(Prop<string>(launcher,"LaunchBlockReason").Contains("영역")||Prop<string>(launcher,"LaunchBlockReason").Contains("전극"),"Outside-zone gives return guidance",Prop<string>(launcher,"LaunchBlockReason"));
        yield return DragLever(1);yield return KeyTap(Key.R);yield return Frames(5);
        var mover=Field<global::LeverMover>(selector.Lever.Reader,"lever");
        Need(Mathf.Abs(mover.Angle)<.001f&&Mathf.Abs(mover.Value)<.001f&&selector.Lever.Value==0&&launcher.Voltage==0,"R physically centers Stage5 lever and clears voltage");
        Need(!lesson.VoltageChallengeSatisfied&&!launcher.HasLaunchSample&&selector.Ball.SignedCharge>0&&session.RetryCount==0,"R restores original positive fresh attempt");
        begun=true;routeBegan=Time.fixedTime;phase="charging";driving=true;
        yield return DragLever(1);yield return Until(()=>launcher.Voltage>=50,8,"Real handle charges to required 50 V");yield return DragLever(0);
        Need(launcher.Voltage>=50&&launcher.Voltage<60,"Actual lever stops charging near target",launcher.Voltage.ToString());
        yield return Until(()=>Prop<bool>(launcher,"CanLaunch")&&Prop<Button>(guide,"LaunchButton").interactable&&body.position.y>zone.bounds.max.y-.2f&&body.linearVelocity.y<-.2f,8,"Original bouncing ball returns inside launch zone near upper edge");
        ValidateLayout("ready");yield return Capture("ready");
        if(report.mode=="keyboard")yield return KeyTap(Key.E);else yield return Click(Prop<Button>(guide,"LaunchButton"));
        yield return Until(()=>lesson.VoltageChallengeSatisfied,3,"Actual at-target launch event and learning callback");
        phase="route";Need(report.actualLaunches==1&&launcher.LastLaunchVoltage>=50&&launcher.Voltage==0,"Successful launch consumes charge once and records pre-consumption voltage");
        Need(Mathf.Abs(report.launchSpeed-report.expectedLaunchSpeed)<.02f,"Actual launch speed follows sqrt(2qV/m)",report.launchSpeed+" vs "+report.expectedLaunchSpeed);
        yield return Capture("launched");
        if(report.mode=="recovery")
        {
            handlingRecovery=true;input=0;float earned=launcher.LastLaunchVoltage;
            yield return KeyTap(Key.Backspace);yield return Frames(3);
            Need(session.RetryCount==1&&lesson.VoltageChallengeSatisfied&&Mathf.Approximately(launcher.LastLaunchVoltage,earned),"Early Backspace preserves earned voltage record at starting floor");
            Need(body.position.x<-2&&body.position.y<1,"Early Backspace returns to actual initial-floor checkpoint",body.position.ToString());
            Need(Prop<bool>(guide,"NeedsRelaunch")&&!Prop<bool>(guide,"HasLandedAfterLaunch"),"Recovery distinguishes starting floor from actual tower landing");
            string retryInstruction=Prop<string>(guide,"CurrentInstruction");
            Need(retryInstruction.Contains("충전")||retryInstruction.Contains("레버"),"Starting-floor recovery guides recharging despite earned result",retryInstruction);
            yield return Capture("retry-start");phase="charging";handlingRecovery=false;
            yield return DragLever(1);yield return Until(()=>launcher.Voltage>=50,8,"Recharging by real mouse lever after early recovery");yield return DragLever(0);
            yield return Until(()=>Prop<bool>(launcher,"CanLaunch")&&Prop<Button>(guide,"LaunchButton").interactable&&body.position.y>zone.bounds.max.y-.2f&&body.linearVelocity.y<-.2f,8,"Recovered ball inside original launch window");
            yield return Click(Prop<Button>(guide,"LaunchButton"));yield return Until(()=>report.actualLaunches==2,3,"Actual second launch after recovery");
            phase="route";Need(lesson.VoltageChallengeSatisfied&&launcher.Voltage==0,"Repeated successful launch preserves earned goal and consumes only new charge");
        }
        yield return Until(()=>report.realFirstLanding&&report.realSecondLanding&&star.IsCollected,30,"Actual original two tower landings and star");
        Need(report.actualStar&&report.realFirstLanding&&report.realSecondLanding,"Both authored towers and natural star collection observed");
        Need(Prop<bool>(guide,"HasLandedAfterLaunch"),"Guidance observes actual landing after launch");
        Need(Prop<bool>(guide,"HasReachedSecondTower")&&!Prop<bool>(guide,"NeedsRelaunch"),"Guidance distinguishes actual second tower from restart floor");
        if(report.mode=="recovery")
        {
            handlingRecovery=true;input=0;int retries=session.RetryCount;float previous=launcher.LastLaunchVoltage;int stars=selector.Ball.Stars;
            yield return KeyTap(Key.Backspace);yield return Frames(3);
            Need(session.RetryCount==retries+1&&lesson.VoltageChallengeSatisfied&&Mathf.Approximately(launcher.LastLaunchVoltage,previous)&&selector.Ball.Stars==stars&&star.IsCollected,"Backspace preserves successful voltage and earned star");
            handlingRecovery=false;
        }
        phase="exit";yield return Until(()=>goal.IsCleared,20,"Natural goal callback");driving=false;input=0;yield return Frames(8);
        report.retries=session.RetryCount;Need(report.retries==(report.mode=="recovery"?2:0),"Route has only explicitly requested recovery",report.retries.ToString());
        Need(report.sourceFixed&&report.physicsUnchanged,"Authored plates, launch zone and player physics unchanged");
        var result=FindFirstObjectByType<ElectricGameCompleteView>();report.resultVisible=result!=null&&result.IsResultOpen;
        Need(report.resultVisible&&result.CompletedStage==5&&Time.timeScale==0,"Actual final game-complete result opens");
        yield return Frames(3);Need(!Prop<bool>(guide,"IsGuidanceVisible"),"Final result hides Stage5 guidance");
        var title=Field<TMP_Text>(result,"resultTitle");
        Need(title==null||!title.gameObject.activeInHierarchy||string.IsNullOrWhiteSpace(title.text)||title.color.a==0,"Final game-complete title is not rendered");
        foreach(var text in result.GetComponentsInChildren<TMP_Text>(true))
        {
            if(!text.gameObject.activeInHierarchy||text.color.a==0)continue;text.ForceMeshUpdate();
            Need(!text.text.Contains("선택 풀이")&&!text.text.Contains("게임 완료"),"Removed result phrases absent: "+text.name,text.text);
            Need(!text.isTextOverflowing,"Final result text fits: "+text.name,text.text);
        }
        Need(FindFirstObjectByType<PauseMenuController>()==null||!FindFirstObjectByType<PauseMenuController>().IsPaused,"Result has no overlapping pause menu");
        yield return Capture("result");yield return new WaitForSecondsRealtime(1f);yield return Click(Field<Button>(result,"mainMenuButton"));
        yield return Until(()=>!SceneTransitionController.IsBusy&&SceneManager.GetActiveScene().path==ElectricGameCompleteView.MainMenuPath,15,"Actual result Menu button transition");
        report.menuReturned=true;Need(Time.timeScale>0,"Main menu restores time scale");yield return Capture("menu");Finish();
    }
    IEnumerator DragLever(float value)
    {
        var mover=Field<global::LeverMover>(selector.Lever.Reader,"lever");var grab=Field<Collider2D>(mover,"grabArea");var pivot=Field<Transform>(mover,"pivot");
        mousePosition=Camera.main.WorldToScreenPoint(grab.bounds.center);mouseDown=false;yield return Frames(2);mouseDown=true;yield return Frames(2);
        Need(Field<bool>(mover,"dragging"),"Mouse grabs original lever collider");Vector3 from=grab.bounds.center,to=pivot.position+(value>.5f?Vector3.left*1.2f+Vector3.up*.001f:Vector3.up*1.2f);
        for(int i=1;i<=8;i++){mousePosition=Camera.main.WorldToScreenPoint(Vector3.Lerp(from,to,i/8f));report.mouseDragSamples++;yield return Frames(1);}
        mouseDown=false;yield return Frames(3);Need(!Field<bool>(mover,"dragging"),"Mouse release ends original drag");
        Need(value>.5f?selector.Lever.Value>.95f:selector.Lever.Value<.001f,"Physical lever matches intended charge/hold input",selector.Lever.Value.ToString());
    }
    void Drive()
    {
        if(phase=="charging"){Steer(-3);return;}
        if(phase=="route")
        {
            if(!report.realFirstLanding){Steer(body.position.y<2.6f&&body.position.x<-2.5f?-2.6f:-.5f);return;}
            Steer(4.8f);if(report.realSecondLanding&&Mathf.Abs(body.position.x-4.8f)<.55f)phase="star";return;
        }
        if(phase=="star"){Steer(star.transform.position.x);return;}
        if(phase=="exit")Steer(goal.transform.position.x);
    }
    void Observe()
    {
        report.peakY=Mathf.Max(report.peakY,body.position.y);
        report.maximumSourceDisplacement=Mathf.Max(report.maximumSourceDisplacement,Vector3.Distance(lower.position,lowerStart),Vector3.Distance(upper.position,upperStart));
        report.sourceFixed&=report.maximumSourceDisplacement<.00001f;
        report.physicsUnchanged&=Mathf.Approximately(body.mass,mass)&&Mathf.Approximately(body.gravityScale,gravity)&&Mathf.Approximately(Field<float>(movement,"jumpForce"),jump)
            &&Mathf.Approximately(Field<float>(movement,"acceleration"),acceleration)&&Mathf.Approximately(Field<float>(movement,"deceleration"),deceleration)
            &&Field<float>(launcher,"maximumVoltage")==maxVoltage&&Field<float>(launcher,"chargeRate")==chargeRate&&Field<float>(launcher,"leakRate")==leakRate&&Field<float>(launcher,"targetVoltage")==targetVoltage&&zone.size==zoneSize;
        int count=body.GetContacts(contacts);for(int i=0;i<count;i++)
        {
            var c=contacts[i];var other=c.collider.attachedRigidbody==body?c.otherCollider:c.collider;
            bool top=Mathf.Abs(c.normal.y)>.6f&&body.position.y>c.point.y+.25f;
            if(other==null||!other.CompareTag("Ground")||!top||!lesson.VoltageChallengeSatisfied)continue;
            if(Mathf.Abs(c.point.y-2)<.1f&&c.point.x>=-2.05f&&c.point.x<=2.05f){report.realFirstLanding=true;if(!report.landings.Contains("Original first tower top y=2"))report.landings.Add("Original first tower top y=2");}
            if(Mathf.Abs(c.point.y-3)<.1f&&c.point.x>=2.95f&&c.point.x<=7.05f){report.realSecondLanding=true;if(!report.landings.Contains("Original second tower top y=3"))report.landings.Add("Original second tower top y=3");}
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
        foreach(var name in new[]{"LaunchButton","PositiveButton","DetailsButton"})
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
