using TMPro;
using UnityEngine;

namespace sung_wook
{
    /// <summary>Saved presentation labels observe the existing experiment; this view never changes physics or completion.</summary>
    [DisallowMultipleComponent]
    public sealed class SciencePresentationView : MonoBehaviour
    {
        [SerializeField, Range(1, 5)] private int stageNumber = 1;
        [SerializeField] private TMP_Text chapterText;
        [SerializeField] private TMP_Text questionText;
        [SerializeField] private TMP_Text observationHeading;
        [SerializeField] private TMP_Text observationValue;
        [SerializeField] private TMP_Text observationDetail;
        [SerializeField] private TMP_Text principleText;
        [SerializeField] private TMP_Text modelNoteText;
        [SerializeField] private TMP_Text progressText;
        [SerializeField] private TMP_Text actionText;
        [SerializeField] private TMP_Text controlsText;

        private ElectricStageSelector selector;
        private ElectricLearningController2D lesson;
        private TutorialController2D tutorial;
        private VoltageLauncher2D voltageLauncher;
        private float observedLaunchVoltage;
        private float observedLaunchSpeed;
        private bool observedVoltageLaunch;
        private Canvas presentationCanvas;
        private Camera experimentCamera;
        private ElectricGameCompleteView resultView;
        private PauseMenuController pauseMenu;
        private Vector2 previousCanvasSize;

        /// <summary>Scientific conclusion backed by this attempt's existing observations; no progress is changed.</summary>
        public string CompletionSummary
        {
            get
            {
                if (selector == null || lesson == null) return "관찰을 마쳤습니다. 실험 결과를 정리해 보세요.";
                switch (stageNumber)
                {
                    case 1:
                        return "중성 0 → 양전하 + → 음전하 - → 중성 0\n"
                            + "공의 색과 기호로 세 전하 상태를 관찰했습니다.\n\n"
                            + "중성은 순전하가 0인 상태입니다.\n다음 실험에서는 실제 접촉으로 전하가 바뀝니다.";
                    case 2:
                        string charge = lesson.TryGetDisplaySample(out ElectricForceSample contact) && contact.UsesSIUnits
                            ? "완료 시 공의 전하: " + Signed(contact.SignedChargeCoulombs * 1000000f) + " μC"
                            : "완료 시 공의 상태: " + ChargeName(selector.Ball.CurrentPolarity);
                        return "중성 공과 전극의 접촉 대전을 확인했습니다.\n"
                            + "같은 부호는 밀고, 다른 부호는 당깁니다.\n\n" + charge
                            + "\n전극에서 떨어진 뒤에도 공의 전하는 유지됩니다.\nSpace 전환은 별도의 실험 조작입니다.";
                    case 3:
                        ElectricForceSample before = lesson.QualifiedBaselineSample;
                        ElectricForceSample after = lesson.QualifiedResultSample;
                        float ratio = before.ForceMagnitude > .0001f ? after.ForceMagnitude / before.ForceMagnitude : 0f;
                        return $"발사 순간의 힘: {before.ForceMagnitude:0.0} → {after.ForceMagnitude:0.0}\n"
                            + $"기준 대비 {ratio:0.00}배 · 같은 출발점에서 비교\n"
                            + $"q: {before.SignedCharge:0.##} → {after.SignedCharge:0.##}   Q: {before.SourceCharge:0.##} → {after.SourceCharge:0.##}\n\n"
                            + "거리가 같으면 힘의 크기는 |qQ|에 비례합니다.\n"
                            + (before.IsLimited || after.IsLimited ? "이번 관측에는 모형의 힘 제한이 적용됐습니다." : "게임 단위의 힘 비교이며, 높이의 배수는 아닙니다.");
                    case 4:
                        return "음전하 상승 → 중성 하강 → 음전하 제동\n"
                            + "세 구간의 실제 움직임을 관찰했습니다.\n\n"
                            + "음전하의 전기력은 전기장과 반대 방향입니다.\n"
                            + "중성에서도 전기장과 중력은 존재합니다.\n"
                            + "위쪽 전기력은 하강하는 공을 늦출 수 있습니다.";
                    default:
                        float voltage = observedVoltageLaunch ? observedLaunchVoltage : lesson.LastLaunchVoltage;
                        return $"발사에 사용한 전압: {voltage:0.0} V\n"
                            + (observedVoltageLaunch ? $"모형의 초기 속력: {observedLaunchSpeed:0.00} (게임 단위)\n" : "")
                            + "\n전기적 에너지가 운동 에너지로 바뀌었습니다.\n"
                            + "qV = ½mv² · 전하량과 질량이 같을 때,\n"
                            + "전압 두 배는 속력 약 1.41배에 해당합니다.";
                }
            }
        }

