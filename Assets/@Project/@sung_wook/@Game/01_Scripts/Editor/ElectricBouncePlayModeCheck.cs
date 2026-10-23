using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace sung_wook
{
    [InitializeOnLoad]
    public static class ElectricBouncePlayModeCheck
    {
        private const string Prefix = "sung_wook.ElectricBounce.PlayModeCheck.";

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
            public string scenePath;
            public float activeSeconds;
            public List<Check> checks = new List<Check>();
            public List<string> errors = new List<string>();
        }

        static ElectricBouncePlayModeCheck()
        {
            if (Application.isBatchMode && SessionState.GetBool(Prefix + "Active", false)) Resume();
        }

        public static void RunBatch()
        {
            Run(ElectricBounceSceneInstaller.ScenePath);
        }

        public static void RunPrototypeBatch()
        {
            Run(ElectricLearningSceneInstaller.PrototypePath);
        }

        private static void Run(string scenePath)
        {
            if (!Application.isBatchMode)
            {
                Debug.LogWarning("ElectricBouncePlayModeCheck is restricted to an explicitly launched batch editor.");
                return;
            }
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            SessionState.SetBool(Prefix + "Active", true);
            SessionState.SetInt(Prefix + "Phase", 0);
            SessionState.SetString(Prefix + "Started", EditorApplication.timeSinceStartup.ToString(CultureInfo.InvariantCulture));
            SessionState.SetString(Prefix + "PlayStarted", "");
            Save(new Report { scenePath = scenePath });
            Resume();
            ElectricBatchPlayMode.EnterAfterEditorStartup();
        }

        private static void Resume()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            Application.logMessageReceived -= CaptureLog;
            Application.logMessageReceived += CaptureLog;
        }

        private static void CaptureLog(string message, string stackTrace, LogType type)
        {
            if (type != LogType.Exception && type != LogType.Error && type != LogType.Assert) return;
            Report report = Load();
            report.errors.Add(message + "\n" + stackTrace);
            Save(report);
        }

        private static double Timestamp(string name)
        {
            double.TryParse(SessionState.GetString(Prefix + name, "0"), NumberStyles.Float,
                CultureInfo.InvariantCulture, out double value);
            return value;
        }

        private static void Tick()
        {
            if (!Application.isBatchMode || !SessionState.GetBool(Prefix + "Active", false))
            {
                return;
            }
            
            try
            {
                if (EditorApplication.timeSinceStartup - Timestamp("Started") > 90d)
                {
                    AddCheck("watchdog", false, "Editor did not finish within 90 seconds.");
                    Finish();
                    return;
                }
                if (!EditorApplication.isPlaying || EditorApplication.isPaused)
                {
                    return;
                }
                if (string.IsNullOrEmpty(SessionState.GetString(Prefix + "PlayStarted", "")))
                {
                    SessionState.SetString(Prefix + "PlayStarted",
                        EditorApplication.timeSinceStartup.ToString(CultureInfo.InvariantCulture));
                }
                if (EditorApplication.timeSinceStartup - Timestamp("PlayStarted") > 15d)
                {
                    AddCheck("playmode-time-limit", false, "Checks did not finish within 15 seconds of active Play Mode.");
                    Finish();
                    return;
                }

                var selector = UnityEngine.Object.FindFirstObjectByType<ElectricStageSelector>();
                if (selector == null || selector.Ball == null || selector.Lever == null)
                {
                    AddCheck("installed-selector", false, "The installed selector, ball or lever reference is missing.");
                    Finish();
                    return;
                }
                
                ElectricBall2D ball = selector.Ball;
                int phase = SessionState.GetInt(Prefix + "Phase", 0);
                float deadline = SessionState.GetFloat(Prefix + "Deadline", 0f);
                switch (phase)
                {
                    case 0:
                        AddCheck("single-existing-player-stack",
                            ball.GetComponents<PlayerMovement>().Length == 1
                            && ball.GetComponents<PlayerController>().Length == 1
                            && ball.GetComponents<ElectricBall2D>().Length == 1
                            && UnityEngine.Object.FindObjectsByType<ElectricBall2D>(FindObjectsSortMode.None).Length == 1,
                            "Player: " + ball.name + "; one movement/controller/ball adapter required.");
                        
                        AddCheck("existing-lever-reader", selector.Lever.Reader != null,
                            selector.Lever.Reader != null ? selector.Lever.Reader.name : "Missing reader");
                        
                        selector.SelectStage(2);
                        selector.Lever.SetValue(1f);
                        Next(1, 1.5f);
                        break;
                    case 1:
                        if (ball.CurrentPolarity == Polarity.Positive || Time.fixedTime >= deadline)
                        {
                            AddCheck("stage02-actual-contact-charging", ball.CurrentPolarity == Polarity.Positive,
                                "Polarity=" + ball.CurrentPolarity + "; position=" + ball.Body.position);
                            selector.SelectStage(3);
                            selector.Lever.SetValue(1f);
                            Next(2, 0.06f);
                        }
                        break;
                    case 2:
                        if (Time.fixedTime < deadline)
                        {
                            break;
                        }
                        
                        Stage<DistanceLauncher2D>(selector, 3).RequestLaunch();
                        Next(3, 0.04f);
                        break;
                    case 3:
                        if (Time.fixedTime < deadline) break;
                        Vector2 distanceVelocity = ball.Body.linearVelocity;
                        AddCheck("stage03-near-distance-launch", distanceVelocity.x > 1f && distanceVelocity.y > 1f,
                            "Velocity=" + distanceVelocity);
                        selector.SelectStage(4);
                        selector.Lever.SetValue(1f);
                        Next(4, 0.08f);
                        break;
                    case 4:
                        if (Time.fixedTime < deadline) break;
                        var field = Stage<UniformField2D>(selector, 4);
                        AddCheck("stage04-upward-field", field.FieldDirection.y > 0.99f && ball.Body.linearVelocity.y > 0f,
                            "Field=" + field.FieldDirection + "; velocity=" + ball.Body.linearVelocity);
                        selector.SelectStage(5);
                        selector.Lever.SetValue(1f);
                        Next(5, 1f);
                        break;
                    case 5:
                        if (Time.fixedTime < deadline) break;
                        var voltage = Stage<VoltageLauncher2D>(selector, 5);
                        AddCheck("stage05-charge", voltage.Voltage >= 20f,
                            "Voltage=" + voltage.Voltage.ToString("0.00", CultureInfo.InvariantCulture));
                        Transform spawn = selector.transform.Find("Restart Spawn");
                        if (spawn == null) throw new InvalidOperationException("Prototype restart anchor is missing.");
                        ball.ResetAt(spawn.position, Polarity.Positive);
                        Next(6, 0.04f);
                        break;
                    case 6:
                        if (Time.fixedTime < deadline) break;
                        var chargedLauncher = Stage<VoltageLauncher2D>(selector, 5);
                        SessionState.SetFloat(Prefix + "VoltageBefore", chargedLauncher.Voltage);
                        chargedLauncher.RequestLaunch();
                        Next(7, 0.04f);
                        break;
                    case 7:
                        if (Time.fixedTime < deadline) break;
                        var afterLaunch = Stage<VoltageLauncher2D>(selector, 5);
                        float previous = SessionState.GetFloat(Prefix + "VoltageBefore", 0f);
                        AddCheck("stage05-consume-voltage-and-launch", previous > 0f
                            && afterLaunch.Voltage < 2f && ball.Body.linearVelocity.y > 1f,
                            "Before=" + previous + "; after=" + afterLaunch.Voltage
                            + "; velocity=" + ball.Body.linearVelocity);
                        Finish();
                        break;
                }
            }
            catch (Exception exception)
            {
                Report report = Load();
                report.errors.Add(exception.ToString());
                Save(report);
                Finish();
            }
        }

        private static T Stage<T>(ElectricStageSelector selector, int stageNumber) where T : Component
        {
            T result = selector.StageRoots[stageNumber - 2].GetComponentInChildren<T>(true);
            if (result == null) throw new InvalidOperationException(typeof(T).Name + " is missing from stage " + stageNumber);
            return result;
        }

        private static void Next(int phase, float waitPhysicsSeconds)
        {
            SessionState.SetInt(Prefix + "Phase", phase);
            SessionState.SetFloat(Prefix + "Deadline", Time.fixedTime + waitPhysicsSeconds);
        }

        private static void AddCheck(string name, bool passed, string details)
        {
            Report report = Load();
            report.checks.Add(new Check { name = name, passed = passed, details = details });
            Save(report);
        }

        private static Report Load()
        {
            string json = SessionState.GetString(Prefix + "Report", "");
            return string.IsNullOrEmpty(json) ? new Report() : JsonUtility.FromJson<Report>(json);
        }

        private static void Save(Report report)
        {
            SessionState.SetString(Prefix + "Report", JsonUtility.ToJson(report));  
        } 

        private static void Finish()
        {
            if (!Application.isBatchMode) return;
            Report report = Load();
            bool passed = report.errors.Count == 0 && report.checks.Count >= 7;
            foreach (Check check in report.checks) passed &= check.passed;
            report.status = passed ? "passed" : "failed";
            double playStarted = Timestamp("PlayStarted");
            report.activeSeconds = playStarted > 0d ? (float)(EditorApplication.timeSinceStartup - playStarted) : 0f;
            string folder = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Logs");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "ElectricBouncePlayModeReport.json"), JsonUtility.ToJson(report, true));
            SessionState.SetBool(Prefix + "Active", false);
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= CaptureLog;
            EditorApplication.Exit(passed ? 0 : 1);
        }
    }
}
