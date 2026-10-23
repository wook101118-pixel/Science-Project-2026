using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using sung_wook;

// Clone-only diagnostics build. It reads existing scenes and does not alter product settings.
public static class ResumeDemoReplayBuild
{
    private const string Clone = "C:/Users/wook1/AppData/Local/Temp/CodexUnityVisualBuild";
    private const string Task = "C:/Users/wook1/GitBub-Unity/Science-Project-2026";
    [Serializable] private sealed class Audit
    {
        public string status = "running", startedUtc = DateTime.UtcNow.ToString("O"), finishedUtc;
        public string project, output, result, error;
        public string[] scenes;
        public int totalErrors, totalWarnings;
        public double buildSeconds;
    }
    public static void Build()
    {
        var audit = new Audit();
        try
        {
            audit.project = Path.GetFullPath(Directory.GetParent(Application.dataPath).FullName).Replace('\\', '/');
            if (!Application.isBatchMode || audit.project != Clone) throw new InvalidOperationException("Wrong isolated build project.");
            audit.output = Task + "/Builds/ResumeDemoReplay/ScienceProject.exe";
            audit.scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
            if (audit.scenes.Length != 6 || audit.scenes[0] != ElectricGameCompleteView.MainMenuPath)
                throw new InvalidOperationException("Expected main menu and five campaign scenes.");
            for (int stage = 1; stage <= 5; stage++)
                if (!audit.scenes.Contains(ElectricStageProgression.StageScenePath(stage)))
                    throw new InvalidOperationException("Missing campaign stage " + stage);
            Directory.CreateDirectory(Path.GetDirectoryName(audit.output));
            BuildReport build = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = audit.scenes, locationPathName = audit.output,
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.None
            });
            audit.result = build.summary.result.ToString();
            audit.totalErrors = (int)build.summary.totalErrors;
            audit.totalWarnings = (int)build.summary.totalWarnings;
            audit.buildSeconds = build.summary.totalTime.TotalSeconds;
            if (build.summary.result != BuildResult.Succeeded || audit.totalErrors != 0)
                throw new InvalidOperationException("Replay build failed: " + audit.result);
            audit.status = "passed";
        }
        catch (Exception error) { audit.status = "failed"; audit.error = error.ToString(); throw; }
        finally
        {
            audit.finishedUtc = DateTime.UtcNow.ToString("O");
            File.WriteAllText(Task + "/Logs/ResumeDemoReplayBuild.json", JsonUtility.ToJson(audit, true));
        }
    }
}

