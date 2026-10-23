using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

public static class BeginnerReleaseBuild
{
    private const string Clone = "C:/Users/wook1/AppData/Local/Temp/CodexUnityVisualBuild";
    private const string Task = "C:/Users/wook1/GitBub-Unity/Science-Project-2026";
    [Serializable] private sealed class Audit
    {
        public string status = "running", startedUtc = DateTime.UtcNow.ToString("O"), finishedUtc;
        public string unity, project, output, result;
        public bool developmentBuild;
        public string[] scenes, errors;
        public int totalErrors, totalWarnings;
        public double buildSeconds;
    }
    public static void Build()
    {
        var audit = new Audit();
        string report = Task + "/Logs/Beginner_BuildReport.json";
        try
        {
            audit.project = Path.GetFullPath(Directory.GetParent(Application.dataPath).FullName).Replace('\\', '/');
            if (!Application.isBatchMode || audit.project != Clone) throw new InvalidOperationException("Wrong isolated build project.");
            audit.unity = Application.unityVersion;
            audit.output = Clone + "/Builds/BeginnerRelease/Scince.exe";
            audit.scenes = EditorBuildSettings.scenes.Where(x => x.enabled).Select(x => x.path).ToArray();
            for (int stage = 1; stage <= 5; stage++)
                if (!audit.scenes.Contains("Assets/@Project/@sung_wook/@Game/00_Scenes/Stage/Stage_" + stage.ToString("00") + ".unity"))
                    throw new InvalidOperationException("Campaign stage missing from build: " + stage);
            PlayerSettings.runInBackground = true;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D11 });
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory(Path.GetDirectoryName(audit.output));
            BuildReport build = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = audit.scenes, locationPathName = audit.output,
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.None
            });
            audit.result = build.summary.result.ToString();
            audit.buildSeconds = build.summary.totalTime.TotalSeconds;
            audit.totalErrors = (int)build.summary.totalErrors;
            audit.totalWarnings = (int)build.summary.totalWarnings;
            if (build.summary.result != BuildResult.Succeeded || audit.totalErrors != 0)
                throw new InvalidOperationException("Windows build failed: " + audit.result);
            audit.status = "passed";
        }
        catch (Exception error) { audit.status = "failed"; audit.errors = new[] { error.ToString() }; throw; }
        finally
        {
            audit.finishedUtc = DateTime.UtcNow.ToString("O");
            Directory.CreateDirectory(Path.GetDirectoryName(report));
            File.WriteAllText(report, JsonUtility.ToJson(audit, true));
        }
    }
}
