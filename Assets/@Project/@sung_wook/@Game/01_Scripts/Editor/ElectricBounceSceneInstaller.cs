using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace sung_wook
{
                 
                                                 
                                                           
                                               
                  
    public static class ElectricBounceSceneInstaller
    {
        public const string ScenePath = "Assets/@Project/@sung_wook/@Game/Scenes/sungwook.unity";
        private static bool validationSucceeded;

        [Serializable]
        private sealed class Report
        {
            public string status;
            public string scenePath;
            public bool saved;
            public bool sceneWasDirty;
            public int playerAdapters;
            public int leverAdapters;
            public int stages;
            public int stars;
            public int goals;
            public string message;
        }

                                                                           
        [MenuItem("Tools/sung_wook/Electric Bounce/Validate Existing Scene Connections")]
        public static void InstallCurrentScene()
        {
            validationSucceeded = false;
            Scene scene = SceneManager.GetActiveScene();
            var report = new Report
            {
                status = "failed",
                scenePath = scene.path,
                sceneWasDirty = scene.isDirty,
                saved = false
            };
            try
            {
                if (scene.path != ScenePath || EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    throw new InvalidOperationException("Open the saved sungwook scene in Edit Mode for connection validation.");
                }

                ElectricStageSelector selector = null;
                int selectors = 0;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    report.playerAdapters += root.GetComponentsInChildren<ElectricBall2D>(true).Length;
                    report.leverAdapters += root.GetComponentsInChildren<StageLever2D>(true).Length;
                    foreach (ElectricStageSelector candidate in root.GetComponentsInChildren<ElectricStageSelector>(true))
                    {
                        selector = candidate;
                        selectors++;
                    }
                }
                if (selectors != 1 || report.playerAdapters != 1 || report.leverAdapters != 1
                    || selector.Ball == null || selector.Lever == null || selector.SpawnPoint == null
                    || selector.Lever.Reader == null || selector.StageRoots.Length != 4)
                {
                    throw new InvalidOperationException("The existing player, lever, selector, spawn or four stage references are missing or duplicated.");
                }

                foreach (GameObject stage in selector.StageRoots)
                {
                    if (stage == null || stage.scene != scene)
                    {
                        throw new InvalidOperationException("A stage reference does not belong to the open original scene.");
                    }
                    report.stages++;
                    report.stars += stage.GetComponentsInChildren<ElectricStar2D>(true).Length;
                    report.goals += stage.GetComponentsInChildren<ElectricGoal2D>(true).Length;
                }
                if (report.stars != 4 || report.goals != 4)
                {
                    throw new InvalidOperationException("The four authored stars and exits must remain connected.");
                }
                report.status = "validated";
                report.message = "Existing connections validated. No objects, references, layout or saved scene data were changed.";
                validationSucceeded = true;
                Debug.Log(report.message, selector);
            }
            catch (Exception exception)
            {
                report.message = exception.Message;
                Debug.LogError("Electric Bounce connection validation: " + report.message);
            }
            WriteReport(report);
        }

                                                                   
        public static void InstallForBatch()
        {
            if (!Application.isBatchMode)
            {
                return;
            }
            if (SceneManager.GetActiveScene().path != ScenePath)
            {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }
            InstallCurrentScene();
            if (!validationSucceeded)
            {
                throw new InvalidOperationException("Saved scene connection validation failed; see ElectricBounceIntegrationReport.json.");
            }
        }

        private static void WriteReport(Report report)
        {
            string folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Logs");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "ElectricBounceIntegrationReport.json"), JsonUtility.ToJson(report, true));
        }
    }
}
