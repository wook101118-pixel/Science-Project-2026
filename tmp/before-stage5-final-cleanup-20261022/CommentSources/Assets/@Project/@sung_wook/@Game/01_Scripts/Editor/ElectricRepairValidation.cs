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
    /// <summary>배치 실행에서만 접촉 배치 복구의 안전 조건과 독립적인 실험 기록을 검증한다.</summary>
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

        /// <summary>열린 씬을 변경하는 자동 콜백 없이, 명시적인 -executeMethod 호출만 허용한다.</summary>
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

        /// <summary>실제 복구 API를 호출하며 수정 대상 외의 연결과 디스크 씬이 보존되는지 검사한다.</summary>
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
                // 실패해도 테스트가 만든 메모리상의 위치를 되돌린다. 씬 저장은 절대로 호출하지 않는다.
                stage.localPosition = originalPosition;
                EditorSceneManager.MarkSceneDirty(scene);
                Add(report, "scene-file-unchanged-after-tests", Hash(diskPath) == diskHash,
                    "Original position restored in memory without saving any validation changes.");
            }
        }

        /// <summary>기준 재설정과 초기화가 이전 실험 결과를 잘못 재사용하지 않는지 검사한다.</summary>
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
