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
    public static class ElectricGoalGatePlayModeCheck
    {
        private const string Prefix = "sung_wook.ElectricGoalGate.Check.";

                                                       
        private enum Phase
        {
            Start,
            ContactUnobservedExit,
            ContactFakePositiveExit,
            ContactActualCollision,
            ContactNeedsStar,
            ContactCollectStar,
            ContactPassed,
            DistanceUnobservedExit,
            DistanceReadyWithoutBaseline,
            DistanceLaunchedWithoutBaseline,
            DistanceNoBaselineExit,
            DistanceReadyUnchanged,
            DistanceUnchangedLaunched,
            DistanceAirborneLaunched,
            DistanceInvalidExit,
            DistanceReadyBaseline,
            DistanceConditionChanged,
            DistanceAccepted,
            DistanceCollectStar,
            DistancePassed,
            DistanceNewBaselineExit,
            DistanceBallChargeBaseline,
            DistanceBallChargeAccepted,
            DistanceSourceChargeBaseline,
            DistanceSourceChargeAccepted,
            VoltageUnobservedExit,
            VoltageChargeOnly,
            VoltageChargeOnlyExit,
            VoltageFailedOutside,
            VoltageLowCharge,
            VoltageLowLaunched,
            VoltageLowExit,
            VoltageHighCharge,
            VoltageHighLaunched,
            VoltageNeedsStar,
            VoltageCollectStar,
            VoltagePassed
        }

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

        static ElectricGoalGatePlayModeCheck()
        {
            if (Application.isBatchMode && SessionState.GetBool(Prefix + "Active", false))
                Resume();
        }

                                                             
        public static void RunBatch() => Run(ElectricBounceSceneInstaller.ScenePath);

                                                               
        public static void RunPrototypeBatch() => Run(ElectricLearningSceneInstaller.PrototypePath);

        private static void Run(string scenePath)
        {
            if (!Application.isBatchMode)
            {
                Debug.LogWarning("ElectricGoalGatePlayModeCheck requires an explicitly launched batch editor.");
                return;
            }

            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            SessionState.SetBool(Prefix + "Active", true);
            SessionState.SetInt(Prefix + "Phase", (int)Phase.Start);
            SessionState.SetFloat(Prefix + "Deadline", 0f);
            SessionState.SetString(Prefix + "Started", Now());
            SessionState.SetString(Prefix + "PlayStarted", "");
            Save(new Report { scenePath = scenePath });
            Resume();
                                                           
            ElectricBatchPlayMode.EnterAfterEditorStartup();
        }

        private static string Now() => EditorApplication.timeSinceStartup.ToString(CultureInfo.InvariantCulture);

        private static void Resume()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            Application.logMessageReceived -= CaptureLog;
            Application.logMessageReceived += CaptureLog;
        }

        private static void CaptureLog(string message, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)
                return;
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
                return;
            try
            {
                if (EditorApplication.timeSinceStartup - Timestamp("Started") > 120d)
                {
                    AddCheck("watchdog", false, "Editor did not finish within 120 seconds.");
                    Finish();
                    return;
                }
                if (!EditorApplication.isPlaying || EditorApplication.isPaused)
                    return;
                if (string.IsNullOrEmpty(SessionState.GetString(Prefix + "PlayStarted", "")))
                    SessionState.SetString(Prefix + "PlayStarted", Now());
                if (EditorApplication.timeSinceStartup - Timestamp("PlayStarted") > 60d)
                {
                    AddCheck("playmode-time-limit", false, "Checks exceeded 60 seconds of active Play Mode.");
                    Finish();
                    return;
                }
                if (Time.fixedTime < SessionState.GetFloat(Prefix + "Deadline", 0f))
                    return;

                var selector = UnityEngine.Object.FindFirstObjectByType<ElectricStageSelector>();
                if (selector == null || selector.Ball == null || selector.Lever == null)
                    throw new InvalidOperationException("Installed selector, existing ball or lever is missing.");
                var lesson = selector.GetComponent<ElectricLearningController2D>();
                if (lesson == null)
                    throw new InvalidOperationException("Installed learning controller is missing.");

                Phase phase = (Phase)SessionState.GetInt(Prefix + "Phase", 0);
                if (phase <= Phase.ContactPassed)
                    CheckContact(phase, selector, lesson);
                else if (phase <= Phase.DistanceSourceChargeAccepted)
                    CheckComparison(phase, selector, lesson);
                else
                    CheckVoltage(phase, selector, lesson);
            }
            catch (Exception exception)
            {
                Report report = Load();
                report.errors.Add(exception.ToString());
                Save(report);
                Finish();
            }
        }

                                                                   
        private static void CheckContact(Phase phase, ElectricStageSelector selector,
            ElectricLearningController2D lesson)
        {
            ElectricBall2D ball = selector.Ball;
            Rigidbody2D body = ball.Body;
            switch (phase)
            {
                case Phase.Start:
                    AddCheck("single-existing-player-and-lever-stack",
                        ball.GetComponents<Player>().Length == 1
                        && ball.GetComponents<PlayerMovement>().Length == 1
                        && ball.GetComponents<PlayerController>().Length == 1
                        && ball.GetComponents<Rigidbody2D>().Length == 1
                        && ball.GetComponents<ElectricBall2D>().Length == 1
                        && selector.Lever.GetComponents<StageLever2D>().Length == 1
                        && selector.Lever.Reader != null,
                        "기존 플레이어·레버 컴포넌트 한 세트와 연결을 유지합니다.");
                    SessionState.SetInt(Prefix + "Constraints", (int)body.constraints);
                    SessionState.SetFloat(Prefix + "Gravity", body.gravityScale);
                    ResetStage(selector, 2);
                    HoldAtExit(selector, true);
                    Next(Phase.ContactUnobservedExit);
                    break;

                case Phase.ContactUnobservedExit:
                    AddCheck("stage02-star-alone-does-not-open-exit", !Goal(selector).IsCleared
                        && !lesson.ContactChallengeSatisfied, "별이 있어도 접촉 대전 전에는 실제 출구가 닫힙니다.");
                    ball.SetPositive();
                    Next(Phase.ContactFakePositiveExit);
                    break;

                case Phase.ContactFakePositiveExit:
                    AddCheck("stage02-direct-polarity-change-cannot-fake-contact", !Goal(selector).IsCleared
                        && !lesson.ContactChallengeSatisfied && ball.CurrentPolarity == Polarity.Positive,
                        "전극 접촉 없이 양전하로 바꾼 공으로 출구를 통과할 수 없습니다.");
                    ResetStage(selector, 2);
                    body.gravityScale = SessionState.GetFloat(Prefix + "Gravity", 1f);
                    selector.Lever.SetValue(1f);
                    SessionState.SetFloat(Prefix + "ContactTimeout", Time.fixedTime + 1.5f);
                    Next(Phase.ContactActualCollision, 0.02f);
                    break;

                case Phase.ContactActualCollision:
                    if (!lesson.ContactChallengeSatisfied
                        && Time.fixedTime < SessionState.GetFloat(Prefix + "ContactTimeout", 0f))
                        return;
                    AddCheck("stage02-real-neutral-contact-recorded", lesson.ContactChallengeSatisfied
                        && ball.CurrentPolarity == Polarity.Positive,
                        "기존 레버와 실제 OnCollision 대전: " + body.position + "; " + DescribeGoalState(selector));
                                                                
                                                               
                    ball.ResetAt(body.position, ball.CurrentPolarity);
                    Stage<ElectricStar2D>(selector, 2).ResetProgress();
                    Goal(selector).ResetProgress();
                    HoldAtExit(selector, false);
                    Next(Phase.ContactNeedsStar);
                    break;

                case Phase.ContactNeedsStar:
                    AddCheck("stage02-contact-still-requires-star", !Goal(selector).IsCleared
                        && ball.Stars == 0, "접촉 목표를 달성해도 별이 없으면 출구는 닫힙니다. "
                        + DescribeGoalState(selector));
                    HoldAtStar(selector);
                    Next(Phase.ContactCollectStar);
                    break;

                case Phase.ContactCollectStar:
                    AddCheck("stage02-real-star-trigger-collected", ball.Stars >= 1,
                        "실제 별 트리거와 겹쳐 수집했습니다. " + DescribeGoalState(selector));
                    HoldAtExit(selector, false);
                    Next(Phase.ContactPassed);
                    break;

                case Phase.ContactPassed:
                    AddCheck("stage02-contact-and-star-open-exit", Goal(selector).IsCleared
                        && !body.simulated, "접촉 기록과 별을 가진 공의 실제 출구 충돌로 클리어합니다. "
                        + DescribeGoalState(selector));
                    ResetStage(selector, 2);
                    AddCheck("stage02-restart-revokes-contact", !lesson.ContactChallengeSatisfied
                        && !Goal(selector).IsCleared && ball.CurrentPolarity == Polarity.Neutral,
                        "재시작하면 중성 공과 닫힌 출구로 돌아갑니다.");
                    ResetStage(selector, 3);
                    HoldAtExit(selector, true);
                    Next(Phase.DistanceUnobservedExit);
                    break;
            }
        }

                                                                        
        private static void CheckComparison(Phase phase, ElectricStageSelector selector,
            ElectricLearningController2D lesson)
        {
            ElectricBall2D ball = selector.Ball;
            var launcher = Stage<DistanceLauncher2D>(selector, 3);
            switch (phase)
            {
                case Phase.DistanceUnobservedExit:
                    AddCheck("stage03-star-alone-does-not-open-exit", !Goal(selector).IsCleared
                        && !lesson.ComparisonChallengeSatisfied, "별만 가진 공의 출구 진입을 막습니다.");
                    ResetStage(selector, 3);
                    Next(Phase.DistanceReadyWithoutBaseline);
                    break;

                case Phase.DistanceReadyWithoutBaseline:
                    launcher.RequestLaunch();
                    Next(Phase.DistanceLaunchedWithoutBaseline);
                    break;

                case Phase.DistanceLaunchedWithoutBaseline:
                    AddCheck("stage03-launch-without-baseline-is-not-comparison", launcher.HasLaunchSample
                        && lesson.HasResult && !lesson.HasBaseline && !lesson.ComparisonChallengeSatisfied,
                        "실제 발사는 되지만 기준값이 없으므로 목표를 달성하지 않습니다.");
                    HoldAtExit(selector, true);
                    Next(Phase.DistanceNoBaselineExit);
                    break;

                case Phase.DistanceNoBaselineExit:
                    AddCheck("stage03-no-baseline-exit-remains-closed", !Goal(selector).IsCleared,
                        "기준 없이 발사한 뒤 별을 모아도 출구가 닫힙니다.");
                    ResetStage(selector, 3);
                    Next(Phase.DistanceReadyUnchanged);
                    break;

                case Phase.DistanceReadyUnchanged:
                    lesson.RecordBaseline();
                    launcher.RequestLaunch();
                    Next(Phase.DistanceUnchangedLaunched);
                    break;

                case Phase.DistanceUnchangedLaunched:
                    AddCheck("stage03-unchanged-launch-is-not-comparison", lesson.HasBaseline && lesson.HasResult
                        && !lesson.ComparisonChallengeSatisfied && !lesson.IsPreparingTrial,
                        "기준과 같은 거리·전하량으로 발사하면 학습 목표는 미완료입니다.");
                                                                
                                                                  
                    launcher.ResetLaunch();
                    launcher.RequestLaunch();
                    Next(Phase.DistanceAirborneLaunched);
                    break;

                case Phase.DistanceAirborneLaunched:
                    AddCheck("stage03-airborne-distance-drift-cannot-complete", launcher.HasLaunchSample
                        && Mathf.Abs(launcher.LastLaunchSample.Distance - lesson.BaselineSample.Distance) > 0.01f
                        && !lesson.IsPreparingTrial && !lesson.ComparisonChallengeSatisfied,
                        "비행 중 두 번째 실제 발사 거리=" + launcher.LastLaunchSample.Distance
                        + "; 기준=" + lesson.BaselineSample.Distance);
                    HoldAtExit(selector, true);
                    Next(Phase.DistanceInvalidExit);
                    break;

                case Phase.DistanceInvalidExit:
                    AddCheck("stage03-unchanged-and-airborne-exit-remains-closed", !Goal(selector).IsCleared,
                        "준비 상태에서 조건을 바꾸지 않은 발사로는 출구가 열리지 않습니다.");
                    ResetStage(selector, 3);
                    Next(Phase.DistanceReadyBaseline);
                    break;

                case Phase.DistanceReadyBaseline:
                    lesson.RecordBaseline();
                    selector.Lever.SetValue(1f);
                    Next(Phase.DistanceConditionChanged);
                    break;

                case Phase.DistanceConditionChanged:
                    AddCheck("stage03-condition-change-alone-does-not-complete", lesson.HasBaseline
                        && lesson.IsPreparingTrial && !lesson.ComparisonChallengeSatisfied,
                        "기준을 저장하고 레버를 조절했더라도 실제 발사까지 필요합니다.");
                    launcher.RequestLaunch();
                    Next(Phase.DistanceAccepted);
                    break;

                case Phase.DistanceAccepted:
                    AddCheck("stage03-prepared-distance-comparison-completes", lesson.ComparisonChallengeSatisfied
                        && lesson.HasResult && !lesson.IsPreparingTrial
                        && ball.Body.constraints == OriginalConstraints && ball.Body.linearVelocity.sqrMagnitude > 0.1f
                        && Mathf.Abs(lesson.ResultSample.Distance - lesson.BaselineSample.Distance) > 0.01f,
                        "거리 변경 후 실제 발사에서 위치 고정을 해제하고 완료 기록을 남깁니다.");
                    HoldAtStar(selector);
                    Next(Phase.DistanceCollectStar);
                    break;

                case Phase.DistanceCollectStar:
                    AddCheck("stage03-real-star-trigger-collected", ball.Stars >= 1,
                        "비교 발사 뒤 실제 별 트리거를 수집했습니다.");
                    HoldAtExit(selector, false);
                    Next(Phase.DistancePassed);
                    break;

                case Phase.DistancePassed:
                    AddCheck("stage03-comparison-and-star-open-exit", Goal(selector).IsCleared,
                        "준비→기준 저장→거리 조절→발사→별 수집 뒤 실제 출구가 열립니다.");
                    RestorePhysics(ball.Body);
                    lesson.PrepareTrial();
                    lesson.RecordBaseline();
                    AddCheck("stage03-new-baseline-revokes-comparison", lesson.HasBaseline
                        && !lesson.ComparisonChallengeSatisfied,
                        "새 기준을 저장하면 이전 비교 성공으로 다음 출구를 열 수 없습니다.");
                    HoldAtExit(selector, true);
                    Next(Phase.DistanceNewBaselineExit);
                    break;

                case Phase.DistanceNewBaselineExit:
                    AddCheck("stage03-new-baseline-exit-remains-closed", !Goal(selector).IsCleared,
                        "새 기준으로 비교 발사를 하기 전까지 실제 출구가 닫힙니다.");
                    ResetStage(selector, 3);
                    AddCheck("stage03-restart-clears-records", !lesson.HasBaseline && !lesson.HasResult
                        && !lesson.ComparisonChallengeSatisfied && lesson.IsPreparingTrial,
                        "재시작은 기준·결과·완료 기록을 함께 지웁니다.");
                    Next(Phase.DistanceBallChargeBaseline);
                    break;

                case Phase.DistanceBallChargeBaseline:
                    lesson.RecordBaseline();
                    lesson.SetBallCharge(2f);
                    launcher.RequestLaunch();
                    Next(Phase.DistanceBallChargeAccepted);
                    break;

                case Phase.DistanceBallChargeAccepted:
                    AddCheck("stage03-ball-charge-comparison-completes", lesson.ComparisonChallengeSatisfied
                        && Mathf.Abs(lesson.ResultSample.SignedCharge - lesson.BaselineSample.SignedCharge) > 0.5f,
                        "거리를 유지하고 공의 전하량만 바꾼 비교 발사도 인정합니다.");
                    ResetStage(selector, 3);
                    Next(Phase.DistanceSourceChargeBaseline);
                    break;

                case Phase.DistanceSourceChargeBaseline:
                    lesson.RecordBaseline();
                    lesson.SetSourceChargeMagnitude(2f);
                    launcher.RequestLaunch();
                    Next(Phase.DistanceSourceChargeAccepted);
                    break;

                case Phase.DistanceSourceChargeAccepted:
                    AddCheck("stage03-source-charge-comparison-completes", lesson.ComparisonChallengeSatisfied
                        && Mathf.Abs(lesson.ResultSample.SourceCharge - lesson.BaselineSample.SourceCharge) > 0.5f,
                        "거리를 유지하고 전극의 전하량만 바꾼 비교 발사도 인정합니다.");
                    ResetStage(selector, 5);
                    AddCheck("stage-switch-clears-comparison-milestone", !lesson.ComparisonChallengeSatisfied
                        && !lesson.HasBaseline && !lesson.HasResult,
                        "스테이지를 바꾸면 이전 비교 완료 기록이 남지 않습니다.");
                    HoldAtExit(selector, true);
                    Next(Phase.VoltageUnobservedExit);
                    break;
            }
        }

                                                                           
        private static void CheckVoltage(Phase phase, ElectricStageSelector selector,
            ElectricLearningController2D lesson)
        {
            ElectricBall2D ball = selector.Ball;
            var launcher = Stage<VoltageLauncher2D>(selector, 5);
            switch (phase)
            {
                case Phase.VoltageUnobservedExit:
                    AddCheck("stage05-star-alone-does-not-open-exit", !Goal(selector).IsCleared
                        && !lesson.VoltageChallengeSatisfied, "별만 가진 공의 출구 진입을 막습니다.");
                    selector.Lever.SetValue(1f);
                    Next(Phase.VoltageChargeOnly);
                    break;

                case Phase.VoltageChargeOnly:
                    if (launcher.Voltage < lesson.RequiredLaunchVoltage) return;
                    selector.Lever.SetValue(0f);
                    Next(Phase.VoltageChargeOnlyExit);
                    break;

                case Phase.VoltageChargeOnlyExit:
                    AddCheck("stage05-charge-alone-does-not-open-exit", launcher.Voltage >= lesson.RequiredLaunchVoltage
                        && !lesson.VoltageChallengeSatisfied && !Goal(selector).IsCleared,
                        "50 V 이상 충전했어도 발사 전에는 실제 출구가 닫힙니다: " + launcher.Voltage);
                    SessionState.SetFloat(Prefix + "VoltageBefore", launcher.Voltage);
                    launcher.RequestLaunch();
                    Next(Phase.VoltageFailedOutside);
                    break;

                case Phase.VoltageFailedOutside:
                    AddCheck("stage05-failed-outside-request-does-not-complete", !launcher.HasLaunchSample
                        && !lesson.VoltageChallengeSatisfied && !Goal(selector).IsCleared
                        && Mathf.Abs(launcher.Voltage - SessionState.GetFloat(Prefix + "VoltageBefore", 0f)) < 0.01f,
                        "충전 상태에서 발사 영역 밖의 요청은 전압도 목표 기록도 소비하지 않습니다.");
                    ResetStage(selector, 5);
                    HoldInVoltageZone(selector);
                    selector.Lever.SetValue(1f);
                    Next(Phase.VoltageLowCharge);
                    break;

                case Phase.VoltageLowCharge:
                    if (launcher.Voltage < 10f) return;
                    selector.Lever.SetValue(0f);
                    AddCheck("stage05-low-voltage-fixture", launcher.Voltage > 0f
                        && launcher.Voltage < lesson.RequiredLaunchVoltage, "실제 적분으로 충전: " + launcher.Voltage);
                    RestorePhysics(ball.Body);
                    launcher.RequestLaunch();
                    Next(Phase.VoltageLowLaunched);
                    break;

                case Phase.VoltageLowLaunched:
                    AddCheck("stage05-successful-low-voltage-launch-does-not-complete", launcher.HasLaunchSample
                        && launcher.LastLaunchVoltage > 0f && launcher.LastLaunchVoltage < lesson.RequiredLaunchVoltage
                        && ball.Body.linearVelocity.y > 0f && !lesson.VoltageChallengeSatisfied,
                        "실제 낮은 전압 발사는 가능하지만 목표는 미완료: " + launcher.LastLaunchVoltage);
                    HoldAtExit(selector, true);
                    Next(Phase.VoltageLowExit);
                    break;

                case Phase.VoltageLowExit:
                    AddCheck("stage05-low-voltage-exit-remains-closed", !Goal(selector).IsCleared,
                        "50 V 미만 발사 뒤 별을 모아도 실제 출구가 닫힙니다.");
                    ResetStage(selector, 5);
                    HoldInVoltageZone(selector);
                    selector.Lever.SetValue(1f);
                    Next(Phase.VoltageHighCharge);
                    break;

                case Phase.VoltageHighCharge:
                    if (launcher.Voltage < lesson.RequiredLaunchVoltage) return;
                    selector.Lever.SetValue(0f);
                    RestorePhysics(ball.Body);
                    launcher.RequestLaunch();
                    Next(Phase.VoltageHighLaunched);
                    break;

                case Phase.VoltageHighLaunched:
                    AddCheck("stage05-target-launch-completes-and-consumes-voltage", launcher.HasLaunchSample
                        && launcher.LastLaunchVoltage >= lesson.RequiredLaunchVoltage
                        && lesson.VoltageChallengeSatisfied && launcher.Voltage < 0.01f
                        && Mathf.Abs(lesson.LastLaunchVoltage - launcher.LastLaunchVoltage) < 0.01f
                        && ball.Body.linearVelocity.y > 0f,
                        "발사 직전 전압=" + launcher.LastLaunchVoltage + "; 발사 후 전압=" + launcher.Voltage);
                    HoldAtExit(selector, false);
                    Next(Phase.VoltageNeedsStar);
                    break;

                case Phase.VoltageNeedsStar:
                    AddCheck("stage05-target-launch-still-requires-star", !Goal(selector).IsCleared
                        && ball.Stars == 0, "50 V 발사 목표를 달성해도 별이 없으면 출구는 닫힙니다.");
                    HoldAtStar(selector);
                    Next(Phase.VoltageCollectStar);
                    break;

                case Phase.VoltageCollectStar:
                    AddCheck("stage05-real-star-trigger-collected", ball.Stars >= 1,
                        "목표 전압 발사 뒤 실제 별 트리거를 수집했습니다.");
                    HoldAtExit(selector, false);
                    Next(Phase.VoltagePassed);
                    break;

                case Phase.VoltagePassed:
                    AddCheck("stage05-target-launch-and-star-open-exit", Goal(selector).IsCleared,
                        "50 V 이상 실제 발사 기록과 별을 가진 공이 실제 출구를 통과합니다.");
                    ResetStage(selector, 5);
                    AddCheck("stage05-restart-revokes-launch", !lesson.VoltageChallengeSatisfied
                        && !launcher.HasLaunchSample && launcher.LastLaunchVoltage == 0f
                        && launcher.Voltage == 0f && !Goal(selector).IsCleared,
                        "재시작은 저장 전압·발사 표본·완료 기록을 함께 초기화합니다.");
                    Finish();
                    break;
            }
        }

        private static RigidbodyConstraints2D OriginalConstraints =>
            (RigidbodyConstraints2D)SessionState.GetInt(Prefix + "Constraints", 0);

                                                                              
        private static void RestorePhysics(Rigidbody2D body)
        {
            body.constraints = OriginalConstraints;
            body.gravityScale = 0f;
        }

        private static void ResetStage(ElectricStageSelector selector, int stageNumber)
        {
            RestorePhysics(selector.Ball.Body);
            selector.SelectStage(stageNumber);
            selector.Lever.SetValue(0f);
        }

                                                         
        private static void Hold(Rigidbody2D body, Vector2 position)
        {
            body.simulated = true;
            body.gravityScale = 0f;
            body.constraints = RigidbodyConstraints2D.FreezeAll;
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
            body.position = position;
            Physics2D.SyncTransforms();
        }

        private static void HoldAtExit(ElectricStageSelector selector, bool grantStar)
        {
            if (grantStar && selector.Ball.Stars == 0)
                selector.Ball.CollectStar();
            Hold(selector.Ball.Body, Goal(selector).transform.position);
        }

        private static void HoldAtStar(ElectricStageSelector selector) =>
            Hold(selector.Ball.Body, Stage<ElectricStar2D>(selector, selector.SelectedStage).transform.position);

        private static void HoldInVoltageZone(ElectricStageSelector selector)
        {
            Transform zone = selector.StageRoots[3].transform.Find("Launch Zone");
            if (zone == null || !zone.TryGetComponent(out BoxCollider2D collider))
                throw new InvalidOperationException("Stage 5 launch zone is missing.");
            Hold(selector.Ball.Body, collider.bounds.center);
        }

        private static ElectricGoal2D Goal(ElectricStageSelector selector) =>
            Stage<ElectricGoal2D>(selector, selector.SelectedStage);

        private static string DescribeGoalState(ElectricStageSelector selector)
        {
            ElectricBall2D ball = selector.Ball;
            ElectricStar2D star = Stage<ElectricStar2D>(selector, selector.SelectedStage);
            return "stars=" + ball.Stars + "; goal=" + Goal(selector).IsCleared
                + "; ballEnabled=" + ball.enabled + "; simulated=" + ball.Body.simulated
                + "; starCollected=" + star.IsCollected + "; starActive=" + star.gameObject.activeSelf;
        }

        private static T Stage<T>(ElectricStageSelector selector, int stageNumber) where T : Component
        {
            T component = selector.GetStageComponent<T>(stageNumber);
            if (component == null)
                throw new InvalidOperationException(typeof(T).Name + " missing from stage " + stageNumber);
            return component;
        }

        private static void Next(Phase phase, float seconds = 0.08f)
        {
            SessionState.SetInt(Prefix + "Phase", (int)phase);
            SessionState.SetFloat(Prefix + "Deadline", Time.fixedTime + seconds);
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

        private static void Save(Report report) => SessionState.SetString(Prefix + "Report", JsonUtility.ToJson(report));

                                                                            
        private static void Finish()
        {
            if (!Application.isBatchMode) return;
            Report report = Load();
            bool passed = report.errors.Count == 0 && report.checks.Count >= 35;
            foreach (Check check in report.checks)
                passed &= check.passed;
            report.status = passed ? "passed" : "failed";
            double playStarted = Timestamp("PlayStarted");
            report.activeSeconds = playStarted > 0d
                ? (float)(EditorApplication.timeSinceStartup - playStarted) : 0f;
            string directory = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Logs");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "ElectricGoalGatePlayModeReport.json"),
                JsonUtility.ToJson(report, true));
            SessionState.SetBool(Prefix + "Active", false);
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= CaptureLog;
            EditorApplication.Exit(passed ? 0 : 1);
        }
    }
}
