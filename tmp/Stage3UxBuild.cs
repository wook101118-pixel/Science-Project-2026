// Clone-only build helper. Never opens or saves authored scenes/prefabs.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;
using sung_wook;

public static class Stage3UxBuild
{
    const string Clone = "C:/Users/wook1/AppData/Local/Temp/CodexUnityVisualBuild";
    const string Source = "C:/Users/wook1/GitBub-Unity/Science-Project-2026";
    [Serializable] sealed class AssetHash { public string path = "", sha256 = ""; }
    [Serializable] sealed class Hashes { public string capturedUtc = ""; public AssetHash[] assets = Array.Empty<AssetHash>(); }
    [Serializable] sealed class Audit
    {
        public string status = "running", startedUtc = DateTime.UtcNow.ToString("O"), finishedUtc, output;
        public bool qa, sourceMapHashesUnchanged, cloneMapHashesUnchanged;
        public string[] scenes, defines;
        public int errors, warnings;
        public double seconds;
        public List<string> failures = new List<string>();
    }
    public static void BuildQa() => Build(true);
    public static void BuildRelease() => Build(false);
    static void Build(bool qa)
    {
        var audit = new Audit { qa = qa, defines = qa ? new[] { "STAGE3_UX_QA" } : Array.Empty<string>() };
        string report = Source + "/Logs/Stage3Guidance/" + (qa ? "QaBuild" : "ReleaseBuild") + ".json";
        try
        {
            string actual = Path.GetFullPath(Directory.GetParent(Application.dataPath).FullName).Replace('\\', '/');
            if (!Application.isBatchMode || actual != Clone || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Build only in the idle isolated clone.");
            VerifyMapHashes(audit);
            audit.scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (audit.scenes.Length != 6 || audit.scenes[0] != ElectricGameCompleteView.MainMenuPath)
                throw new InvalidOperationException("Expected main menu plus five saved stages.");
            for (int stage = 1; stage <= 5; stage++)
                if (audit.scenes[stage] != ElectricStageProgression.StageScenePath(stage))
                    throw new InvalidOperationException("Wrong saved scene order.");
            PlayerSettings.runInBackground = true;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D11 });
            audit.output = Clone + "/Builds/Stage3Ux" + (qa ? "Qa" : "Release") + "/ScienceProject.exe";
            Directory.CreateDirectory(Path.GetDirectoryName(audit.output));
            var build = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = audit.scenes,
                target = BuildTarget.StandaloneWindows64, locationPathName = audit.output,
                options = BuildOptions.None, extraScriptingDefines = audit.defines });
            audit.errors = (int)build.summary.totalErrors; audit.warnings = (int)build.summary.totalWarnings;
            audit.seconds = build.summary.totalTime.TotalSeconds;
            foreach (var step in build.steps) foreach (var message in step.messages)
                if (message.type == LogType.Error || message.type == LogType.Exception) audit.failures.Add(message.content);
            VerifyMapHashes(audit);
            if (build.summary.result != BuildResult.Succeeded || audit.errors != 0)
                throw new InvalidOperationException("Build failed: " + build.summary.result);
            audit.status = "passed";
        }
        catch (Exception e) { audit.status = "failed"; audit.failures.Add(e.ToString()); throw; }
        finally { audit.finishedUtc = DateTime.UtcNow.ToString("O"); Directory.CreateDirectory(Path.GetDirectoryName(report)); File.WriteAllText(report, JsonUtility.ToJson(audit, true)); }
    }
    static void VerifyMapHashes(Audit audit)
    {
        var before = JsonUtility.FromJson<Hashes>(File.ReadAllText(Source + "/Logs/Stage3Guidance/BeforeAssets.json"));
        if (before.assets == null || before.assets.Count(a => a.path.EndsWith(".unity")) != 5)
            throw new InvalidOperationException("Expected a prior snapshot of all five map scenes.");
        foreach (AssetHash asset in before.assets)
            foreach (string project in new[] { Source, Clone })
                using (var hash = SHA256.Create())
                    if (BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(project + "/" + asset.path))).Replace("-", "") != asset.sha256)
                        throw new InvalidOperationException("Authored map changed: " + project + "/" + asset.path);
        audit.sourceMapHashesUnchanged = audit.cloneMapHashesUnchanged = true;
    }
}

