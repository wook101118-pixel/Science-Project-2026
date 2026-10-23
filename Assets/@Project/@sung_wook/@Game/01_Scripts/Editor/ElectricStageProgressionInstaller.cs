using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace sung_wook
{
                                                                    
    public static class ElectricStageProgressionInstaller
    {
        [MenuItem("Tools/sung_wook/Electric Bounce/Connect Stage Progression")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode before installing stage progression.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save pending scene edits before installing stage progression.");

            string previous = SceneManager.GetActiveScene().path;
            ElectricCompletionScreenBuilder.InstallIntoHudPrefab();
            var buildScenes = new List<EditorBuildSettingsScene>();
            var stagePaths = new HashSet<string>();
            for (int stage = 2; stage <= 5; stage++)
            {
                string path = ElectricStageProgression.StageScenePath(stage);
                stagePaths.Add(path);
                buildScenes.Add(new EditorBuildSettingsScene(path, true));
                Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                ElectricStageSceneBootstrap bootstrap = null;
                ElectricGameCompleteView view = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    if (root.TryGetComponent(out ElectricStageSceneBootstrap candidate)) bootstrap = candidate;
                    if (view == null) view = root.GetComponentInChildren<ElectricGameCompleteView>(true);
                }
                if (bootstrap == null || view == null)
                    throw new InvalidOperationException("Stage " + stage + " is missing its bootstrap or saved completion screen.");
                var progression = bootstrap.GetComponent<ElectricStageProgression>();
                if (progression == null) progression = bootstrap.gameObject.AddComponent<ElectricStageProgression>();
                var data = new SerializedObject(progression);
                data.FindProperty("completionView").objectReferenceValue = view;
                data.ApplyModifiedPropertiesWithoutUndo();
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save " + path);
            }
            foreach (EditorBuildSettingsScene existing in EditorBuildSettings.scenes)
                if (!stagePaths.Contains(existing.path)) buildScenes.Add(existing);
            EditorBuildSettings.scenes = buildScenes.ToArray();
            AssetDatabase.SaveAssets();
            if (!string.IsNullOrEmpty(previous)) EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
            Debug.Log("Connected Stage 02 → 03 → 04 → 05 → Game Complete and saved Build Settings.");
        }

        public static void InstallBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("InstallBatch requires a batch editor.");
            try { Install(); EditorApplication.Exit(0); }
            catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
        }
    }
}
