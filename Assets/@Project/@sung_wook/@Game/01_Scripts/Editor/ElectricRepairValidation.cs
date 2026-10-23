using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace sung_wook
{
                                                                      
    public static class ElectricRepairValidation
    {
        [Serializable]
        private sealed class Check
        {
            public string name;
            public bool passed;
            public string details;
        }

        [Serializable]
        private sealed class Report
        {
            public string status = "running";
            public string scenePath = ElectricBounceSceneInstaller.ScenePath;
            public List<Check> checks = new List<Check>();
            public List<string> errors = new List<string>();
        }

                                                                                 
        public static void RunBatch()
        {
            if (!Application.isBatchMode)
            {
                Debug.LogWarning("ElectricRepairValidation requires an explicitly launched batch editor.");
                return;
            }

            var report = new Report();
            Application.LogCallback captureError = (message, stack, type) =>
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                    report.errors.Add(message + "\n" + stack);
            };
            Application.logMessageReceived += captureError;
            try
            {
                ValidateSceneRepair(report);
                ValidateComparisonRecord(report);
            }
            catch (Exception exception)
            {
                report.errors.Add(exception.ToString());
            }
            finally
            {
                Application.logMessageReceived -= captureError;
            }

            bool passed = report.errors.Count == 0 && report.checks.Count >= 9;
            foreach (Check check in report.checks) passed &= check.passed;
            report.status = passed ? "passed" : "failed";
            string folder = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Logs");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "ElectricRepairValidationReport.json"), JsonUtility.ToJson(report, true));
            EditorApplication.Exit(passed ? 0 : 1);
        }

                                                                              
        private static void ValidateSceneRepair(Report report)
        {
            string diskPath = Path.Combine(Path.GetDirectoryName(Application.dataPath), report.scenePath);
            string diskHash = Hash(diskPath);
            Scene scene = EditorSceneManager.OpenScene(report.scenePath, OpenSceneMode.Single);
            ElectricStageSelector selector = null;
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.TryGetComponent(out ElectricStageSelector candidate)) selector = candidate;
            if (selector == null || selector.StageRoots == null || selector.StageRoots.Length == 0)
                throw new InvalidOperationException("The original scene has no installed stage selector.");

            Transform stage = selector.StageRoots[0].transform;
            Vector3 originalPosition = stage.localPosition;
            var charger = stage.GetComponentInChildren<ContactCharger2D>(true);
            if (charger == null) throw new InvalidOperationException("Stage 02 has no contact charger.");
            string originalSelector = EditorJsonUtility.ToJson(selector);
            string originalCharger = EditorJsonUtility.ToJson(charger);
            try
            {
                bool changed = ElectricStageContactRepair.TryRepair(scene, out string message);
                Add(report, "fixed-scene-noop", !changed && stage.localPosition == Vector3.zero
                    && !scene.isDirty && Hash(diskPath) == diskHash, message);

                stage.localPosition = new Vector3(4.99f, 1.03f, 0f);
                EditorSceneManager.MarkSceneDirty(scene);
                changed = ElectricStageContactRepair.TryRepair(scene, out message);
                Add(report, "dirty-known-offset-repaired", changed && stage.localPosition == Vector3.zero, message);
                Add(report, "existing-references-preserved", EditorJsonUtility.ToJson(selector) == originalSelector
                    && EditorJsonUtility.ToJson(charger) == originalCharger,
                    "Existing player, lever, stage and contact anchor references remain serialized identically.");
                Add(report, "dirty-scene-not-saved", scene.isDirty && Hash(diskPath) == diskHash,
                    "Pending scene edits remain dirty and the scene file SHA256 is unchanged.");

                stage.localPosition = new Vector3(1f, 1f, 0f);
                EditorSceneManager.MarkSceneDirty(scene);
                changed = ElectricStageContactRepair.TryRepair(scene, out message);
                Add(report, "arbitrary-layout-preserved", !changed && stage.localPosition == new Vector3(1f, 1f, 0f)
                    && scene.isDirty && Hash(diskPath) == diskHash, message);
            }
            finally
            {
                                                                   
                stage.localPosition = originalPosition;
                EditorSceneManager.MarkSceneDirty(scene);
                Add(report, "scene-file-unchanged-after-tests", Hash(diskPath) == diskHash,
                    "Original position restored in memory without saving any validation changes.");
            }
        }

                                                                        
        private static void ValidateComparisonRecord(Report report)
        {
            var record = new ElectricComparisonRecord();
            var baseline = new ElectricForceSample { Distance = 2f, SignedCharge = 1f, SourceCharge = 1f,
                Force = Vector2.right * 2.5f, TheoreticalForceMagnitude = 2.5f };
            var result = new ElectricForceSample { Distance = 1f, SignedCharge = 1f, SourceCharge = 1f,
                Force = Vector2.right * 10f, TheoreticalForceMagnitude = 10f };
            record.RecordBaseline(baseline);
            record.RecordResult(result);
            Add(report, "comparison-preserves-baseline-and-result", record.HasBaseline && record.HasResult
                && record.BaselineSample.Distance == 2f && record.ResultSample.Distance == 1f
                && record.ResultSample.ForceMagnitude / record.BaselineSample.ForceMagnitude == 4f,
                record.DescribeResult());
            record.RecordBaseline(result);
            Add(report, "new-baseline-invalidates-old-result", record.HasBaseline && !record.HasResult
                && record.BaselineSample.Distance == 1f && record.ResultSample.ForceMagnitude == 0f,
                "A new baseline removes the result associated with the previous baseline.");
            record.RecordResult(result);
            record.Reset();
            Add(report, "comparison-reset-clears-records", !record.HasBaseline && !record.HasResult
                && record.BaselineSample.Distance == 0f && record.ResultSample.ForceMagnitude == 0f,
                "Reset removes both snapshots and their availability flags.");
        }

        private static string Hash(string path)
        {
            using (SHA256 algorithm = SHA256.Create())
                return BitConverter.ToString(algorithm.ComputeHash(File.ReadAllBytes(path)));
        }

        private static void Add(Report report, string name, bool passed, string details)
        {
            report.checks.Add(new Check { name = name, passed = passed, details = details });
        }
    }
}
