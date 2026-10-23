from pathlib import Path

root = Path(__file__).resolve().parent.parent
old = (root/'tmp/Stage4UxProbe.cs').read_text(encoding='utf-8-sig')
utility = old[old.index('    IEnumerator Click(Button button)'):]
utility = utility.replace('new[]{"PositiveButton","NegativeButton","NeutralButton","DetailsButton"}', 'new[]{"LaunchButton","DetailsButton"}')
prefix = old[:old.index('[DefaultExecutionOrder')].replace('STAGE4_UX_QA','STAGE5_UX_QA').replace('Stage 4','Stage 5')
body = r'''[DefaultExecutionOrder(-1000)]
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
        Need(!Prop<bool>(launcher,"CanLaunch")&&!Prop<Button>(guide,"LaunchButton").interactable,"Zero voltage blocks UI and common launch gate");
        yield return KeyTap(Key.E);Need(report.actualLaunches==0&&launcher.Voltage==0&&!launcher.HasLaunchSample,"E at 0 V does not launch or create a sample");
        Need(launcher.LastCommandFeedback.Contains("충전")||launcher.LastCommandFeedback.Contains("50"),"Zero voltage explains charging requirement",launcher.LastCommandFeedback);
        yield return DragLever(1);yield return Until(()=>launcher.Voltage>=10,5,"Real lever charges at original rate");yield return DragLever(0);
        float stored=launcher.Voltage;Need(stored>0&&stored<50,"Low-voltage trial has real stored charge",stored.ToString());
        yield return KeyTap(Key.E);Need(report.actualLaunches==0&&Mathf.Abs(launcher.Voltage-stored)<.001f,"E below 50 V rejects without consuming voltage");
        Need(!Prop<Button>(guide,"LaunchButton").interactable,"UI shares the below-target launch gate");yield return Capture("below-target");
        yield return KeyTap(Key.Space);Need(selector.Ball.SignedCharge<0,"Space selects actual negative polarity");yield return KeyTap(Key.E);
        Need(report.actualLaunches==0&&Mathf.Abs(launcher.Voltage-stored)<.001f&&!Prop<bool>(launcher,"CanLaunch"),"Wrong polarity rejects without consuming stored voltage");
        Need(Prop<string>(launcher,"LaunchBlockReason").Contains("양전하"),"Wrong polarity gives positive-charge restoration guidance",Prop<string>(launcher,"LaunchBlockReason"));
        yield return KeyTap(Key.Space);yield return KeyTap(Key.Space);Need(selector.Ball.SignedCharge>0,"Space restores actual positive polarity");
        input=1;yield return Until(()=>body.position.x>zone.bounds.max.x+.6f,8,"Actual D input leaves launch zone");input=0;yield return KeyTap(Key.E);
        Need(report.actualLaunches==0&&Mathf.Abs(launcher.Voltage-stored)<.001f&&!Prop<bool>(launcher,"CanLaunch"),"Outside-zone E rejects without consuming voltage");
        Need(Prop<string>(launcher,"LaunchBlockReason").Contains("영역")||Prop<string>(launcher,"LaunchBlockReason").Contains("전극"),"Outside-zone gives return guidance",Prop<string>(launcher,"LaunchBlockReason"));
        yield return DragLever(1);yield return KeyTap(Key.R);yield return Frames(5);
        var mover=Field<global::LeverMover>(selector.Lever.Reader,"lever");
        Need(Mathf.Abs(mover.Angle)<.001f&&Mathf.Abs(mover.Value)<.001f&&selector.Lever.Value==0&&launcher.Voltage==0,"R physically centers Stage5 lever and clears voltage");
        Need(!lesson.VoltageChallengeSatisfied&&!launcher.HasLaunchSample&&selector.Ball.SignedCharge>0&&session.RetryCount==0,"R restores original positive fresh attempt");
        begun=true;routeBegan=Time.fixedTime;phase="charging";driving=true;
        yield return DragLever(1);yield return Until(()=>launcher.Voltage>=50,8,"Real handle charges to required 50 V");yield return DragLever(0);
        Need(launcher.Voltage>=50&&launcher.Voltage<60,"Actual lever stops charging near target",launcher.Voltage.ToString());
        yield return Until(()=>Prop<bool>(launcher,"CanLaunch")&&Prop<Button>(guide,"LaunchButton").interactable,8,"Original bouncing ball is inside launch zone");
        ValidateLayout("ready");yield return Capture("ready");
        if(report.mode=="keyboard")yield return KeyTap(Key.E);else yield return Click(Prop<Button>(guide,"LaunchButton"));
        yield return Until(()=>lesson.VoltageChallengeSatisfied,3,"Actual at-target launch event and learning callback");
        phase="route";Need(report.actualLaunches==1&&launcher.LastLaunchVoltage>=50&&launcher.Voltage==0,"Successful launch consumes charge once and records pre-consumption voltage");
        Need(Mathf.Abs(report.launchSpeed-report.expectedLaunchSpeed)<.02f,"Actual launch speed follows sqrt(2qV/m)",report.launchSpeed+" vs "+report.expectedLaunchSpeed);
        yield return Capture("launched");yield return Until(()=>report.realFirstLanding&&report.realSecondLanding&&star.IsCollected,30,"Actual original two tower landings and star");
        Need(report.actualStar&&report.realFirstLanding&&report.realSecondLanding,"Both authored towers and natural star collection observed");
        if(report.mode=="recovery")
        {
            handlingRecovery=true;input=0;int retries=session.RetryCount;float previous=launcher.LastLaunchVoltage;int stars=selector.Ball.Stars;
            yield return KeyTap(Key.Backspace);yield return Frames(3);
            Need(session.RetryCount==retries+1&&lesson.VoltageChallengeSatisfied&&Mathf.Approximately(launcher.LastLaunchVoltage,previous)&&selector.Ball.Stars==stars&&star.IsCollected,"Backspace preserves successful voltage and earned star");
            handlingRecovery=false;
        }
        phase="exit";yield return Until(()=>goal.IsCleared,20,"Natural goal callback");driving=false;input=0;yield return Frames(8);
        report.retries=session.RetryCount;Need(report.retries==(report.mode=="recovery"?1:0),"Route has only explicitly requested recovery",report.retries.ToString());
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
'''
(root/'tmp/Stage5UxProbe.cs').write_text(prefix+body+utility,encoding='utf-8')
build=(root/'tmp/Stage4UxBuild.cs').read_text(encoding='utf-8-sig').replace('Stage4','Stage5').replace('"STAGE4_UX_QA", "STAGE3_UX_QA"','"STAGE5_UX_QA", "STAGE4_UX_QA", "STAGE3_UX_QA"')
(root/'tmp/Stage5UxBuild.cs').write_text(build,encoding='utf-8')
for src,out in [('RunStage4Ux.ps1','RunStage5Ux.ps1'),('RunStage3InStage4.ps1','RunStage3InStage5.ps1')]:
    text=(root/'tmp'/src).read_text(encoding='utf-8-sig').replace('Stage4','Stage5').replace('stage4','stage5')
    (root/'tmp'/out).write_text(text,encoding='utf-8')
text=(root/'tmp/RunStage4Ux.ps1').read_text(encoding='utf-8-sig').replace('Builds/Stage4UxQa','Builds/Stage5UxQa').replace('Logs/Stage4Guidance','Logs/Stage5Guidance')
(root/'tmp/RunStage4InStage5.ps1').write_text(text,encoding='utf-8')
print('Prepared Stage5 clone-only probe, build helper, and serial runners.')
