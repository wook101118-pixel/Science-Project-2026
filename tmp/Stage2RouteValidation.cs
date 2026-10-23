using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace sung_wook
{
    // Temporary validation-only script. Copy into the isolated batch project's Editor folder.
    [InitializeOnLoad]
    public static class Stage2RouteValidation
    {
        private const string Prefix = "Codex.Stage2Route.";
        private const string ScenePath = "Assets/@Project/@sung_wook/@Game/00_Scenes/Stage/Stage_02.unity";
        [Serializable] private sealed class Sample
        {
            public float time, input, lever, signedCharge;
            public string phase;
            public Vector2 position, velocity;
            public bool charged, star;
        }
        [Serializable] private sealed class Report
        {
            public string status = "running", reason;
            public string method = "Saved default player; normal RestartStage, lever, and horizontal movement commands only. No route teleport, speed, gravity, jump, collision, force, or learning-goal mutation.";
            public Vector2 spawn, finish;
            public float seconds, peakY, highestBallEdge, cameraTop, mass, gravityScale, jumpForce, maxSpeed, acceleration;
            public bool actualChargeEvent, learningSatisfied, actualStarEvent, actualGoalEvent;
            public List<string> surfaces = new List<string>();
            public List<Sample> trace = new List<Sample>();
            public List<string> errors = new List<string>();
        }

        private static Report report;
        private static ElectricStageSelector selector;
        private static ElectricLearningController2D lesson;
        private static PlayerMovement movement;
        private static ContactCharger2D charger;
        private static ElectricStar2D star;
        private static ElectricGoal2D goal;
        private static float start, lastFixedTime = -1f, nextTrace, input;
        private static bool initialized, finished;
        private static string phase;
        private static readonly ContactPoint2D[] contacts = new ContactPoint2D[32];
        private static string ReportPath => Path.Combine(Path.GetDirectoryName(Application.dataPath), "Logs/Stage2DefaultRouteReport.json");

        static Stage2RouteValidation()
        {
            if (Application.isBatchMode && SessionState.GetBool(Prefix + "Active", false)) Subscribe();
        }
        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated batch editor.");
            report = new Report();
            SessionState.SetBool(Prefix + "Active", true);
            SessionState.SetString(Prefix + "Started", DateTime.UtcNow.ToString("O"));
            Write();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Subscribe();
            ElectricBatchPlayMode.EnterAfterEditorStartup();
        }
        private static void Subscribe()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
        }
        private static void OnLog(string message, string stack, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            EnsureReport();
            report.errors.Add(message + "\n" + stack);
            Write();
        }
        private static void EnsureReport()
        {
            if (report == null) report = JsonUtility.FromJson<Report>(File.ReadAllText(ReportPath));
        }
        private static void Tick()
        {
            if (finished || !SessionState.GetBool(Prefix + "Active", false)) return;
            try
            {
                EnsureReport();
                if ((DateTime.UtcNow - DateTime.Parse(SessionState.GetString(Prefix + "Started", ""))).TotalSeconds > 180)
                    throw new TimeoutException("Stage 2 route batch watchdog exceeded 180 seconds.");
                if (!EditorApplication.isPlaying || EditorApplication.isPaused) return;
                if (!initialized)
                {
                    selector = UnityEngine.Object.FindFirstObjectByType<ElectricStageSelector>();
                    if (selector == null || selector.SelectedStage != 2 || selector.GetComponent<ElectricStageSession2D>()?.IsInitialized != true) return;
                    Begin();
                }
                if (Mathf.Approximately(lastFixedTime, Time.fixedTime)) return;
                lastFixedTime = Time.fixedTime;
                Rigidbody2D body = selector.Ball.Body;
                report.seconds = Time.fixedTime - start;
                report.finish = body.position;
                report.peakY = Mathf.Max(report.peakY, body.position.y);
                report.highestBallEdge = Mathf.Max(report.highestBallEdge, selector.Ball.GetComponent<Collider2D>().bounds.max.y);
                if (!Mathf.Approximately(body.mass, report.mass) || !Mathf.Approximately(body.gravityScale, report.gravityScale))
                    throw new InvalidOperationException("Saved player mass or gravity changed during route.");
                ObserveContacts();
                report.learningSatisfied = lesson.CurrentLearningGoalSatisfied;
                if (goal.IsCleared)
                {
                    Finish(report.actualChargeEvent && report.actualStarEvent && report.actualGoalEvent && report.learningSatisfied,
                        "Observed actual neutral-to-charged collision, star collection, and goal completion using saved default physics.");
                    return;
                }
                if (report.seconds > 65f || body.position.y < -9f || Mathf.Abs(body.position.x) > 25f)
                {
                    Finish(false, "Route timeout or outside map.");
                    return;
                }
                if (!lesson.ContactChallengeSatisfied)
                {
                    phase = "Contact";
                    selector.Lever.SetValue(1f);
                    Steer(report.spawn.x);
                }
                else
                {
                    // Releasing a powered electrode after actual contact is a normal lever action.
                    selector.Lever.SetValue(0f);
                    phase = star.IsCollected ? "Exit" : "StaircaseAndStar";
                    Steer(star.IsCollected ? goal.transform.position.x : star.transform.position.x);
                }
                if (Time.fixedTime >= nextTrace)
                {
                    nextTrace = Time.fixedTime + 0.15f;
                    report.trace.Add(new Sample { time = report.seconds, input = input, lever = selector.Lever.SignedValue,
                        signedCharge = selector.Ball.SignedCharge, phase = phase, position = body.position,
                        velocity = body.linearVelocity, charged = lesson.ContactChallengeSatisfied, star = star.IsCollected });
                    Write();
                }
            }
            catch (Exception error)
            {
                EnsureReport();
                report.errors.Add(error.ToString());
                Finish(false, error.Message);
            }
        }
        private static void Begin()
        {
            initialized = true;
            lesson = selector.GetComponent<ElectricLearningController2D>();
            movement = selector.Ball.GetComponent<PlayerMovement>();
            selector.Ball.GetComponent<PlayerController>().enabled = false;
            selector.Ball.GetComponent<PlayerInputReader>().enabled = false;
            selector.RestartStage();
            charger = selector.GetStageComponent<ContactCharger2D>(2);
            star = selector.GetStageComponent<ElectricStar2D>(2);
            goal = selector.GetStageComponent<ElectricGoal2D>(2);
            charger.ChargedFromNeutral += _ => report.actualChargeEvent = true;
            star.OnCollected.AddListener(() => report.actualStarEvent = true);
            goal.OnCleared.AddListener(() => report.actualGoalEvent = true);
            start = Time.fixedTime;
            report.spawn = selector.Ball.Body.position;
            report.peakY = report.spawn.y;
            report.mass = selector.Ball.Body.mass;
            report.gravityScale = selector.Ball.Body.gravityScale;
            report.cameraTop = Camera.main.transform.position.y + Camera.main.orthographicSize;
            var settings = new SerializedObject(movement);
            report.jumpForce = settings.FindProperty("jumpForce").floatValue;
            report.maxSpeed = settings.FindProperty("maxMoveSpeed").floatValue;
            report.acceleration = settings.FindProperty("acceleration").floatValue;
            if (report.maxSpeed <= 0f) report.maxSpeed = settings.FindProperty("moveSpeed").floatValue;
            selector.Lever.SetValue(1f);
            Write();
        }
        private static void Steer(float target)
        {
            Rigidbody2D body = selector.Ball.Body;
            float error = target - body.position.x;
            float cap = Mathf.Min(report.maxSpeed, Mathf.Sqrt(2f * report.acceleration * Mathf.Abs(error)));
            float desired = Mathf.Clamp(error * 2.5f - body.linearVelocity.x * 0.45f, -cap, cap);
            // Digital A/D-equivalent commands only. A small velocity dead zone avoids key chatter.
            float velocityError = desired - body.linearVelocity.x;
            input = Mathf.Abs(velocityError) < 0.15f ? 0f : Mathf.Sign(velocityError);
            if (Mathf.Abs(error) < 0.08f && Mathf.Abs(body.linearVelocity.x) < 0.15f) input = 0f;
            movement.SetMovex(input);
        }
        private static void ObserveContacts()
        {
            int count = selector.Ball.Body.GetContacts(contacts);
            for (int index = 0; index < count; index++)
            {
                ContactPoint2D point = contacts[index];
                if (Mathf.Abs(point.normal.y) < 0.6f || selector.Ball.Body.position.y < point.point.y + 0.25f) continue;
                string key = "top y=" + point.point.y.ToString("F2");
                if (!report.surfaces.Contains(key)) report.surfaces.Add(key);
            }
        }
        private static void Write()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, JsonUtility.ToJson(report, true));
        }
        private static void Finish(bool passed, string reason)
        {
            finished = true;
            report.status = passed && report.errors.Count == 0 ? "passed" : "failed";
            report.reason = reason;
            Write();
            SessionState.SetBool(Prefix + "Active", false);
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            EditorApplication.Exit(report.status == "passed" ? 0 : 1);
        }
    }
}
