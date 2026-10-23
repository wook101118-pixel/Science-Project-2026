using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace sung_wook
{
    /// <summary>저장된 UI를 재배치해 현재 행동·관측·입력 결과를 항상 따로 읽을 수 있게 한다.</summary>
    [DisallowMultipleComponent]
    public sealed class ElectricStageHudView : MonoBehaviour
    {
        [SerializeField] private TMP_Text stageNumberText;
        [SerializeField] private TMP_Text stageNameText;
        [SerializeField] private TMP_Text questText;
        [SerializeField] private TMP_Text controlsText;
        [SerializeField] private TMP_Text objectiveBodyText;
        [SerializeField] private RectTransform planChoices;

        private ElectricStageSelector selector;
        private TutorialController2D tutorial;
        private CollapsiblePanel help;
        private Canvas canvas;
        private int layoutWidth, layoutHeight;
        private float layoutScale;
        private bool layoutPlans;

        public static string StageName(int stage)
        {
            switch (stage)
            {
                case 1: return "실험 준비";
                case 3: return "전하량과 전기력";
                case 4: return "전기장 속 운동";
                case 5: return "전압과 에너지";
                default: return "접촉과 대전";
            }
        }

        public static string Instructions(int stage)
        {
            switch (stage)
            {
                case 1: return "자동 바운스 · A/D 양쪽 이동\nSpace 전하 0→+→-→0\n색과 기호로 전하 상태 관찰\n별을 모아 출구로 이동";
                case 3: return "전극·거리 고정: B 기준 → 레버 Q → E\n발사 뒤 A/D 착지 · T 재준비\n준비 중: Z/X/C q=.5/1/2, V/N Q=.5/1\n힘의 배수를 비교합니다. 수치는 게임 단위입니다.";
                case 4: return "X 음전하로 장 안에서 연속 1m 상승\n별 → C 중성으로 연속 1m 하강\n하강 중 X 음전하 제동 0.10초\nZ 양전하 / X 음전하 / C 중성 · 위치 유지";
                case 5: return "50 V 이상 충전 → 전극 사이에서 E\n발사 뒤 A/D로 착지 조절\n전압이 속력으로 전환: qV = 0.5mv²";
                default: return "중성 공을 충전된 전극에 접촉\n레버 중앙 중성 / 양쪽 +와 −\n같은 부호는 밀고, 반대는 당깁니다.";
            }
        }

        public void PreviewStage(int stage)
        {
            if (stageNumberText != null) stageNumberText.text = stage.ToString();
            if (stageNameText != null) stageNameText.text = StageName(stage);
            if (objectiveBodyText != null) objectiveBodyText.text = Instructions(stage);
        }

        private void Start()
        {
            canvas = GetComponent<Canvas>();
            if (objectiveBodyText != null) help = objectiveBodyText.GetComponentInParent<CollapsiblePanel>();
            foreach (GameObject root in gameObject.scene.GetRootGameObjects())
            {
                if (selector == null) selector = root.GetComponentInChildren<ElectricStageSelector>();
                if (tutorial == null) tutorial = root.GetComponentInChildren<TutorialController2D>();
            }
            PreviewStage(tutorial != null ? 1 : selector != null ? selector.SelectedStage : 2);
        }

        private void LateUpdate()
        {
            if (selector == null && tutorial == null) return;
            int stage = tutorial != null ? 1 : selector.SelectedStage;
            ElectricLearningController2D lesson = selector != null ? selector.GetComponent<ElectricLearningController2D>() : null;
            ElectricStageSession2D session = selector != null ? selector.GetComponent<ElectricStageSession2D>() : null;
            ElectricExperimentPlan2D plan = selector != null ? selector.GetComponent<ElectricExperimentPlan2D>() : null;
            bool hasPlans = plan != null && plan.ShowPlans;
            ApplyReadableLayout(hasPlans);
            if (tutorial == null && (lesson == null || selector.Ball == null)) return;

            string task = tutorial != null ? tutorial.CurrentTaskText : lesson.CurrentTaskText;
            if (hasPlans && !string.IsNullOrEmpty(plan.CurrentTaskOverride)) task = plan.CurrentTaskOverride;
            string status = tutorial != null ? tutorial.StatusText : Status(lesson);
            // 현재 행동은 항상 남긴다. 최근 입력 결과는 아래쪽의 별도 행에만 표시한다.
            if (questText != null) questText.text = task + "\n" + status;

            string feedback = tutorial != null ? tutorial.RecentFeedback : session != null ? session.RecoveryFeedback : "";
            if (string.IsNullOrEmpty(feedback) && lesson != null) feedback = lesson.RecentFeedback;
            if (controlsText != null)
            {
                string basic = "자동 바운스 · A/D 이동 · Space 전하 0→+→−→0";
                string retry = "Backspace 발판부터 · R 전체 초기화 · Esc 메뉴";
                string last = string.IsNullOrEmpty(feedback) ? "레버 드래그 · 도움말을 열면 안전 정지" : "입력: " + feedback;
                controlsText.text = Screen.width < 1300 ? basic + "\n" + retry + "\n" + last
                    : basic + " · " + retry + "\n" + last;
            }
            if (objectiveBodyText != null)
            {
                string detail = tutorial != null ? tutorial.ShortInstructions : Instructions(stage);
                if (hasPlans) detail += "\n" + FirstLine(plan.DetailText);
                if (stage > 1 && session != null && !string.IsNullOrEmpty(session.ChallengeCompactText))
                    detail += "\n" + session.ChallengeCompactText;
                objectiveBodyText.text = detail;
            }
        }

        private string Status(ElectricLearningController2D lesson)
        {
            string charge = selector.Ball.CurrentPolarity == Polarity.Positive ? "+"
                : selector.Ball.CurrentPolarity == Polarity.Negative ? "−" : "0";
            string sample = Measurement(lesson);
            if (selector.SelectedStage == 4) sample = lesson.FieldStatusText + " · " + sample;
            else if (selector.SelectedStage == 3) sample = (lesson.IsPreparingTrial ? "공 고정 · " : "A/D 착지 · ") + sample;
            return sample + " · 별 " + selector.Ball.Stars + "/1 · 전하 " + charge;
        }

        private string Measurement(ElectricLearningController2D lesson)
        {
            if (selector.SelectedStage == 5) return $"{lesson.CurrentVoltage:0.0}/50 V · 발사 {lesson.LastLaunchVoltage:0.0}V";
            if (!lesson.TryGetDisplaySample(out ElectricForceSample sample)) return "실험 준비";
            if (sample.UsesSIUnits) return $"r {sample.DistanceMeters:0.00}m · F {sample.ForceNewtons.magnitude:0.00}N";
            if (selector.SelectedStage == 4) return $"E {Direction(sample.Field.y)} F {Direction(sample.Force.y)}";
            if (selector.SelectedStage == 3)
            {
                if (lesson.HasResult) return $"q {lesson.ResultSample.SignedCharge:0.#} Q {lesson.ResultSample.SourceCharge:0.#} F {lesson.BaselineSample.ForceMagnitude:0.0}→{lesson.ResultSample.ForceMagnitude:0.0}";
                if (lesson.HasBaseline) return $"q {sample.SignedCharge:0.#} Q {sample.SourceCharge:0.#} F {lesson.BaselineSample.ForceMagnitude:0.0}→{sample.ForceMagnitude:0.0}";
                return $"고정 r {sample.Distance:0.00} q {sample.SignedCharge:0.#} Q {sample.SourceCharge:0.#} F {sample.ForceMagnitude:0.0}";
            }
            return $"r {sample.Distance:0.00} F {sample.ForceMagnitude:0.0}";
        }

        /// <summary>저장된 Rect와 Text만 재사용하며 글자 크기를 화면 픽셀 기준으로 유지한다.</summary>
        private void ApplyReadableLayout(bool hasPlans)
        {
            if (canvas == null) canvas = GetComponent<Canvas>();
            float scale = canvas != null ? Mathf.Max(0.01f, canvas.scaleFactor) : 1f;
            if (layoutWidth == Screen.width && layoutHeight == Screen.height
                && Mathf.Abs(layoutScale - scale) < 0.0001f && layoutPlans == hasPlans) return;
            layoutWidth = Screen.width; layoutHeight = Screen.height; layoutScale = scale; layoutPlans = hasPlans;
            bool compact = Screen.width < 1100;
            float headerHeight = compact ? 116f : 108f;
            float footerHeight = Screen.width < 1300 ? 78f : 68f;
            RectTransform header = stageNameText != null ? stageNameText.transform.parent as RectTransform : null;
            RectTransform footer = controlsText != null ? controlsText.transform.parent as RectTransform : null;
            if (header != null)
            {
                DisableLayout(header);
                StretchBar(header, true, headerHeight / scale, 8f / scale, 4f / scale);
                RectTransform number = stageNumberText.transform.parent as RectTransform;
                Box(number, 8f / scale, 8f / scale, (compact ? 28f : 36f) / scale, (compact ? 24f : 32f) / scale);
                Fill(stageNumberText.rectTransform);
                Pixels(stageNumberText, compact ? 24f : 32f, scale);
                Box(stageNameText.rectTransform, (compact ? 46f : 56f) / scale, 8f / scale,
                    (Screen.width - (compact ? 78f : 88f)) / scale, (compact ? 24f : 32f) / scale);
                Pixels(stageNameText, compact ? 17f : 24f, scale);
                RectTransform quest = questText != null ? questText.transform.parent as RectTransform : null;
                Box(quest, 8f / scale, (compact ? 36f : 46f) / scale,
                    (Screen.width - 32f) / scale, (compact ? 72f : 54f) / scale);
                if (questText != null)
                {
                    Fill(questText.rectTransform);
                    Pixels(questText, compact ? 16f : Screen.width < 1400 ? 18f : 20f, scale);
                }
            }
            if (footer != null)
            {
                DisableLayout(footer);
                StretchBar(footer, false, footerHeight / scale, 8f / scale, 4f / scale);
                Fill(controlsText.rectTransform, 8f / scale, 6f / scale);
                Pixels(controlsText, Screen.width < 1300 ? 14f : 18f, scale);
            }
            if (help == null && objectiveBodyText != null) help = objectiveBodyText.GetComponentInParent<CollapsiblePanel>();
            if (help == null || objectiveBodyText == null) return;
            float width = Mathf.Min(460f, Screen.width - 16f);
            float top = headerHeight + 12f;
            float height = Mathf.Min(460f, Screen.height - top - 8f);
            float tabHeight = 32f, titleHeight = 44f, choicesHeight = hasPlans && planChoices != null ? 80f : 0f;
            help.ConfigureReadingSize(width / scale, (height - tabHeight) / scale, tabHeight / scale);
            RectTransform helpRect = (RectTransform)help.transform;
            helpRect.anchorMin = helpRect.anchorMax = new Vector2(0f, 1f);
            helpRect.pivot = new Vector2(0f, 1f);
            helpRect.anchoredPosition = new Vector2(8f, -top) / scale;
            RectTransform content = objectiveBodyText.transform.parent as RectTransform;
            RectTransform heading = content != null ? content.Find("Head_Text") as RectTransform : null;
            Box(heading, 12f / scale, 4f / scale, (width - 24f) / scale, titleHeight / scale);
            TMP_Text headingText = heading != null ? heading.GetComponentInChildren<TMP_Text>() : null;
            if (headingText != null) { Fill(headingText.rectTransform); Pixels(headingText, 30f, scale); }
            if (planChoices != null)
            {
                planChoices.gameObject.SetActive(hasPlans);
                Box(planChoices, 12f / scale, titleHeight / scale, (width - 24f) / scale, choicesHeight / scale);
                DisableLayout(planChoices);
                Button[] choices = planChoices.GetComponentsInChildren<Button>(true);
                for (int index = 0; index < choices.Length; index++)
                {
                    Box((RectTransform)choices[index].transform, 0f, index * 40f / scale, (width - 24f) / scale, 36f / scale);
                    foreach (TMP_Text label in choices[index].GetComponentsInChildren<TMP_Text>(true)) Pixels(label, compact ? 14f : 18f, scale);
                }
            }
            Box(objectiveBodyText.rectTransform, 12f / scale, (titleHeight + choicesHeight + 4f) / scale,
                (width - 24f) / scale, (height - tabHeight - titleHeight - choicesHeight - 12f) / scale);
            objectiveBodyText.margin = Vector4.zero;
            Pixels(objectiveBodyText, compact ? 14f : 18f, scale);
            if (help.ToggleLabel != null) Pixels(help.ToggleLabel, 16f, scale);
        }

        private static string FirstLine(string value) => string.IsNullOrEmpty(value) ? "" : value.Split('\n')[0];
        private static string Direction(float value) => value > 0.001f ? "위" : value < -0.001f ? "아래" : "0";
        private static void DisableLayout(RectTransform rect)
        {
            LayoutGroup layout = rect.GetComponent<LayoutGroup>();
            if (layout != null) layout.enabled = false;
        }
        private static void Pixels(TMP_Text text, float pixels, float scale)
        {
            if (text == null) return;
            text.enableAutoSizing = false;
            text.fontSize = pixels / scale;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.verticalAlignment = VerticalAlignmentOptions.Top;
        }
        private static void Fill(RectTransform rect, float horizontal = 0f, float vertical = 0f)
        {
            if (rect == null) return;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero; rect.sizeDelta = new Vector2(-2f * horizontal, -2f * vertical);
        }
        private static void Box(RectTransform rect, float x, float y, float width, float height)
        {
            if (rect == null) return;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f); rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height);
        }
        private static void StretchBar(RectTransform rect, bool top, float height, float margin, float edge)
        {
            rect.anchorMin = new Vector2(0f, top ? 1f : 0f); rect.anchorMax = new Vector2(1f, top ? 1f : 0f);
            rect.pivot = new Vector2(0.5f, top ? 1f : 0f); rect.anchoredPosition = new Vector2(0f, top ? -edge : edge);
            rect.sizeDelta = new Vector2(-2f * margin, height);
        }
    }
}