        public static string Chapter(int stage)
        {
            switch (stage)
            {
                case 1: return "01   실험 준비";
                case 2: return "02   접촉과 대전";
                case 3: return "03   전하량과 전기력";
                case 4: return "04   전기장 속 운동";
                default: return "05   전압과 에너지";
            }
        }

        public static string Question(int stage)
        {
            switch (stage)
            {
                case 1: return "공의 전하가 바뀌면, 무엇이 달라질까?";
                case 2: return "전극에 닿은 공은 왜 밀려날까?";
                case 3: return "같은 거리에서 전하량을 두 배로 하면?";
                case 4: return "음전하가 받는 힘은 어느 방향일까?";
                default: return "전압을 높이면 발사 속력은 어떻게 달라질까?";
            }
        }

        public static string Principle(int stage)
        {
            switch (stage)
            {
                case 1: return "중성 0\n양전하 +  /  음전하 -";
                case 2: return "같은 부호는 척력\n다른 부호는 인력";
                case 3: return "F = k |qQ| / r²\n거리가 같으면 F ∝ |qQ|";
                case 4: return "F = qE\n음전하의 힘은 전기장과 반대";
                default: return "qV = ½mv²\n전기적 에너지 → 운동 에너지";
            }
        }

        public static string ModelNote(int stage)
        {
            switch (stage)
            {
                case 1: return "공의 색과 기호가 실제 전하 상태를 나타냅니다.\nSpace 전환은 비교 실험을 위한 조작입니다.";
                case 2: return "전원에 연결된 전극 모형입니다.\n접촉 대전 뒤 점전하의 힘을 계산합니다. 중성 물체의 분극은 생략합니다.";
                case 3: return "발사 순간의 힘 비교 · 게임 단위\n힘을 0.4초 충격량으로 바꿉니다.\n힘의 배수와 높이의 배수는 다릅니다.";
                case 4: return "균일 전기장 · 게임 단위\n중성에서도 전기장은 존재합니다.\n전기력은 0, 중력은 작용합니다.";
                default: return "V는 전위 하강량 · 게임 모형 값\n정지 출발, 손실을 생략한 발사입니다. 전하량·질량·속력은 게임 단위입니다.";
            }
        }

        public static string Controls(int stage)
        {
            string common = "A / D 이동    Space 전하 전환    R 다시 실험    Esc 메뉴";
            if (stage == 3) return "B 기준 저장    레버 Q 조절    E 발사    T 다시 준비    A / D 이동    Esc 메뉴";
            if (stage == 4) return "레버 장 방향    X 음전하    C 중성    Z 양전하    A / D 이동    Esc 메뉴";
            if (stage == 5) return "레버 전압 충전    E 발사    A / D 이동    R 다시 실험    Esc 메뉴";
            return stage == 2 ? "레버 전극 충전    " + common : common;
        }

        public void Configure(int stage, TMP_Text chapter, TMP_Text question, TMP_Text heading,
            TMP_Text value, TMP_Text detail, TMP_Text principle, TMP_Text note,
            TMP_Text progress, TMP_Text action, TMP_Text controls)
        {
            stageNumber = Mathf.Clamp(stage, 1, 5);
            chapterText = chapter;
            questionText = question;
            observationHeading = heading;
            observationValue = value;
            observationDetail = detail;
            principleText = principle;
            modelNoteText = note;
            progressText = progress;
            actionText = action;
            controlsText = controls;
            Preview();
        }

        public void Preview()
        {
            Set(chapterText, Chapter(stageNumber));
            Set(questionText, Question(stageNumber));
            Set(observationHeading, "지금 관찰하는 것");
            Set(principleText, Principle(stageNumber));
            Set(modelNoteText, ModelNote(stageNumber));
            Set(controlsText, Controls(stageNumber));
            Set(observationValue, stageNumber == 5 ? "0.0 V" : stageNumber == 3 ? "기준을 정해요" : stageNumber == 4 ? "E ↓  ·  F ↓" : "중성  0");
            Set(observationDetail, stageNumber == 5 ? "목표 50 V\n레버로 에너지를 저장합니다." : "장치를 조작하며 변화를 관찰하세요.");
            Set(progressText, "실험 → 관찰 → 별 → 출구");
            Set(actionText, stageNumber == 1 ? "가만히 두면 공은 바닥에서 튀어 오릅니다." : "장치를 조작하고 측정값의 변화를 관찰하세요.");
        }

