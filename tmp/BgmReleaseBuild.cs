// Temporary Windows release builder. Compile/run only in the isolated clone Assets/Editor.
// May be called directly with -batchmode -quit -executeMethod BgmReleaseBuild.Build,
// or after BgmSettingsCapture.RunBatchAndBuild has left Play Mode.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;
using sung_wook;

public static class BgmReleaseBuild
{
    private const string Clone = "C:/Users/wook1/AppData/Local/Temp/CodexUnityVisualBuild";

    [Serializable] private sealed class Audit
    {
        public string status = "running";
        public string startedUtc = DateTime.UtcNow.ToString("O");
        public string finishedUtc;
        public string unity;
        public string project;
        public string output;
        public string result;
        public string[] scenes;
        public bool developmentBuild;
        public int totalErrors;
        public int totalWarnings;
        public double buildSeconds;
        public List<string> errors = new List<string>();
    }

    public static void Build()
    {
        string reportPath = Clone + "/Logs/BgmSettings/Build.json";
        var audit = new Audit();
        try
        {
            audit.unity = Application.unityVersion;
            audit.project = Path.GetFullPath(Directory.GetParent(Application.dataPath).FullName).Replace('\\', '/');
            if (!Application.isBatchMode || audit.project != Clone)
                throw new InvalidOperationException("BGM release builds must use the isolated validation clone.");
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Leave Play Mode before building the BGM release.");
            audit.scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
            if (audit.scenes.Length != 6 || audit.scenes[0] != ElectricGameCompleteView.MainMenuPath)
                throw new InvalidOperationException("The release must contain the main menu followed by the five current campaign stages.");
            for (int stage = 1; stage <= 5; stage++)
                if (audit.scenes[stage] != ElectricStageProgression.StageScenePath(stage) || !File.Exists(audit.scenes[stage]))
                    throw new InvalidOperationException("An enabled campaign scene is missing or out of order: " + stage);
            if (!File.Exists(audit.scenes[0])) throw new InvalidOperationException("The enabled main menu file is missing.");

            PlayerSettings.runInBackground = true;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D11 });
            audit.output = Clone + "/Builds/BgmSettingsRelease/ScienceProject.exe";
            Directory.CreateDirectory(Path.GetDirectoryName(audit.output));
            BuildReport build = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = audit.scenes,
                target = BuildTarget.StandaloneWindows64,
                locationPathName = audit.output,
                options = BuildOptions.None
            });
            audit.result = build.summary.result.ToString();
            audit.totalErrors = (int)build.summary.totalErrors;
            audit.totalWarnings = (int)build.summary.totalWarnings;
            audit.buildSeconds = build.summary.totalTime.TotalSeconds;
            foreach (BuildStep step in build.steps)
                foreach (BuildStepMessage message in step.messages)
                    if (message.type == LogType.Error || message.type == LogType.Exception || message.type == LogType.Assert)
                        audit.errors.Add(message.content);
            if (build.summary.result != BuildResult.Succeeded || audit.totalErrors != 0)
                throw new InvalidOperationException("BGM Windows release build failed: " + audit.result);
            audit.status = "passed";
            Debug.Log("BGM settings Windows release built: " + audit.output);
        }
        catch (Exception error)
        {
            audit.status = "failed";
            audit.errors.Add(error.ToString());
            throw;
        }
        finally
        {
            audit.finishedUtc = DateTime.UtcNow.ToString("O");
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
            File.WriteAllText(reportPath, JsonUtility.ToJson(audit, true));
        }
    }
}
