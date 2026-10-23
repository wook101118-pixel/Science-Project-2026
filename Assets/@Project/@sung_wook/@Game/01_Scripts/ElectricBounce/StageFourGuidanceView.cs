using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace sung_wook
{
                                                                              
    [DisallowMultipleComponent]
    public sealed class StageFourGuidanceView : MonoBehaviour
    {
        private ElectricStageSelector selector;
        private ElectricLearningController2D lesson;
        private UniformField2D field;
        private BoxCollider2D fieldZone;
        private PauseMenuController pause;
        private ElectricGameCompleteView result;
        private CollapsiblePanel help;
        private Camera worldCamera;
        private ElectricVectorDisplay2D previousVectors;
        private bool previousVectorsEnabled, ownsVectorDisplay;
        private Canvas overlay;
        private GraphicRaycaster raycaster;
        private RectTransform card, worldLayer, fieldGuide, forceArrow;
        private RectTransform[] fieldEdges, fieldArrowParts, forceArrowParts;
        private Image progressFill;
        private TMP_Text selectedText, recommendationText, progressText, directionsText, motionText;
        private TMP_Text fieldLabel, forceLabel;
        private TMP_FontAsset font;
        private readonly Outline[] selectedOutlines = new Outline[3];
        private readonly Image[] recommendedMarks = new Image[3];
        private int layoutWidth, layoutHeight;
        private float worldBottom;

        private static readonly Color PanelColor = new Color(.12f, .17f, .29f, .97f);
        private static readonly Color ButtonColor = new Color(.31f, .39f, .57f, 1f);
        private static readonly Color AccentColor = new Color(1f, .82f, .30f, 1f);
        private static readonly Color SecondaryColor = new Color(.80f, .86f, .95f, 1f);
        private static readonly Color FieldColor = new Color(.47f, .23f, .70f, .9f);

        public bool IsGuidanceVisible => overlay != null && overlay.enabled;
        public bool IsFieldGuideVisible => IsGuidanceVisible && fieldGuide != null && fieldGuide.gameObject.activeSelf;
        public bool IsForceArrowVisible => IsGuidanceVisible && forceArrow != null && forceArrow.gameObject.activeSelf;
        public Button PositiveButton { get; private set; }
        public Button NegativeButton { get; private set; }
        public Button NeutralButton { get; private set; }
        public Button DetailsButton { get; private set; }
        public float SelectedCharge => selector != null && selector.Ball != null ? selector.Ball.SignedCharge : 0f;
        public float RecommendedCharge => lesson == null || !lesson.HasFieldAscent || lesson.CollectedStars == 0
            || lesson.HasRestoredNegativeField || lesson.IsFieldBrakeRecommended || lesson.IsFieldBrakeActive ? -1f : 0f;
        public float CurrentStepProgress { get; private set; }
        public string CurrentMeasurement { get; private set; } = "전기장을 확인하고 있습니다.";
        public string CurrentInstruction => lesson == null || selector == null || selector.SelectedStage != 4 ? ""
            : help != null && help.IsOpen ? "원리를 확인한 뒤 도움말을 닫고 계속하세요."
            : lesson.FieldInstructionText;
        public string ProgressText => lesson == null ? ""
            : "상승 " + State(lesson.HasFieldAscent) + " · 별 " + lesson.CollectedStars + "/1"
                + " · 하강 " + State(lesson.HasNeutralFieldDescent) + " · 제동 " + State(lesson.HasRestoredNegativeField);

        public void Configure(ElectricStageSelector owner, ElectricLearningController2D learning)
        {
            if (selector != owner) { RestoreVectors(); field = null; fieldZone = null; }
            selector = owner; lesson = learning;
            if (selector == null || selector.SelectedStage != 4) { SetVisible(false); return; }
            ResolveParticipants();
        }

        private void Start()
        {
            if (selector == null) selector = GetComponent<ElectricStageSelector>();
            if (lesson == null && selector != null) lesson = selector.GetComponent<ElectricLearningController2D>();
            ResolveParticipants();
        }

        private void ResolveParticipants()
        {
            if (selector == null || selector.SelectedStage != 4) return;
            field = selector.GetStageComponent<UniformField2D>(4);
            fieldZone = field != null ? field.GetComponent<BoxCollider2D>() : null;
            foreach (GameObject root in gameObject.scene.GetRootGameObjects())
            {
                if (pause == null) pause = root.GetComponentInChildren<PauseMenuController>(true);
                if (result == null) result = root.GetComponentInChildren<ElectricGameCompleteView>(true);
                if (help == null) help = root.GetComponentInChildren<CollapsiblePanel>(true);
                if (worldCamera == null)
                    foreach (Camera candidate in root.GetComponentsInChildren<Camera>(true))
                        if (candidate.CompareTag("MainCamera")) { worldCamera = candidate; break; }
                if (font == null)
                {
                    ElectricStageHudView hud = root.GetComponentInChildren<ElectricStageHudView>(true);
                    if (hud != null)
                        foreach (TMP_Text label in hud.GetComponentsInChildren<TMP_Text>(true))
                            if (label.font != null) { font = label.font; break; }
                }
            }
            if (font == null) font = TMP_Settings.defaultFontAsset;
        }

        private void LateUpdate()
        {
            bool stageFour = selector != null && selector.SelectedStage == 4 && lesson != null && selector.Ball != null;
            OwnVectorDisplay(stageFour);
            if (!stageFour) { SetVisible(false); return; }
            if (overlay == null) { ResolveParticipants(); BuildUi(); }
            bool visible = isActiveAndEnabled && Time.timeScale > 0f && !SceneTransitionController.IsBusy
                && !(pause != null && pause.IsPaused) && !(result != null && result.IsResultOpen)
                && !(help != null && help.IsOpen) && !lesson.IsStageCleared;
            SetVisible(visible);
            if (!visible) return;
            Layout();
            RefreshCard();
            UpdateWorldGuide();
        }

        private void RefreshCard()
        {
            selectedText.text = "현재: " + ChargeName(SelectedCharge);
            bool recommendCharge = lesson.IsFieldDirectedDownward;
            recommendationText.text = recommendCharge ? "다음 선택: " + (RecommendedCharge < 0f ? "X · 음전하" : "C · 중성")
                : "먼저 레버로 전기장을 아래로";
            float[] charges = { 1f, -1f, 0f };
            float selectedSign = SelectedCharge > .0001f ? 1f : SelectedCharge < -.0001f ? -1f : 0f;
            for (int index = 0; index < charges.Length; index++)
            {
                selectedOutlines[index].enabled = selectedSign == charges[index];
                recommendedMarks[index].enabled = recommendCharge && RecommendedCharge == charges[index];
            }
            string step;
            if (!lesson.HasFieldAscent) { step = "음전하로 연속 상승"; CurrentStepProgress = lesson.FieldAscentProgress; }
            else if (lesson.CollectedStars == 0) { step = "상부의 별을 모으세요"; CurrentStepProgress = 0f; }
            else if (!lesson.HasNeutralFieldDescent) { step = "중성으로 연속 하강"; CurrentStepProgress = lesson.NeutralFieldDescentProgress; }
            else if (!lesson.HasRestoredNegativeField) { step = "하강 중 음전하 제동"; CurrentStepProgress = lesson.FieldBrakeProgress / .1f; }
            else { step = "실험 완료 · 출구로"; CurrentStepProgress = 1f; }
            CurrentStepProgress = Mathf.Clamp01(CurrentStepProgress);
            progressText.text = step + (lesson.HasFieldAscent && lesson.CollectedStars == 0 ? "" : $"  {CurrentStepProgress * 100f:0}%");
            progressFill.rectTransform.anchorMax = new Vector2(CurrentStepProgress, 1f);
            progressFill.color = lesson.IsFieldBrakeRecommended && !lesson.HasRestoredNegativeField ? AccentColor : new Color(.52f, .78f, .88f);
            Vector2 electricForce = Vector2.zero;
            bool inRange = lesson.TryGetDisplaySample(out ElectricForceSample sample) && sample.IsInRange;
            if (inRange) electricForce = sample.Force;
            Vector2 electricField = field != null ? field.FieldDirection : Vector2.zero;
            directionsText.text = "전기장 E " + Direction(electricField.y) + "   전기력 F " + Direction(electricForce.y);
            float speed = lesson.FieldVerticalVelocity;
            string motion = speed > .25f ? "위로 움직이는 중" : speed < -.25f ? "아래로 움직이는 중" : "잠시 멈추거나 방향 전환 중";
            string note = !inRange ? "장 밖에서는 전기력이 작용하지 않아요"
                : Mathf.Abs(SelectedCharge) <= .0001f ? "중성도 관성과 중력으로 움직여요"
                : "힘과 이동 방향은 다를 수 있어요";
            motionText.text = "공: " + motion + "\n" + note;
            CurrentMeasurement = selectedText.text + "\n" + progressText.text + "\n" + directionsText.text + "\n" + motionText.text;
            DetailsButton.interactable = help != null && help.isActiveAndEnabled;
        }

        private void BuildUi()
        {
            var root = new GameObject("Stage 4 Guidance", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.hideFlags = HideFlags.DontSave;
            SceneManager.MoveGameObjectToScene(root, gameObject.scene);
            root.transform.SetParent(transform, false);
            overlay = root.GetComponent<Canvas>(); overlay.renderMode = RenderMode.ScreenSpaceOverlay; overlay.sortingOrder = 30;
            raycaster = root.GetComponent<GraphicRaycaster>();
            root.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            RectTransform host = root.GetComponent<RectTransform>();
            worldLayer = Empty(host, "Stage4 World Guide Mask"); worldLayer.gameObject.AddComponent<RectMask2D>();
            fieldGuide = Empty(worldLayer, "Stage4 Electric Field Region");
            fieldEdges = new RectTransform[4];
            for (int index = 0; index < fieldEdges.Length; index++)
                fieldEdges[index] = Rectangle(fieldGuide, "Field Boundary " + index, new Color(FieldColor.r, FieldColor.g, FieldColor.b, .36f)).rectTransform;
            fieldArrowParts = ArrowParts(fieldGuide, "Electric Field", FieldColor);
            fieldLabel = Text(fieldGuide, "Electric Field Label", 14, FieldColor); fieldLabel.text = "전기장 E";
            forceArrow = Empty(worldLayer, "Stage4 Electric Force Arrow");
            forceArrowParts = ArrowParts(forceArrow, "Electric Force", new Color(.92f, .53f, .04f, 1f));
            forceLabel = Text(forceArrow, "Electric Force Label", 14, new Color(.50f, .27f, .02f, 1f)); forceLabel.text = "전기력 F";
            card = Rectangle(host, "Stage4 Charge Card", PanelColor).rectTransform;
            selectedText = Text(card, "Stage4 Selected Charge", 18, Color.white);
            DetailsButton = Button(card, "Stage4 Details Button", "원리 보기", OpenDetails);
            PositiveButton = ChargeButton(card, "Stage4 Positive Button", "Z  +", 1f, 0);
            NegativeButton = ChargeButton(card, "Stage4 Negative Button", "X  −", -1f, 1);
            NeutralButton = ChargeButton(card, "Stage4 Neutral Button", "C  0", 0f, 2);
            recommendationText = Text(card, "Stage4 Recommendation", 15, AccentColor);
            progressText = Text(card, "Stage4 Current Progress", 16, Color.white);
            RectTransform track = Rectangle(card, "Stage4 Progress Track", new Color(.27f, .34f, .46f)).rectTransform;
            progressFill = Rectangle(track, "Stage4 Progress Fill", SecondaryColor);
            Fill(progressFill.rectTransform, 0, 0);
            directionsText = Text(card, "Stage4 Field And Force", 16, SecondaryColor);
            motionText = Text(card, "Stage4 Actual Motion", 14, SecondaryColor);
            SetVisible(false); Layout();
        }

        private void Layout()
        {
            if (layoutWidth == Screen.width && layoutHeight == Screen.height) return;
            layoutWidth = Screen.width; layoutHeight = Screen.height;
            float width = Mathf.Min(320f, Screen.width - 24f);
            float header = Screen.width < 1100 ? 116f : 108f;
            worldBottom = Screen.width < 1300 ? 82f : 72f;
            BottomBox(worldLayer, 0, worldBottom, Screen.width, Mathf.Max(0f, Screen.height - header - 8f - worldBottom));
            BottomBox(fieldGuide, 0, 0, 0, 0); BottomBox(forceArrow, 0, 0, 0, 0);
            Box(card, 16, header + 48f, width, 232f);
            Box(selectedText.rectTransform, 12, 10, width - 124f, 26);
            Box((RectTransform)DetailsButton.transform, width - 100f, 10, 88, 25);
            float buttonWidth = (width - 40f) / 3f;
            Button[] choices = { PositiveButton, NegativeButton, NeutralButton };
            for (int index = 0; index < choices.Length; index++)
                Box((RectTransform)choices[index].transform, 12 + index * (buttonWidth + 8), 44, buttonWidth, 34);
            Box(recommendationText.rectTransform, 12, 86, width - 24, 22);
            Box(progressText.rectTransform, 12, 112, width - 24, 24);
            Box((RectTransform)progressFill.transform.parent, 12, 140, width - 24, 6);
            Box(directionsText.rectTransform, 12, 155, width - 24, 22);
            Box(motionText.rectTransform, 12, 183, width - 24, 40);
        }

        private void UpdateWorldGuide()
        {
            bool showField = worldCamera != null && field != null && field.isActiveAndEnabled && fieldZone != null && fieldZone.enabled;
            fieldGuide.gameObject.SetActive(showField);
            if (showField)
            {
                Vector2 half = fieldZone.size * .5f;
                Vector2 offset = fieldZone.offset;
                Vector2[] corners = {
                    ScreenPoint(fieldZone.transform.TransformPoint(offset + new Vector2(-half.x, -half.y))),
                    ScreenPoint(fieldZone.transform.TransformPoint(offset + new Vector2(half.x, -half.y))),
                    ScreenPoint(fieldZone.transform.TransformPoint(offset + new Vector2(half.x, half.y))),
                    ScreenPoint(fieldZone.transform.TransformPoint(offset + new Vector2(-half.x, half.y))) };
                for (int index = 0; index < 4; index++) Line(fieldEdges[index], corners[index], corners[(index + 1) % 4], 1.5f);
                Vector2 origin = (corners[0] + corners[3]) * .5f + Vector2.right * 24f;
                Vector3 worldOrigin = fieldZone.bounds.center;
                Vector2 direction = (ScreenPoint(worldOrigin + (Vector3)field.FieldDirection) - ScreenPoint(worldOrigin)).normalized;
                DrawArrow(fieldArrowParts, origin - direction * 24f, origin + direction * 24f, 3f);
                BottomBox(fieldLabel.rectTransform, origin.x + 10f, origin.y - 12f, 100f, 24f);
            }
            bool showForce = worldCamera != null && lesson.TryGetDisplaySample(out ElectricForceSample sample)
                && sample.IsInRange && sample.Force.sqrMagnitude > .000001f;
            forceArrow.gameObject.SetActive(showForce);
            if (!showForce) return;
            lesson.TryGetDisplaySample(out sample);
            Vector2 center = ScreenPoint(sample.Position);
            Vector2 directionForce = (ScreenPoint(sample.Position + sample.Force.normalized) - center).normalized;
            float radius = Mathf.Abs(ScreenPoint(sample.Position + Vector2.right * .6f).x - center.x);
            Vector2 start = center + Vector2.right * (Mathf.Max(18f, radius) + 8f);
            Vector2 end = start + directionForce * 54f;
            DrawArrow(forceArrowParts, start, end, 4f);
            BottomBox(forceLabel.rectTransform, end.x + 7f, end.y - 10f, 100f, 24f);
        }

        private Vector2 ScreenPoint(Vector3 point)
        {
            Vector3 screen = worldCamera.WorldToScreenPoint(point);
            return new Vector2(screen.x, screen.y - worldBottom);
        }

        private void SelectCharge(float charge)
        {
            if (!IsGuidanceVisible || lesson == null) return;
            ElectricSfxPlayer.Play(SfxCue.UiClick); lesson.SetBallCharge(charge);
        }
        private void OpenDetails()
        {
            if (!IsGuidanceVisible || help == null) return;
            ElectricSfxPlayer.Play(SfxCue.UiClick); help.SetOpen(true); SetVisible(false);
        }
        private void SetVisible(bool visible)
        {
            if (overlay != null) overlay.enabled = visible;
            if (raycaster != null) raycaster.enabled = visible;
        }
        private void OwnVectorDisplay(bool stageFour)
        {
            if (stageFour && !ownsVectorDisplay)
            {
                previousVectors = selector.GetComponent<ElectricVectorDisplay2D>();
                if (previousVectors != null) { previousVectorsEnabled = previousVectors.enabled; previousVectors.enabled = false; ownsVectorDisplay = true; }
            }
            else if (!stageFour && ownsVectorDisplay) RestoreVectors();
        }
        private void RestoreVectors()
        {
            if (ownsVectorDisplay && previousVectors != null) previousVectors.enabled = previousVectorsEnabled;
            ownsVectorDisplay = false;
        }
        private void OnDisable() { SetVisible(false); RestoreVectors(); }
        private void OnDestroy() { RestoreVectors(); if (overlay != null) Destroy(overlay.gameObject); }

        private Button ChargeButton(Transform parent, string name, string caption, float charge, int index)
        {
            Button button = Button(parent, name, caption, () => SelectCharge(charge));
            selectedOutlines[index] = button.gameObject.AddComponent<Outline>();
            selectedOutlines[index].effectColor = Color.white; selectedOutlines[index].effectDistance = new Vector2(2, -2);
            recommendedMarks[index] = Rectangle(button.transform, "Recommended Charge Mark", AccentColor);
            RectTransform mark = recommendedMarks[index].rectTransform;
            mark.anchorMin = Vector2.zero; mark.anchorMax = new Vector2(1f, 0f); mark.pivot = new Vector2(.5f, 0f);
            mark.sizeDelta = new Vector2(0, 3); mark.anchoredPosition = Vector2.zero;
            return button;
        }
        private Button Button(Transform parent, string name, string caption, UnityEngine.Events.UnityAction action)
        {
            Image background = Rectangle(parent, name, ButtonColor); background.raycastTarget = true;
            Button button = background.gameObject.AddComponent<Button>(); button.targetGraphic = background;
            button.onClick.AddListener(action);
            TMP_Text label = Text(button.transform, "Label", 16, Color.white); label.text = caption;
            label.alignment = TextAlignmentOptions.Center; Fill(label.rectTransform, 4, 2);
            return button;
        }
        private TMP_Text Text(Transform parent, string name, float size, Color color)
        {
            TMP_Text label = Empty(parent, name).gameObject.AddComponent<TextMeshProUGUI>();
            label.font = font; label.fontSize = size; label.color = color; label.raycastTarget = false;
            label.enableAutoSizing = false; label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Overflow; label.alignment = TextAlignmentOptions.TopLeft;
            label.margin = Vector4.zero; return label;
        }
        private static RectTransform[] ArrowParts(Transform parent, string name, Color color)
        {
            return new[] { Rectangle(parent, name + " Shaft", color).rectTransform,
                Rectangle(parent, name + " Tip A", color).rectTransform, Rectangle(parent, name + " Tip B", color).rectTransform };
        }
        private static void DrawArrow(RectTransform[] parts, Vector2 start, Vector2 end, float width)
        {
            Vector2 direction = (end - start).normalized, side = new Vector2(-direction.y, direction.x);
            Line(parts[0], start, end, width);
            Line(parts[1], end - direction * 12f + side * 7f, end, width);
            Line(parts[2], end - direction * 12f - side * 7f, end, width);
        }
        private static RectTransform Empty(Transform parent, string name)
        {
            var item = new GameObject(name, typeof(RectTransform)); item.hideFlags = HideFlags.DontSave;
            item.transform.SetParent(parent, false); return item.GetComponent<RectTransform>();
        }
        private static Image Rectangle(Transform parent, string name, Color color)
        {
            Image image = Empty(parent, name).gameObject.AddComponent<Image>(); image.color = color; image.raycastTarget = false; return image;
        }
        private static string State(bool complete) => complete ? "완료" : "대기";
        private static string ChargeName(float value) => value > .0001f ? "양전하 (+)" : value < -.0001f ? "음전하 (−)" : "중성 (0)";
        private static string Direction(float value) => value > .001f ? "위" : value < -.001f ? "아래" : "0";
        private static void Fill(RectTransform rect, float x, float y)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = Vector2.zero; rect.sizeDelta = new Vector2(-2 * x, -2 * y);
        }
        private static void Box(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height);
        }
        private static void BottomBox(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = Vector2.zero; rect.pivot = Vector2.zero;
            rect.anchoredPosition = new Vector2(x, y); rect.sizeDelta = new Vector2(width, height);
        }
        private static void Line(RectTransform line, Vector2 start, Vector2 end, float width)
        {
            Vector2 delta = end - start;
            BottomBox(line, start.x, start.y, delta.magnitude, width); line.pivot = new Vector2(0, .5f);
            line.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        }
    }
}
