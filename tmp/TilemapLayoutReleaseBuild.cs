// Temporary build entry point. Copy only to the isolated clone Assets/Editor.
// Unity -batchmode -quit -projectPath <clone> -executeMethod TilemapLayoutReleaseBuild.Build
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Tilemaps;
using sung_wook;

public static class TilemapLayoutReleaseBuild
{
    private const string Clone = "C:/Users/wook1/AppData/Local/Temp/CodexUnityVisualBuild";
    private const string Task = "C:/Users/wook1/GitBub-Unity/Science-Project-2026";
    [Serializable] private sealed class TerrainCheck
    {
        public int stage, tiles, collisionPaths;
        public string name;
        public bool groundTag, collisionReady;
    }
    [Serializable] private sealed class Audit
    {
        public string status = "running", startedUtc = DateTime.UtcNow.ToString("O"), finishedUtc;
        public string unity, project, output, result;
        public bool developmentBuild = false;
        public string[] scenes, errors;
        public int totalErrors, totalWarnings;
        public int savedContactGroundScenes, savedNonBouncyStage3Obstacles;
        public List<TerrainCheck> terrain = new List<TerrainCheck>();
        public double buildSeconds;
    }
    public static void Build()
    {
        var audit = new Audit();
        string report = Task + "/Logs/TilemapLayout/Build.json";
        try
        {
            audit.project = Path.GetFullPath(Directory.GetParent(Application.dataPath).FullName).Replace('\\', '/');
            if (!Application.isBatchMode || audit.project != Clone) throw new InvalidOperationException("Wrong isolated build project.");
            audit.unity = Application.unityVersion;
            audit.output = Clone + "/Builds/TilemapLayoutRelease/ScienceProject.exe";
            audit.scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
            if (audit.scenes.Length != 6 || audit.scenes[0] != ElectricGameCompleteView.MainMenuPath)
                throw new InvalidOperationException("Main menu must be the first of six enabled build scenes.");
            for (int stage = 1; stage <= 5; stage++)
            {
                string path = ElectricStageProgression.StageScenePath(stage);
                if (!audit.scenes.Contains(path) || !File.Exists(path)) throw new InvalidOperationException("Campaign stage missing from build: " + path);
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                int terrainBefore = audit.terrain.Count;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    if (stage <= 2)
                    {
                        foreach (ContactCharger2D source in root.GetComponentsInChildren<ContactCharger2D>(true))
                        {
                            if (!source.CompareTag("Ground")) throw new InvalidOperationException("Fixed contact electrode must support normal player bounce in stage " + stage);
                            audit.savedContactGroundScenes++;
                        }
                    }
                    if (stage == 3)
                    {
                        foreach (Collider2D obstacle in root.GetComponentsInChildren<Collider2D>(true))
                            if (obstacle.name.StartsWith("Obstacle Blue Falling Stone", StringComparison.Ordinal))
                            {
                                if (obstacle.CompareTag("Ground")) throw new InvalidOperationException("Stage 3 obstacles must keep their saved non-bouncy tags.");
                                audit.savedNonBouncyStage3Obstacles++;
                            }
                    }
                    foreach (Tilemap map in root.GetComponentsInChildren<Tilemap>(true))
                    {
                        if (!map.gameObject.activeInHierarchy || !IsTerrain(map.transform, stage)) continue;
                        TilemapCollider2D collider = map.GetComponent<TilemapCollider2D>();
                        if (collider == null || !collider.enabled || collider.isTrigger)
                            throw new InvalidOperationException("Terrain needs an enabled solid tile collider: " + map.name);
                        int tiles = 0;
                        foreach (Vector3Int cell in map.cellBounds.allPositionsWithin) if (map.HasTile(cell)) tiles++;
                        collider.ProcessTilemapChanges();
                        Physics2D.SyncTransforms();
                        CompositeCollider2D composite = map.GetComponent<CompositeCollider2D>();
                        if (composite != null) composite.GenerateGeometry();
                        var check = new TerrainCheck {
                            stage = stage, name = map.name, tiles = tiles,
                            collisionPaths = composite != null ? composite.pathCount : collider.shapeCount,
                            groundTag = map.CompareTag("Ground"), collisionReady = !collider.hasTilemapChanges
                        };
                        audit.terrain.Add(check);
                        if (check.tiles == 0 || check.collisionPaths == 0 || !check.groundTag || !check.collisionReady)
                            throw new InvalidOperationException("Saved terrain tag or collision is invalid in stage " + stage + ": " + map.name);
                    }
                }
                if (audit.terrain.Count == terrainBefore) throw new InvalidOperationException("No authored terrain maps found in stage " + stage);
            }
            if (audit.savedContactGroundScenes != 2 || audit.savedNonBouncyStage3Obstacles != 3) throw new InvalidOperationException("Saved contact/obstacle checks did not find expected scene objects.");
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
    private static bool IsTerrain(Transform value, int stage)
    {
        string prefabSuffix = "/Modules/Terrain/Stage" + stage.ToString("00") + "_Terrain.prefab";
        for (Transform current = value; current != null; current = current.parent)
        {
            // A scene may override a prefab instance's display name (Stage 2 uses Rectangle).
            string prefab = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(current.gameObject);
            if (!string.IsNullOrEmpty(prefab) && prefab.Replace('\\', '/').EndsWith(prefabSuffix, StringComparison.Ordinal)) return true;
            GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(current.gameObject);
            string asset = source != null ? AssetDatabase.GetAssetPath(source) : null;
            if (!string.IsNullOrEmpty(asset) && asset.Replace('\\', '/').EndsWith(prefabSuffix, StringComparison.Ordinal)) return true;
        }
        return false;
    }
}
