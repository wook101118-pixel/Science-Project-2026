from pathlib import Path

root=Path(__file__).resolve().parents[1]
s=(root/'tmp/QaPlayerRouteProbe.cs').read_text(encoding='utf-8-sig')
def change(old,new):
    global s
    if old not in s: raise RuntimeError('Expected source not found: '+old[:100])
    s=s.replace(old,new)
change('QaPlayerRouteProbe','QaBeginnerRoutesProbe')
change('--qa-player-routes','--qa-beginner-routes')
change('private const float TargetVoltage = 50f;','private static float TargetVoltage = 50f;')
change('public int retryCount;','public int retryCount, selectedPlan;\n        public bool chosenPlanSatisfied, planRecordSaved;\n        public string planResult;')
change('public List<Route> routes = new List<Route>();','public List<Route> routes = new List<Route>();\n        public List<string> screenshots = new List<string>();\n        public bool alternativePlan;\n        public int resultContinueCalls;')
change('private Report report;','private bool alternativePlan;\n    private bool captureAndContinue;\n    private Report report;')
change('int onlyStage = 0;','int onlyStage = 0;\n        bool alternative = false;')
change('if (arg == "--qa-beginner-routes") optedIn = true;','if (arg == "--qa-beginner-routes") optedIn = true;\n            if (arg == "--qa-plan-alt") alternative = true;')
change('.Initialize(path, onlyStage);','.Initialize(path, onlyStage, alternative);')
change('private void Initialize(string path, int onlyStage)','private void Initialize(string path, int onlyStage, bool alternative)')
change('reportPath = path;','reportPath = path;\n        alternativePlan = alternative;\n        TargetVoltage = alternative ? 55f : 50f;')
change('startedUtc = DateTime.UtcNow.ToString("O"), unity = Application.unityVersion,','alternativePlan = alternative, startedUtc = DateTime.UtcNow.ToString("O"), unity = Application.unityVersion,')
change('if (awaitingCompletion)','if (captureAndContinue) return;\n            if (selector != null && selector.SelectedStage < expectedStage\n                && selector.GetComponent<ElectricStageProgression>()?.IsResultOpen == true\n                && !SceneTransitionController.IsBusy)\n            {\n                captureAndContinue = true;\n                StartCoroutine(CaptureResultAndContinue(false));\n                return;\n            }\n            if (awaitingCompletion)')
change('report.finalGameComplete = true;\n                    Finish();','report.finalGameComplete = true;\n                    captureAndContinue = true;\n                    StartCoroutine(CaptureResultAndContinue(true));')
change('selector.Lever.SetValue(stage == 2 || stage == 5 ? 1f : 0f);','selector.Lever.SetValue(stage == 2 || stage == 5 ? 1f : stage == 3 && alternativePlan ? .95f : 0f);\n        var plan = selector.GetComponent<ElectricExperimentPlan2D>();\n        if (plan != null && plan.ShowPlans && !plan.SelectPlan(alternativePlan ? 1 : 0))\n            throw new InvalidOperationException("Saved plan selection rejected before attempt.");\n        if (stage == 3 && alternativePlan) lesson.SetBallCharge(.5f);')
change('selector.Lever.SetValue(0f);\n                    Change(Phase.Star);','selector.Lever.SetValue(.65f);\n                    if (!alternativePlan)\n                        while (selector.Ball.CurrentPolarity != Polarity.Neutral) selector.Ball.Player.CycleState();\n                    Change(Phase.Star);')
change('selector.Lever.SetValue(0.95f);','if (alternativePlan) lesson.SetBallCharge(1f);\n                else selector.Lever.SetValue(0.95f);')
change('current.status = success ? "passed" : "failed";','var plan = selector.GetComponent<ElectricExperimentPlan2D>();\n        if (plan != null && plan.ShowPlans)\n        {\n            plan.RecordClear();\n            current.selectedPlan = plan.SelectedPlan;\n            current.chosenPlanSatisfied = plan.PlanSatisfied;\n            current.planRecordSaved = (plan.CompletedPlans & (1 << plan.SelectedPlan)) != 0;\n            current.planResult = plan.ResultSummary;\n            success &= current.chosenPlanSatisfied && current.planRecordSaved;\n        }\n        current.status = success ? "passed" : "failed";')
change('private static T ReadField<T>', '''private System.Collections.IEnumerator CaptureResultAndContinue(bool final)
    {
        yield return new WaitForSecondsRealtime(.2f);
        yield return new WaitForEndOfFrame();
        Texture2D capture = ScreenCapture.CaptureScreenshotAsTexture();
        string path = Path.Combine(Path.GetDirectoryName(reportPath), Path.GetFileNameWithoutExtension(reportPath)
            + "-stage" + selector.SelectedStage + "-result.png");
        if (capture == null) { Fail(new InvalidOperationException("Result screen capture failed.")); yield break; }
        File.WriteAllBytes(path, capture.EncodeToPNG());
        UnityEngine.Object.Destroy(capture);
        report.screenshots.Add(path);
        if (final) { Finish(); yield break; }
        var progression = selector.GetComponent<ElectricStageProgression>();
        if (progression == null || !progression.IsResultOpen)
        { Fail(new InvalidOperationException("Real goal did not open saved result UI.")); yield break; }
        progression.ContinueNextStage();
        report.resultContinueCalls++;
        captureAndContinue = false;
        Write();
    }

    private static T ReadField<T>''')
(root/'tmp/QaBeginnerRoutesProbe.cs').write_text(s,encoding='utf-8')
print('Created QaBeginnerRoutesProbe with actual result continuation, base/alternate plans, exact target voltage, and screenshots.')