        private void Start()
        {
            presentationCanvas = GetComponent<Canvas>();
            foreach (GameObject root in gameObject.scene.GetRootGameObjects())
            {
                if (selector == null) selector = root.GetComponentInChildren<ElectricStageSelector>(true);
                if (resultView == null) resultView = root.GetComponentInChildren<ElectricGameCompleteView>(true);
                if (pauseMenu == null) pauseMenu = root.GetComponentInChildren<PauseMenuController>(true);
                foreach (Camera candidate in root.GetComponentsInChildren<Camera>(true))
                    if (candidate.CompareTag("MainCamera")) experimentCamera = candidate;
            }
            if (selector == null) return;
            lesson = selector.GetComponent<ElectricLearningController2D>();
            tutorial = selector.GetComponent<TutorialController2D>();
            voltageLauncher = selector.GetStageComponent<VoltageLauncher2D>(5);
            if (voltageLauncher != null) voltageLauncher.LaunchedWithVoltage += ObserveVoltageLaunch;
            Preview();
            Reflow();
        }

        private void ObserveVoltageLaunch(float voltage)
        {
            if (selector == null || selector.Ball == null) return;
            observedVoltageLaunch = true;
            observedLaunchVoltage = voltage;
            observedLaunchSpeed = ElectricMath.SpeedFromVoltage(selector.Ball.SignedCharge, voltage, selector.Ball.Body.mass);
        }

        private void LateUpdate()
        {
            if (presentationCanvas != null)
                presentationCanvas.enabled = !(resultView != null && resultView.IsResultOpen)
                    && !(pauseMenu != null && pauseMenu.IsPaused);
            Reflow();
            if (selector == null || selector.Ball == null || lesson == null) return;
            ElectricBall2D ball = selector.Ball;
            Set(actionText, stageNumber == 1 && tutorial != null ? tutorial.CurrentTaskText : CurrentAction());
            Set(progressText, Progress());
            switch (stageNumber)
            {
                case 1:
                    Set(observationValue, ChargeName(ball.CurrentPolarity));
                    Set(observationDetail, "색과 기호를 함께 보세요.\n" + (ball.CurrentPolarity == Polarity.Neutral
                        ? "중성은 전체 전하의 합이 0인 상태입니다." : ball.CurrentPolarity == Polarity.Positive
                            ? "양전하를 띤 공입니다. 다음 실험에서 전극과의 힘을 관찰합니다."
                            : "음전하를 띤 공입니다. 양전하와 힘의 방향을 비교할 수 있습니다."));
                    break;
                case 2: ContactObservation(ball); break;
                case 3: ComparisonObservation(); break;
                case 4: FieldObservation(ball); break;
                case 5: VoltageObservation(); break;
            }
        }

