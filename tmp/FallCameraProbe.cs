#if STAGE5_UX_QA
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using sung_wook;

[DefaultExecutionOrder(-1100)]
public sealed class FallCameraProbe : MonoBehaviour
{
    [Serializable] sealed class Report { public string status="running",error=""; public List<string> checks=new List<string>(); public List<string> screenshots=new List<string>(); public List<string> falls=new List<string>(); public bool progressRestored; }
    Report report=new Report(); string path; Keyboard keyboard; Key activeKey; bool pressed,finished; QaProgressPreferences progress;
    InputSettings.UpdateMode oldMode; InputSettings.BackgroundBehavior oldBackground;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
        if(Application.isEditor||!Environment.GetCommandLineArgs().Contains("--qa-fall-camera"))return;
        string arg=Environment.GetCommandLineArgs().First(a=>a.StartsWith("--qa-report="));
        var go=new GameObject("Fall camera QA"); DontDestroyOnLoad(go);go.AddComponent<FallCameraProbe>().Begin(arg.Substring(12));
    }
    void Begin(string output)
    {
        path=output;progress=QaProgressPreferences.Capture(path+".ProgressBackup.json");
        Application.runInBackground=true;Application.targetFrameRate=60;QualitySettings.vSyncCount=0;
        oldMode=InputSystem.settings.updateMode;oldBackground=InputSystem.settings.backgroundBehavior;
        InputSystem.settings.updateMode=InputSettings.UpdateMode.ProcessEventsManually;
        InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
        keyboard=InputSystem.AddDevice<Keyboard>("FallCameraKeyboard");keyboard.MakeCurrent();
        StartCoroutine(Guard(Run()));
    }
    void Update(){if(finished||keyboard==null)return;InputSystem.QueueStateEvent(keyboard,new KeyboardState(pressed?new[]{activeKey}:Array.Empty<Key>()));InputSystem.Update();}
    IEnumerator Run()
    {
        for(int stage=1;stage<=5;stage++)
        {
            yield return SceneManager.LoadSceneAsync(ElectricStageProgression.StageScenePath(stage));yield return new WaitForSecondsRealtime(1);
            var selector=FindFirstObjectByType<ElectricStageSelector>();var camera=Camera.main;
            Need(camera!=null&&camera.orthographic&&Mathf.Approximately(camera.orthographicSize,10),"Stage "+stage+" orthographic size 10");
            Need(camera.GetComponents<MonoBehaviour>().Any(c=>c.GetType().Name=="CinemachineBrain"&&c.enabled),"Stage "+stage+" enabled Cinemachine brain");
            var follow=FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).Single(c=>c.GetType().Name=="CinemachineCamera");
            var target=follow.GetType().GetField("Target").GetValue(follow);
            Need((Transform)target.GetType().GetField("TrackingTarget").GetValue(target)==selector.Ball.transform,"Stage "+stage+" follows its own player");
            var composer=follow.GetComponents<MonoBehaviour>().Single(c=>c.GetType().Name=="CinemachinePositionComposer");
            Need((Vector3)composer.GetType().GetField("Damping").GetValue(composer)==new Vector3(1,1.5f,1),"Stage "+stage+" Stage1 damping");
            Need(follow.GetComponents<MonoBehaviour>().Any(c=>c.GetType().Name=="CinemachineConfiner2D"&&c.enabled),"Stage "+stage+" camera bounds enabled");
            yield return Capture("camera-stage"+stage);
        }
        foreach(int stage in new[]{2,5})
        {
            yield return SceneManager.LoadSceneAsync(ElectricStageProgression.StageScenePath(stage));yield return new WaitForSecondsRealtime(.5f);
            var selector=FindFirstObjectByType<ElectricStageSelector>();var session=selector.GetComponent<ElectricStageSession2D>();
            int retries=session.RetryCount;Vector2 start=selector.Ball.Body.position;activeKey=Key.A;pressed=true;
            yield return Until(()=>session.IsDead,20,"natural fall stage "+stage);pressed=false;yield return null;
            var result=FindFirstObjectByType<ElectricGameCompleteView>();
            Need(result!=null&&result.IsDeathOpen&&Time.timeScale==0,"Stage "+stage+" immediate death panel");
            Need(!selector.Ball.Body.simulated&&session.RetryCount==retries&&!session.CanRetryCheckpoint,"Stage "+stage+" frozen death without auto recovery");
            Need(selector.Ball.Body.position.y<session.FallDeathY&&selector.Ball.Body.position.x<start.x-1,"Stage "+stage+" fell with actual left input");
            report.falls.Add("Stage "+stage+": boundary="+session.FallDeathY+", position="+selector.Ball.Body.position);
            yield return Capture("death-stage"+stage);
            if(stage==2)
            {
                int handle=SceneManager.GetActiveScene().handle;yield return Tap(Key.R);
                yield return Until(()=>SceneManager.GetActiveScene().handle!=handle&&!SceneTransitionController.IsBusy,12,"R restarts current stage");
                yield return new WaitForSecondsRealtime(.5f);session=FindFirstObjectByType<ElectricStageSession2D>();
                Need(session!=null&&!session.IsDead&&session.RetryCount==0&&Time.timeScale>0,"R creates fresh live stage");
            }
            else
            {
                yield return Tap(Key.Escape);
                yield return Until(()=>SceneManager.GetActiveScene().path==ElectricGameCompleteView.MainMenuPath&&!SceneTransitionController.IsBusy,12,"death Esc returns to menu");
                Need(Time.timeScale>0,"death menu restores time scale");yield return Capture("death-menu");
            }
        }
        Finish(null);
    }
    IEnumerator Tap(Key key){activeKey=key;pressed=true;yield return null;yield return null;pressed=false;yield return null;}
    IEnumerator Until(Func<bool> condition,float seconds,string label){float end=Time.realtimeSinceStartup+seconds;while(!condition()){if(Time.realtimeSinceStartup>end)throw new Exception(label);yield return null;}}
    IEnumerator Capture(string name){yield return new WaitForEndOfFrame();string image=Path.Combine(Path.GetDirectoryName(path),"Final-"+name+".png");ScreenCapture.CaptureScreenshot(image);yield return new WaitForSecondsRealtime(.15f);report.screenshots.Add(image);}
    void Need(bool value,string label){if(!value)throw new Exception(label);report.checks.Add(label);}
    IEnumerator Guard(IEnumerator routine)
    {
        var stack=new Stack<IEnumerator>();stack.Push(routine);
        while(stack.Count>0){object next;try{var top=stack.Peek();if(!top.MoveNext()){stack.Pop();continue;}next=top.Current;}catch(Exception e){Finish(e);yield break;}if(next is IEnumerator nested)stack.Push(nested);else yield return next;}
    }
    void Finish(Exception error)
    {
        if(finished)return;finished=true;pressed=false;
        try{progress.Restore();report.progressRestored=true;}catch(Exception e){error=e;}
        InputSystem.settings.updateMode=oldMode;InputSystem.settings.backgroundBehavior=oldBackground;if(keyboard!=null)InputSystem.RemoveDevice(keyboard);
        report.status=error==null?"passed":"failed";report.error=error?.ToString()??"";Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,JsonUtility.ToJson(report,true));Application.Quit(error==null?0:1);
    }
}
#endif
