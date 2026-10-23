// Temporary opt-in WindowsPlayer probe. Copy only into isolated clone Assets; no fake goal/clear calls.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using sung_wook;
using Object = UnityEngine.Object;

public sealed class QaBeginnerMenusProbe : MonoBehaviour
{
    [Serializable] private sealed class Check { public string name, details; public bool passed; }
    [Serializable] private sealed class Report {
        public string status="running", mode, startedUtc, finishedUtc; public int processId;
        public string scope="Fresh Windows executable. Authored menu/practice/help/pause button events and virtual InputSystem Esc. No goal completion/ShowStageResult fixture. Real completed result actions are verified by the separate campaign route probe.";
        public List<string> fixturePreferenceMutations=new List<string>(), screenshots=new List<string>(), errors=new List<string>();
        public List<Check> checks=new List<Check>();
    }
    private Report report; private string reportPath, mode; private bool ending; private float started;
    private Keyboard keyboard, previousKeyboard; private InputSettings.UpdateMode previousUpdate; private InputSettings.BackgroundBehavior previousBackground;
    private readonly List<string> errors=new List<string>();
    private const BindingFlags Fields=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot() {
        if(Application.isEditor)return;
        string mode=null; foreach(string arg in Environment.GetCommandLineArgs()) {
            if(arg=="--qa-beginner-menus")mode="locked";
            else if(arg.StartsWith("--qa-beginner-menus=",StringComparison.Ordinal))mode=arg.Substring("--qa-beginner-menus=".Length);
        }
        if(mode==null)return;
        var root=new GameObject("Temporary Beginner Menus QA"); DontDestroyOnLoad(root);
        var probe=root.AddComponent<QaBeginnerMenusProbe>();probe.mode=mode;probe.Initialize();
    }
    private static string Arg(string prefix){foreach(string arg in Environment.GetCommandLineArgs())if(arg.StartsWith(prefix,StringComparison.Ordinal))return arg.Substring(prefix.Length);return null;}
    private void Initialize(){
        reportPath=Arg("--qa-report=");started=Time.realtimeSinceStartup;
        report=new Report{mode=mode,startedUtc=DateTime.UtcNow.ToString("O"),processId=Process.GetCurrentProcess().Id};
        try{
            Require(Application.platform==RuntimePlatform.WindowsPlayer,"Windows standalone required.");
            Require(mode=="locked"||mode=="read","Use locked/default or read mode.");
            Require(!string.IsNullOrEmpty(reportPath)&&Path.IsPathRooted(reportPath),"Absolute --qa-report required.");
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
            if(mode=="locked"){
                report.fixturePreferenceMutations.Add("Explicit first-run fixture: DeleteKey("+ElectricStageProgression.UnlockKey+"). No Mastery or Plan keys are changed.");
                PlayerPrefs.DeleteKey(ElectricStageProgression.UnlockKey);PlayerPrefs.Save();
            }
            report.fixturePreferenceMutations.Add("Real Screen.SetResolution(640x480 and 1600x900 Windowed) is observed by production display preferences and can write its Width/Height/Fullscreen keys. Parent restores the complete original registry after all runs.");
            Application.runInBackground=true;Application.logMessageReceived+=OnLog;
            previousKeyboard=Keyboard.current;previousUpdate=InputSystem.settings.updateMode;previousBackground=InputSystem.settings.backgroundBehavior;
            InputSystem.settings.updateMode=InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
            keyboard=InputSystem.AddDevice<Keyboard>();keyboard.MakeCurrent();
            StartCoroutine(Guarded(Run()));
        }catch(Exception error){Finish(error);}
    }
    private IEnumerator Run(){
        yield return Ready("MainMenu");
        CheckResult("efficiency-display-boundary-50-succeeds",ElectricStageChallenge2D.IsEfficientLaunchVoltage(50f),"Raw 50 V satisfies the unchanged minimum.");
        CheckResult("efficiency-display-boundary-55-005-succeeds",ElectricStageChallenge2D.IsEfficientLaunchVoltage(55.0050468f),"The actual alternative-route voltage displays 55.0 V and earns the same efficiency challenge.");
        CheckResult("efficiency-display-boundary-55-1-fails",!ElectricStageChallenge2D.IsEfficientLaunchVoltage(55.1f),"Displayed 55.1 V exceeds the efficiency challenge.");
        CheckResult("efficiency-display-boundary-below-50-fails",!ElectricStageChallenge2D.IsEfficientLaunchVoltage(49.999f),"Rounding cannot bypass the unchanged raw 50 V minimum.");
        float deadline=Time.realtimeSinceStartup+12f;while(!UnityEngine.Rendering.SplashScreen.isFinished&&Time.realtimeSinceStartup<deadline)yield return null;
        yield return Delay(.4f);
        MainMenuController menu=Find<MainMenuController>();Click(menu,"practiceButton");
        CheckResult("authored-practice-button-opens-window",menu.IsPracticeOpen&&!menu.IsSettingsOpen,"Saved practice button opens the saved practice window.");
        Button[] stageButtons=Ref<Button[]>(menu,"stageButtons");Require(stageButtons.Length==5,"Five saved stage buttons required.");
        if(mode=="locked"){
            CheckResult("first-run-unlocks-tutorial-only",ElectricStageProgression.HighestUnlockedStage==1&&stageButtons[0].interactable,"Unlock fixture leaves tutorial available.");
            for(int index=1;index<5;index++){
                CheckResult("stage-"+(index+1)+"-button-displays-lock",!stageButtons[index].interactable&&stageButtons[index].GetComponentInChildren<Text>(true).text.Contains("잠김"),"Locked saved UI is disabled and explicitly labelled.");
                CheckResult("stage-"+(index+1)+"-direct-api-rejects-lock",!menu.PracticeStage(index+1)&&!SceneTransitionController.IsBusy,"Public practice command also guards locked stages.");
            }
            int highest=ElectricStageProgression.HighestUnlockedStage;ElectricStageProgression.UnlockStage(0);ElectricStageProgression.UnlockStage(6);
            CheckResult("invalid-unlock-does-not-widen-stage-range",ElectricStageProgression.HighestUnlockedStage==highest&&!menu.PracticeStage(6),"Stage range remains 1..5; invalid unlock calls do not persist a new value.");
        }else{
            CheckResult("fresh-process-restores-real-final-unlock",ElectricStageProgression.HighestUnlockedStage==5&&PlayerPrefs.GetInt(ElectricStageProgression.UnlockKey,-1)==5,"Read mode seeds no progress keys; actual preceding campaign must have unlocked stage 5.");
            for(int stage=2;stage<=5;stage++){
                int flags=PlayerPrefs.GetInt("ElectricBounce.Mastery.Stage"+stage+".Flags",0);
                CheckResult("real-mastery-stage-"+stage+"-persists",flags!=0&&PlayerPrefs.GetInt("ElectricBounce.Mastery.Stage"+stage+".FewestRetries",-1)>=0,"Saved Mastery flags="+flags);
            }
            foreach(int stage in new[]{2,3,5})CheckResult("real-plan-stage-"+stage+"-both-persist",PlayerPrefs.GetInt("ElectricBounce.Plan.Stage"+stage+".Flags",0)==3,"Both actually completed plan choices survive a new executable.");
            CheckResult("real-stage5-efficiency-challenge-persists",(PlayerPrefs.GetInt("ElectricBounce.Mastery.Stage5.Flags",0)&2)!=0,"Actual successful 50/55 V routes preserve the efficiency challenge flag in a new executable.");
            CheckResult("unlocked-practice-buttons-enabled",Array.TrueForAll(stageButtons,b=>b!=null&&b.interactable),"All five legitimately unlocked stages are selectable.");
        }
        foreach(Vector2Int size in new[]{new Vector2Int(640,480),new Vector2Int(1600,900)}){
            Screen.SetResolution(size.x,size.y,FullScreenMode.Windowed);yield return SizeReady(size);
            CheckVisibleLayout(Ref<GameObject>(menu,"practiceWindow"),"practice-"+size.x);
            yield return Capture("practice-"+size.x);
        }
        DispatchEscape(menu);yield return null;
        CheckResult("practice-escape-returns-main",!menu.IsPracticeOpen&&!SceneTransitionController.IsBusy,"Esc closes practice without loading a stage.");
        if(mode=="read"){
            Click(menu,"practiceButton");ClickStage(menu,5);yield return Ready("Stage_05");
            CheckResult("saved-stage5-practice-button-loads-stage5",Find<ElectricStageSelector>().SelectedStage==5,"The desired stage loads directly through the saved practice button.");
            var progression=Find<ElectricStageProgression>();progression.ContinueNextStage();progression.RetryCurrentStage();
            CheckResult("hidden-result-actions-do-not-load-scenes",!SceneTransitionController.IsBusy&&!progression.BlocksGameplayInput,"Results cannot be skipped or replayed before a real clear.");
            CollapsiblePanel help=Find<CollapsiblePanel>();Click(help,"toggleButton");yield return Delay(.4f);
            PauseMenuController pause=Find<PauseMenuController>();
            CheckResult("help-opens-in-safe-reading-pause",help.IsOpen&&pause.IsPaused&&Time.timeScale==0f&&!Ref<GameObject>(pause,"menuRoot").activeSelf,"Actual help button uses reading pause while leaving the help content visible.");
            yield return Capture("stage5-help-reading");
            DispatchEscape(pause);yield return null;yield return null;
            CheckResult("help-escape-closes-and-resumes",!help.IsOpen&&!pause.IsPaused&&Time.timeScale==1f,"Esc ends reading pause and closes the card.");
            DispatchEscape(pause);yield return null;Click(pause,"settingsButton");
            CheckResult("practice-pause-settings-remain-available",pause.IsPaused&&pause.IsSettingsOpen&&Find<SfxSettingsView>()!=null&&Find<DisplaySettingsView>()!=null,"Normal shared settings are usable after direct stage practice.");
            DispatchEscape(pause);yield return null;
            CheckResult("settings-escape-stays-paused",pause.IsPaused&&!pause.IsSettingsOpen&&Time.timeScale==0f,"Esc returns to pause without unpausing.");
            Click(pause,"mainMenuButton");yield return Ready("MainMenu");menu=Find<MainMenuController>();
        }
        Click(menu,"startButton");yield return Ready("Stage_01");
        CheckResult("new-campaign-starts-real-tutorial",Find<ElectricStageSelector>().SelectedStage==1&&Find<TutorialController2D>()!=null,"Authored start now begins the real Stage01 tutorial.");
        if(mode=="read")CheckResult("new-campaign-preserves-unlocked-practice",ElectricStageProgression.HighestUnlockedStage==5,"A new campaign does not erase previously opened practice stages.");
        CheckResult("no-unexpected-runtime-errors",errors.Count==0,string.Join("\n",errors));Finish(null);
    }
    private static void ClickStage(MainMenuController menu,int stage)=>Ref<Button[]>(menu,"stageButtons")[stage-1].onClick.Invoke();
    private void CheckVisibleLayout(GameObject root,string label){
        Canvas.ForceUpdateCanvases();int count=0;var problems=new List<string>();
        foreach(Text text in root.GetComponentsInChildren<Text>(false)){
            if(!text.isActiveAndEnabled||string.IsNullOrWhiteSpace(text.text))continue;count++;
            if(Outside(text.rectTransform,text.canvas))problems.Add(text.name+" outside screen");
            if(text.verticalOverflow==VerticalWrapMode.Truncate&&text.preferredHeight>text.rectTransform.rect.height+1f)problems.Add(text.name+" truncates");
        }
        foreach(TMP_Text text in root.GetComponentsInChildren<TMP_Text>(false)){
            if(!text.isActiveAndEnabled||string.IsNullOrWhiteSpace(text.text))continue;count++;
            if(Outside(text.rectTransform,text.canvas)||text.isTextOverflowing)problems.Add(text.name+" outside/overflow");
        }
        CheckResult(label+"-saved-window-text-fits",count>=7&&problems.Count==0,"Visible labels="+count+"; "+string.Join(", ",problems));
    }
    private static bool Outside(RectTransform rect,Canvas canvas){
        Vector3[] corners=new Vector3[4];rect.GetWorldCorners(corners);Camera camera=canvas!=null&&canvas.renderMode!=RenderMode.ScreenSpaceOverlay?canvas.worldCamera:null;
        foreach(Vector3 corner in corners){Vector2 point=RectTransformUtility.WorldToScreenPoint(camera,corner);if(point.x<-.5f||point.y<-.5f||point.x>Screen.width+.5f||point.y>Screen.height+.5f)return true;}return false;
    }
    private static IEnumerator SizeReady(Vector2Int size){float deadline=Time.realtimeSinceStartup+8f;while((Screen.width!=size.x||Screen.height!=size.y||Screen.fullScreen)&&Time.realtimeSinceStartup<deadline)yield return null;Require(Screen.width==size.x&&Screen.height==size.y&&!Screen.fullScreen,"Native size failed.");yield return Delay(.5f);}
    private static IEnumerator Ready(string scene){float deadline=Time.realtimeSinceStartup+30f;while((SceneManager.GetActiveScene().name!=scene||SceneTransitionController.IsBusy||!SceneTransitionController.IsAvailable)&&Time.realtimeSinceStartup<deadline)yield return null;Require(SceneManager.GetActiveScene().name==scene&&!SceneTransitionController.IsBusy,"Scene failed: "+scene);yield return null;}
    private static IEnumerator Delay(float seconds){float deadline=Time.realtimeSinceStartup+seconds;while(Time.realtimeSinceStartup<deadline)yield return null;}
    private IEnumerator Capture(string label){yield return new WaitForEndOfFrame();Texture2D image=ScreenCapture.CaptureScreenshotAsTexture();Require(image!=null,"No screenshot.");string path=Path.Combine(Path.GetDirectoryName(reportPath),Path.GetFileNameWithoutExtension(reportPath)+"-"+label+".png");File.WriteAllBytes(path,image.EncodeToPNG());Object.Destroy(image);report.screenshots.Add(path);Flush();}
    private static T Find<T>()where T:Object=>Object.FindFirstObjectByType<T>();
    private static T Ref<T>(object target,string name)where T:class{FieldInfo field=target.GetType().GetField(name,Fields);T value=field?.GetValue(target)as T;Require(value!=null,"Missing "+name);return value;}
    private static void Click(object target,string name)=>Ref<Button>(target,name).onClick.Invoke();
    private void DispatchEscape(MonoBehaviour target){InputSystem.QueueStateEvent(keyboard,new KeyboardState());Pump();InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Escape));Pump();target.SendMessage("Update",SendMessageOptions.RequireReceiver);InputSystem.QueueStateEvent(keyboard,new KeyboardState());Pump();}
    private static void Pump()=>typeof(InputSystem).GetMethod("Update",BindingFlags.Static|BindingFlags.NonPublic,null,new[]{typeof(InputUpdateType)},null).Invoke(null,new object[]{InputUpdateType.Dynamic});
    private void OnLog(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(message+"\n"+stack);}
    private void CheckResult(string name,bool passed,string details){report.checks.Add(new Check{name=name,passed=passed,details=details});Flush();Require(passed,name+": "+details);}
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private void Flush(){if(!string.IsNullOrEmpty(reportPath))File.WriteAllText(reportPath,JsonUtility.ToJson(report,true));}
    private IEnumerator Guarded(IEnumerator sequence){var stack=new Stack<IEnumerator>();stack.Push(sequence);while(stack.Count>0&&!ending){bool next=false;object value=null;Exception error=null;try{next=stack.Peek().MoveNext();if(next)value=stack.Peek().Current;}catch(Exception caught){error=caught;}if(error!=null){Finish(error);yield break;}if(!next)stack.Pop();else if(value is IEnumerator nested)stack.Push(nested);else yield return value;}}
    private void Update(){if(!ending&&report!=null&&Time.realtimeSinceStartup-started>180f)Finish(new TimeoutException("Beginner menus QA exceeded 180s."));}
    private void Finish(Exception error){
        if(ending)return;ending=true;if(error!=null)report.errors.Add(error.ToString());report.errors.AddRange(errors);
        report.status=report.errors.Count==0&&!report.checks.Exists(c=>!c.passed)?"passed":"failed";report.finishedUtc=DateTime.UtcNow.ToString("O");Flush();
        Application.logMessageReceived-=OnLog;if(keyboard!=null&&keyboard.added)InputSystem.RemoveDevice(keyboard);if(previousKeyboard!=null&&previousKeyboard.added)previousKeyboard.MakeCurrent();
        InputSystem.settings.updateMode=previousUpdate;InputSystem.settings.backgroundBehavior=previousBackground;Application.Quit(report.status=="passed"?0:1);
    }
}
