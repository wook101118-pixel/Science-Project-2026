// Temporary opt-in QA. Copy only into the isolated clone's Assets folder.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using sung_wook;

public static class QaMasteryPersistenceProbe
{
    [Serializable] private sealed class Check { public string name, details; public bool passed; }
    [Serializable] private sealed class Report {
        public string status, mode, startedUtc, finishedUtc, scope;
        public int pid;
        public List<Check> checks = new List<Check>();
        public List<string> errors = new List<string>();
    }
    private static Report report;
    private static void CheckResult(string name, bool passed, string details) {
        report.checks.Add(new Check { name = name, passed = passed, details = details });
        if (!passed) throw new InvalidOperationException(name + ": " + details);
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot() {
        if (Application.isEditor) return;
        string mode = null, path = null;
        foreach (string arg in Environment.GetCommandLineArgs()) {
            if (arg.StartsWith("--qa-mastery=")) mode = arg.Substring(13);
            if (arg.StartsWith("--qa-report=")) path = arg.Substring(12);
        }
        if (mode == null || string.IsNullOrEmpty(path) || !Path.IsPathRooted(path)) return;
        report = new Report { status = "running", mode = mode, startedUtc = DateTime.UtcNow.ToString("O"), pid = System.Diagnostics.Process.GetCurrentProcess().Id,
            scope = "Standalone-only. reset removes only temporary mastery keys before the campaign test; read uses a fresh process to inspect actual production records written by completed routes. Pure comparison regressions do not assert a route clear. Root restores the entire original PlayerPrefs registry after all tests." };
        try {
            Require(mode == "reset" || mode == "read", "Unknown mode.");
            if (mode == "reset") {
                for (int stage = 2; stage <= 5; stage++) {
                    string key = "ElectricBounce.Mastery.Stage" + stage;
                    PlayerPrefs.DeleteKey(key + ".Flags"); PlayerPrefs.DeleteKey(key + ".FewestRetries"); PlayerPrefs.DeleteKey(key + ".BestVoltage");
                    CheckResult("mastery-stage-" + stage + "-starts-empty", !PlayerPrefs.HasKey(key + ".Flags") && !PlayerPrefs.HasKey(key + ".FewestRetries") && !PlayerPrefs.HasKey(key + ".BestVoltage"), "Only stage mastery keys removed; display and SFX keys are untouched.");
                }
                PlayerPrefs.Save();
            } else {
                for (int stage = 2; stage <= 5; stage++) {
                    string key = "ElectricBounce.Mastery.Stage" + stage;
                    int flags = PlayerPrefs.GetInt(key + ".Flags", -1), retries = PlayerPrefs.GetInt(key + ".FewestRetries", -1);
                    CheckResult("mastery-stage-" + stage + "-persisted-real-no-retry-clear", flags >= 1 && (flags & 1) != 0 && retries == 0, "Production persistent flags=" + flags + ", fewest checkpoint returns=" + retries);
                    if (stage >= 3) CheckResult("mastery-stage-" + stage + "-persisted-optional-objective", (flags & 2) != 0, "Actual campaign completed the one-variable / four-change / 50~55V optional objective.");
                }
                float voltage = PlayerPrefs.GetFloat("ElectricBounce.Mastery.Stage5.BestVoltage", -1f);
                CheckResult("fresh-process-restores-actual-50V-personal-best", Mathf.Abs(voltage - 50f) <= 0.01f, "Saved qualified launch voltage=" + voltage);
                ComparisonRegressions();
            }
            report.status = "passed";
        } catch (Exception error) { report.status = "failed"; report.errors.Add(error.ToString()); }
        report.finishedUtc = DateTime.UtcNow.ToString("O");
        Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, JsonUtility.ToJson(report, true));
        Application.Quit(report.status == "passed" ? 0 : 1);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void ComparisonRegressions() {
        var record = new ElectricComparisonRecord();
        var baseline = new ElectricForceSample { Position = Vector2.zero, Distance = 2f, SignedCharge = 1f, SourceCharge = 1f, Force = Vector2.right * 4f, IsInRange = true };
        var one = baseline; one.Distance = 1f; one.Force = Vector2.right * 16f;
        var multiple = one; multiple.SignedCharge = 2f; multiple.Force = Vector2.right * 32f;
        record.RecordBaseline(baseline); record.RecordResult(one, true);
        CheckResult("qualified-one-variable-comparison-is-recorded", record.HasQualifiedComparison && record.HasSingleVariableComparison, "A successful prepared one-variable experiment earns its own record.");
        record.RecordResult(multiple, false);
        CheckResult("unprepared-relaunch-cannot-erase-earned-single-variable", record.HasSingleVariableComparison, "A later nonqualifying latest ResultSample does not erase the legitimate experiment.");
        record.RecordBaseline(baseline);
        CheckResult("new-baseline-resets-single-variable-result", !record.HasQualifiedComparison && !record.HasSingleVariableComparison, "New comparisons require new actual qualifying evidence.");
        record.RecordResult(multiple, true);
        CheckResult("multi-variable-remains-valid-basic-comparison", record.HasQualifiedComparison && !record.HasSingleVariableComparison, "Optional mastery never tightens the basic comparison goal.");
        record.RecordResult(one, false);
        CheckResult("unprepared-one-variable-relaunch-cannot-award-mastery", record.HasQualifiedComparison && !record.HasSingleVariableComparison, "A sticky basic goal plus a newer airborne sample cannot manufacture a mastery badge.");
        record.RecordResult(one, true);
        CheckResult("subsequent-prepared-experiment-can-earn-mastery", record.HasSingleVariableComparison, "Legitimate subsequent evidence is accepted.");
        record.Reset();
        CheckResult("full-comparison-reset-clears-all-attempt-flags", !record.HasSingleVariableComparison && !record.HasQualifiedComparison && !record.HasBaseline, "R's comparison reset does not retain attempt evidence.");
    }
}
