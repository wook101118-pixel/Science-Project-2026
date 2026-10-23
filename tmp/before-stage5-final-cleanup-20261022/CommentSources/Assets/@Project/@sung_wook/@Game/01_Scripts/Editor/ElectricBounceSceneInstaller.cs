using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace sung_wook
{
    /// <summary>
    /// 원본 씬에 저장된 플레이어·레버·스테이지 연결을 읽기 전용으로 검사합니다.
    /// 이전 설치 메뉴가 수작업 디자인을 덮어쓰지 않도록 생성·자동 설치·저장 경로를 제거했습니다.
    /// 기존 검사 코드와의 호환성을 위해 클래스명과 공개 진입점은 유지합니다.
    /// </summary>
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

        /// <summary>메뉴 이름도 검사로 바꾸어, 배치나 참조를 수정하지 않는다는 역할을 드러냅니다.</summary>
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

        /// <summary>이전 배치 명령도 생성 대신 동일한 읽기 전용 검사를 수행합니다.</summary>
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
