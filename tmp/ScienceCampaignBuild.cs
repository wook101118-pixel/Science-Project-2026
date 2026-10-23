// Temporary isolated-clone editor helper. QA code is compiled only for BuildQa.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;
using sung_wook;

public static class ScienceCampaignBuild
{
    const string Clone = "C:/Users/wook1/AppData/Local/Temp/CodexUnityVisualBuild";
    [Serializable] sealed class Audit
    {
        public string status = "running", startedUtc = DateTime.UtcNow.ToString("O"), finishedUtc, unity, project, output, result;
        public string[] scenes, extraDefines;
        public bool qa, developmentBuild = false;
        public int totalErrors, totalWarnings;
        public double buildSeconds;
        public List<string> errors = new List<string>();
    }
    public static void BuildQa() => Build(true);
    public static void BuildRelease() => Build(false);
    static void Build(bool qa)
    {
        var audit = new Audit { qa = qa, extraDefines = qa ? new[] { "SCIENCE_CAMPAIGN_QA" } : Array.Empty<string>() };
        string report = Clone + "/Logs/ScienceCampaign/" + (qa ? "QaBuild" : "ReleaseBuild") + ".json";
        try
        {
            audit.unity = Application.unityVersion; audit.project = Path.GetFullPath(Directory.GetParent(Application.dataPath).FullName).Replace('\\', '/');
            if (!Application.isBatchMode || audit.project != Clone) throw new InvalidOperationException("Use only the isolated clone.");
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Leave Play Mode before building.");
            audit.scenes = EditorBuildSettings.scenes.Where(value => value.enabled).Select(value => value.path).ToArray();
            if (audit.scenes.Length != 6 || audit.scenes[0] != ElectricGameCompleteView.MainMenuPath) throw new InvalidOperationException("Expected menu plus five saved campaign scenes.");
            for (int stage = 1; stage <= 5; stage++) if (audit.scenes[stage] != ElectricStageProgression.StageScenePath(stage) || !File.Exists(audit.scenes[stage])) throw new InvalidOperationException("Campaign scene missing or out of order: " + stage);
            PlayerSettings.runInBackground = true;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D11 });
            audit.output = Clone + "/Builds/ScienceCampaign" + (qa ? "Qa" : "Release") + "/ScienceProject.exe";
            Directory.CreateDirectory(Path.GetDirectoryName(audit.output));
            BuildReport build = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = audit.scenes, target = BuildTarget.StandaloneWindows64,
                locationPathName = audit.output, options = BuildOptions.None, extraScriptingDefines = audit.extraDefines });
            audit.result = build.summary.result.ToString(); audit.totalErrors = (int)build.summary.totalErrors;
            audit.totalWarnings = (int)build.summary.totalWarnings; audit.buildSeconds = build.summary.totalTime.TotalSeconds;
            foreach (BuildStep step in build.steps) foreach (BuildStepMessage message in step.messages)
                if (message.type == LogType.Error || message.type == LogType.Exception || message.type == LogType.Assert) audit.errors.Add(message.content);
            if (build.summary.result != BuildResult.Succeeded || audit.totalErrors > 0) throw new InvalidOperationException("Build failed: " + audit.result);
            audit.status = "passed";
        }
        catch (Exception error) { audit.status = "failed"; audit.errors.Add(error.ToString()); throw; }
        finally { audit.finishedUtc = DateTime.UtcNow.ToString("O"); Directory.CreateDirectory(Path.GetDirectoryName(report)); File.WriteAllText(report, JsonUtility.ToJson(audit, true)); }
    }
}