        /// <summary>Reflow saved objects and match the experiment viewport to the actual canvas dimensions.</summary>
        private void Reflow()
        {
            if (presentationCanvas == null) presentationCanvas = GetComponent<Canvas>();
            if (presentationCanvas == null) return;
            RectTransform canvasRect = presentationCanvas.transform as RectTransform;
            if (canvasRect == null || canvasRect.rect.width <= 0f || canvasRect.rect.height <= 217f) return;
            Vector2 size = canvasRect.rect.size;
            if ((size - previousCanvasSize).sqrMagnitude < .01f) return;
            previousCanvasSize = size;
            float width = size.x, height = size.y;
            float sideWidth = width * .23f, sideHeight = height - 217f;
            float vertical = Mathf.Min(1f, sideHeight / 683f);
            float innerWidth = Mathf.Max(120f, sideWidth - 80f);
            RectTransform header = chapterText != null ? chapterText.transform.parent as RectTransform : null;
            RectTransform footer = actionText != null ? actionText.transform.parent as RectTransform : null;
            RectTransform card = observationHeading != null ? observationHeading.transform.parent as RectTransform : null;
            RectTransform side = card != null ? card.parent as RectTransform : null;

            Box(header, 0, 0, width, 118);
            ChildBox(header, "Header Rule", 24, 116, width - 48, 1);
            TextBox(chapterText, 32, 14, width - 400, 26, 19);
            TextBox(questionText, 32, 48, width - 64, 56, Mathf.Min(34f, 34f * width / 1600f));
            ChildBox(header, "Series", width - 270, 17, 238, 23);

            Box(side, width * .77f, 118, sideWidth, sideHeight);
            ChildBox(side, "Observation Rule", 0, 16, 1, Mathf.Max(1, sideHeight - 32));
            Box(card, 18, 24 * vertical, sideWidth - 36, 310 * vertical);
            TextBox(observationHeading, 22, 22 * vertical, innerWidth, 28 * vertical, 18 * vertical);
            TextBox(observationValue, 22, 66 * vertical, innerWidth, 100 * vertical,
                Mathf.Min(40f, 40f * innerWidth / 288f) * vertical);
            TextBox(observationDetail, 22, 184 * vertical, innerWidth, 104 * vertical, 19 * vertical);
            TextBox(principleText, 40, 360 * vertical, innerWidth, 84 * vertical, 23 * vertical);
            ChildBox(side, "Note Rule", 40, 452 * vertical, innerWidth, 1);
            TextBox(modelNoteText, 40, 470 * vertical, innerWidth, 112 * vertical, 16 * vertical);
            TextBox(progressText, 40, sideHeight - 81 * vertical, innerWidth, 64 * vertical, 18 * vertical);

            Box(footer, 0, height - 99, width, 99);
            ChildBox(footer, "Footer Rule", 24, 0, width - 48, 1);
            ChildBox(footer, "Action Accent", 32, 17, 4, 30);
            TextBox(actionText, 50, 16, width - 82, 34, Mathf.Min(23f, 23f * width / 1600f));
            TextBox(controlsText, 50, 62, width - 82, 26, Mathf.Min(16f, 16f * width / 1600f));
            if (experimentCamera != null)
                experimentCamera.rect = new Rect(0, 99f / height, .77f, 1f - 217f / height);
        }

        private static void TextBox(TMP_Text text, float x, float y, float width, float height, float fontSize)
        {
            if (text == null) return;
            Box(text.rectTransform, x, y, width, height);
            text.fontSize = fontSize;
        }

        private static void ChildBox(RectTransform parent, string name, float x, float y, float width, float height)
        {
            if (parent != null) Box(parent.Find(name) as RectTransform, x, y, width, height);
        }

        private static void Box(RectTransform rect, float x, float y, float width, float height)
        {
            if (rect == null) return;
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }

        private string CurrentAction()
        {
            if (lesson.IsStageCleared) return "관찰을 마쳤습니다. 결과를 정리해 보세요.";
            if (stageNumber == 3 && lesson.IsPreparingTrial)
            {
                if (!lesson.HasBaseline) return "B로 기준을 저장하세요. 전극과 공의 위치는 고정됩니다.";
                if (lesson.TryGetDisplaySample(out ElectricForceSample sample)
                    && !ElectricComparisonRecord.HaveConditionsChanged(lesson.BaselineSample, sample))
                    return "레버로 Q를 바꾸세요. q와 거리는 그대로 두고 비교합니다.";
                return "힘의 변화를 확인한 뒤 E로 발사하세요.";
            }
            return lesson.CurrentTaskText;
        }

        private string Progress()
        {
            if (stageNumber == 1 && tutorial != null)
                return Mark(tutorial.HasBounced) + " 바운스   "
                    + Mark(tutorial.HasMovedLeft && tutorial.HasMovedRight) + " 이동\n"
                    + Mark(tutorial.HasCycledCharge) + " 전하 관찰   "
                    + Mark(tutorial.HasCollectedStar) + " 별";
            if (stageNumber == 4)
                return Mark(lesson.HasFieldAscent) + " 음전하 상승\n"
                    + Mark(lesson.HasNeutralFieldDescent) + " 중성 하강   " + Mark(lesson.HasRestoredNegativeField) + " 제동";
            return Mark(lesson.CurrentLearningGoalSatisfied) + " 실험 완료\n"
                + Mark(selector.Ball.Stars > 0) + " 별 수집 → 출구";
        }

