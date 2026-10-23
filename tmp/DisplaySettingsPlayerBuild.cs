// Temporary build utility. Copy only into the isolated validation clone's Assets/Editor.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

public static class DisplaySettingsPlayerBuild
{
    private const string CloneRoot = "C:/Users/wook1/AppData/Local/Temp/CodexUnityVisualBuild";
    private const string ReportPath = "C:/Users/wook1/GitBub-Unity/Science-Project-2026/Logs/DisplaySettingsPlayerBuildReport.json";

    [Serializable]
    private sealed class BuildAudit
    {
        public string status = "running";
        public string startedUtc = DateTime.UtcNow.ToString("O");
        public string finishedUtc;
        public string unityVersion;
        public string project;
        public string output;
        public string target = "StandaloneWindows64";
        public string graphicsApi = "Direct3D11";
        public bool developmentBuild = true;
        public bool runInBackground;
        public string companyName;
        public string productName;
        public string result;
        public string totalBytes;
        public double durationSeconds;
        public int totalErrors;
        public int totalWarnings;
        public string[] scenes = Array.Empty<string>();
        public List<string> errors = new List<string>();
        public string note = "Builds existing enabled scenes with an opt-in development-only runtime probe. Only clone PlayerSettings are changed. Source assets, company/product identity, and visual resources are preserved.";
    }

    public static void Build()
    {
        var audit = new BuildAudit();
        try
        {
            string project = Normalize(Directory.GetParent(Application.dataPath).FullName);
            audit.project = project;
            audit.unityVersion = Application.unityVersion;
            if (!Application.isBatchMode || !string.Equals(project, Normalize(CloneRoot), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Standalone validation builds must run in the exact isolated CodexUnityVisualBuild clone, in batch mode.");

            string output = ReadArgument("-displayBuildOutput") ?? Path.Combine(CloneRoot, "Builds/DisplaySettingsValidation/Scince.exe");
            output = Normalize(output);
            string allowedRoot = Normalize(Path.Combine(CloneRoot, "Builds")) + "/";
            if (!output.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetExtension(output), ".exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Validation executable output must be an .exe inside the clone's Builds directory.");
            audit.output = output;
            audit.scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
            if (audit.scenes.Length == 0)
                throw new InvalidOperationException("No enabled build scenes are configured.");
            foreach (string scene in audit.scenes)
                if (!File.Exists(scene)) throw new FileNotFoundException("Enabled build scene is missing.", scene);

            audit.companyName = PlayerSettings.companyName;
            audit.productName = PlayerSettings.productName;
            audit.developmentBuild = Array.IndexOf(Environment.GetCommandLineArgs(), "-qaReleaseBuild") < 0;
            PlayerSettings.runInBackground = true;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D11 });
            audit.runInBackground = PlayerSettings.runInBackground;
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory(Path.GetDirectoryName(output));

            BuildReport build = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = audit.scenes,
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                options = audit.developmentBuild ? BuildOptions.Development : BuildOptions.None
            });
            if (build == null) throw new InvalidOperationException("BuildPipeline returned no report.");
            BuildSummary summary = build.summary;
            audit.result = summary.result.ToString();
            audit.totalBytes = summary.totalSize.ToString();
            audit.durationSeconds = summary.totalTime.TotalSeconds;
            audit.totalErrors = (int)summary.totalErrors;
            audit.totalWarnings = (int)summary.totalWarnings;
            foreach (BuildStep step in build.steps)
                foreach (BuildStepMessage message in step.messages)
                    if (message.type == LogType.Error || message.type == LogType.Exception || message.type == LogType.Assert)
                        audit.errors.Add(step.name + ": " + message.content);

            if (summary.result != BuildResult.Succeeded || summary.totalErrors != 0 || !File.Exists(output))
                throw new InvalidOperationException("Windows standalone build failed: " + summary.result + ", errors=" + summary.totalErrors + ".");
            if (PlayerSettings.companyName != audit.companyName || PlayerSettings.productName != audit.productName)
                throw new InvalidOperationException("Build unexpectedly changed the player preference identity.");
            audit.status = "passed";
        }
        catch (Exception exception)
        {
            audit.status = "failed";
            audit.errors.Add(exception.ToString());
            throw;
        }
        finally
        {
            audit.finishedUtc = DateTime.UtcNow.ToString("O");
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, JsonUtility.ToJson(audit, true));
            Debug.Log("Standalone display build audit: " + audit.status + " (" + ReportPath + ")");
        }
    }

    private static string ReadArgument(string name)
    {
        string[] arguments = Environment.GetCommandLineArgs();
        for (int index = 0; index + 1 < arguments.Length; index++)
            if (string.Equals(arguments[index], name, StringComparison.OrdinalIgnoreCase)) return arguments[index + 1];
        return null;
    }

    private static string Normalize(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/');
}