        private void ContactObservation(ElectricBall2D ball)
        {
            Set(observationHeading, lesson.ContactChallengeSatisfied ? "접촉 뒤의 전하" : "접촉 전의 전하");
            if (!lesson.TryGetDisplaySample(out ElectricForceSample sample)) return;
            Set(observationValue, sample.UsesSIUnits ? Signed(sample.SignedChargeCoulombs * 1000000f) + " μC" : ChargeName(ball.CurrentPolarity));
            float product = sample.SignedCharge * sample.SourceCharge;
            string relation = Mathf.Abs(product) < 0.000001f ? "전기력 0" : product > 0f ? "같은 부호 · 서로 밀어냄" : "다른 부호 · 서로 끌어당김";
            Set(observationDetail, relation + "\n" + (sample.UsesSIUnits
                ? $"거리 {sample.DistanceMeters:0.00} m  /  힘 {sample.ForceNewtons.magnitude:0.00} N"
                : $"거리 {sample.Distance:0.00}  /  힘 {sample.ForceMagnitude:0.00}"));
        }

        private void ComparisonObservation()
        {
            bool hasSample = lesson.TryGetDisplaySample(out ElectricForceSample sample);
            if (lesson.HasResult) { sample = lesson.ResultSample; hasSample = true; }
            if (!hasSample) return;
            Set(observationHeading, lesson.HasResult ? "발사에 사용한 힘" : lesson.HasBaseline ? "기준과 비교" : "고정된 거리에서 측정");
            if (!lesson.HasBaseline)
            {
                Set(observationValue, $"F  {sample.ForceMagnitude:0.0}");
                Set(observationDetail, $"q {sample.SignedCharge:0.##}  ·  Q {sample.SourceCharge:0.##}\n거리 r {sample.Distance:0.00}  (게임 단위)");
                return;
            }
            ElectricForceSample baseline = lesson.BaselineSample;
            float ratio = baseline.ForceMagnitude > 0.0001f ? sample.ForceMagnitude / baseline.ForceMagnitude : 0f;
            Set(observationValue, $"{baseline.ForceMagnitude:0.0} → {sample.ForceMagnitude:0.0}");
            Set(observationDetail, $"기준 대비 힘 {ratio:0.00}배\nq {sample.SignedCharge:0.##}  ·  Q {sample.SourceCharge:0.##}  ·  r {sample.Distance:0.00}"
                + (sample.IsLimited || baseline.IsLimited ? "\n힘 제한이 적용된 조건입니다." : "\n힘과 전하량: 게임 단위"));
        }

        private void FieldObservation(ElectricBall2D ball)
        {
            if (!lesson.TryGetDisplaySample(out ElectricForceSample sample)) return;
            Set(observationHeading, sample.IsInRange ? "전기장과 전기력의 방향" : "전기장 영역 밖");
            Set(observationValue, $"E {Arrow(sample.Field.y)}\nF {Arrow(sample.Force.y)}");
            string charge = ChargeName(ball.CurrentPolarity);
            Set(observationDetail, charge + "\n" + (!sample.IsInRange ? "두 판 사이로 들어가 관찰하세요."
                : ball.CurrentPolarity == Polarity.Neutral ? "전기력은 0 · 중력은 작용"
                : ball.CurrentPolarity == Polarity.Negative ? "전기력은 전기장과 반대 방향" : "전기력은 전기장과 같은 방향"));
        }

        private void VoltageObservation()
        {
            if (voltageLauncher != null && !voltageLauncher.HasLaunchSample) observedVoltageLaunch = false;
            Set(observationHeading, observedVoltageLaunch ? "발사에 사용한 전압" : "저장된 전압");
            Set(observationValue, $"{(observedVoltageLaunch ? observedLaunchVoltage : lesson.CurrentVoltage):0.0} V");
            Set(observationDetail, observedVoltageLaunch
                ? $"모형 속력 {observedLaunchSpeed:0.00} (게임 단위)\n발사 후 저장 전압 {lesson.CurrentVoltage:0.0} V"
                : $"목표 {lesson.RequiredLaunchVoltage:0} V\n충전 → E 발사 → 운동 관찰");
        }

        private static string ChargeName(Polarity charge) => charge == Polarity.Positive ? "양전하  +" : charge == Polarity.Negative ? "음전하  -" : "중성  0";
        private static string Arrow(float value) => value > 0.001f ? "↑" : value < -0.001f ? "↓" : "0";
        private static string Signed(float value) => value > 0f ? "+" + value.ToString("0.#") : value.ToString("0.#");
        private static string Mark(bool done) => done ? "[완료]" : "[대기]";
        private static void Set(TMP_Text target, string value) { if (target != null && target.text != value) target.text = value; }
        private void OnDestroy() { if (voltageLauncher != null) voltageLauncher.LaunchedWithVoltage -= ObserveVoltageLaunch; }
    }
}
