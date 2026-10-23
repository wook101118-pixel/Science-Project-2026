using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace sung_wook
{
                                                                                
    [DisallowMultipleComponent]
    public sealed class StageThreeGuidanceView : MonoBehaviour
    {
        private ElectricStageSelector selector;
        private ElectricLearningController2D lesson;
        private ElectricExperimentPlan2D plan;
        private DistanceLauncher2D launcher;
        private PlayerMovement movement;
        private PauseMenuController pause;
        private ElectricGameCompleteView result;
        private CollapsiblePanel help;
        private ElectricVectorDisplay2D previousVectors;
        private bool previousVectorsEnabled, ownsVectorDisplay;
        private Camera worldCamera;
        private Transform grip;
        private SpriteRenderer gripRenderer;
        private Canvas overlay;
        private GraphicRaycaster raycaster;
        private RectTransform card, dragHint, forceArrow;
        private RectTransform forceLine, forceTipA, forceTipB;
        private Image[] gripCorners;
        private TMP_Text chargeText, ratioText, numbersText, flightText, dragText;
        private TMP_Text launchLabel, retryLabel, detailsLabel, arrowLabel;
        private TMP_FontAsset font;
        private bool launched, landed, wasPreparing;
        private int layoutWidth, layoutHeight;

        private static readonly Color PanelColor = new Color(0.12f, 0.17f, 0.29f, .97f);
        private static readonly Color ButtonColor = new Color(.31f, .39f, .57f, 1f);
        private static readonly Color AccentColor = new Color(1f, .82f, .30f, 1f);
        private static readonly Color SecondaryColor = new Color(.80f, .86f, .95f, 1f);

        public bool IsGuidanceVisible => overlay != null && overlay.enabled;
        public bool IsDragHintVisible => IsGuidanceVisible && dragHint != null && dragHint.gameObject.activeSelf;
        public bool IsForceArrowVisible => IsGuidanceVisible && forceArrow != null && forceArrow.gameObject.activeSelf;
        public bool HasLandedAfterLaunch => landed;
        public bool CanLaunch => IsGuidanceVisible && lesson != null && lesson.IsPreparingTrial && lesson.CanLaunchComparison;
        public Button LaunchButton { get; private set; }
        public Button RetryButton { get; private set; }
        public Button DetailsButton { get; private set; }
        public float ForceRatio { get; private set; }
        public float BaselineSourceCharge { get; private set; }
        public float CurrentSourceCharge { get; private set; }
        public string CurrentMeasurement { get; private set; } = "기준을 준비하고 있습니다.";
        private bool IsBallChargePlan => plan != null && plan.SelectedPlan == 1;

        private bool HasPhysicalLaunchBlock => !lesson.CanLaunchComparison
            && !lesson.ComparisonLaunchBlockReason.StartsWith("기준과 같은 조건");

        private bool RecommendsMoreCharge
        {
            get
            {
                return lesson != null && lesson.IsPreparingTrial && lesson.HasBaseline
                    && !lesson.ComparisonChallengeSatisfied
                    && lesson.TryGetDisplaySample(out ElectricForceSample sample)
                    && lesson.BaselineSample.ForceMagnitude > .0001f
                    && sample.ForceMagnitude / lesson.BaselineSample.ForceMagnitude < 1.95f;
            }
        }

        public string CurrentInstruction
        {
            get
            {
                if (lesson == null || selector == null || selector.SelectedStage != 3) return "";
                if (lesson.IsStageCleared) return "비교와 별 수집을 마쳤어요!";
                if (help != null && help.IsOpen) return "원리를 확인한 뒤 도움말을 닫고 계속하세요.";
                if (lesson.IsPreparingTrial)
                {
                    if (!lesson.HasBaseline) return "공을 고정하고 비교 기준을 준비합니다.";
                    if (HasPhysicalLaunchBlock) return lesson.ComparisonLaunchBlockReason;
                    if (RecommendsMoreCharge || !lesson.CanLaunchComparison)
                        return IsBallChargePlan ? "전극은 그대로 두고 X로 공 전하량을 높이세요."
                            : "레버를 왼쪽 끝까지 끌어 전기력을 약 2배로 높이세요.";
                    return "힘이 달라졌어요! E 또는 발사 버튼을 누르세요.";
                }
                if (selector.Ball != null && selector.Ball.Stars > 0)
                    return lesson.ComparisonChallengeSatisfied ? "별을 모았어요. 오른쪽 출구로 이동하세요." : "다시 실험에서 전기력을 비교한 뒤 출구로 가세요.";
                if (landed) return "낙석이 비켜갈 때 다음 발판으로 이동해 별을 모으세요.";
                return "A/D로 방향을 조절해 다음 발판에 착지하세요.";
            }
        }

        public string ProgressText => lesson == null || selector == null || selector.Ball == null ? ""
            : "비교 " + (lesson.ComparisonChallengeSatisfied ? "완료" : "대기")
                + "  ·  별 " + selector.Ball.Stars + "/1";

                                                                              
        public void Configure(ElectricStageSelector owner, ElectricLearningController2D learning)
        {
            if (selector != owner || lesson != learning) Unbind();
            selector = owner;
            lesson = learning;
            if (selector == null || selector.SelectedStage != 3) { SetVisible(false); return; }
            BindParticipants();
        }

        private void Start()
        {
            if (selector == null) selector = GetComponent<ElectricStageSelector>();
            if (lesson == null && selector != null) lesson = selector.GetComponent<ElectricLearningController2D>();
            BindParticipants();
        }

        private void BindParticipants()
        {
            if (selector == null || selector.SelectedStage != 3) return;
            if (plan == null) plan = selector.GetComponent<ElectricExperimentPlan2D>();
            DistanceLauncher2D nextLauncher = selector.GetStageComponent<DistanceLauncher2D>(3);
            if (launcher != nextLauncher)
            {
                if (launcher != null) launcher.Launched -= OnLaunched;
                launcher = nextLauncher;
                if (launcher != null) launcher.Launched += OnLaunched;
            }
            PlayerMovement nextMovement = selector.Ball != null ? selector.Ball.GetComponent<PlayerMovement>() : null;
            if (movement != nextMovement)
            {
                if (movement != null) movement.LandedOnGround -= OnLanded;
                movement = nextMovement;
                if (movement != null) movement.LandedOnGround += OnLanded;
            }
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
            if (grip == null && selector.Lever != null)
                foreach (SpriteRenderer renderer in selector.Lever.GetComponentsInChildren<SpriteRenderer>(true))
                    if (renderer.name == "LeverHead") { grip = renderer.transform; gripRenderer = renderer; break; }
            if (font == null) font = TMP_Settings.defaultFontAsset;
        }

        private void LateUpdate()
        {
            bool stageThree = selector != null && selector.SelectedStage == 3 && lesson != null && selector.Ball != null;
            OwnVectorDisplay(stageThree);
            if (!stageThree) { SetVisible(false); return; }
            if (overlay == null) { BindParticipants(); BuildUi(); }
            bool visible = isActiveAndEnabled && Time.timeScale > 0f && !SceneTransitionController.IsBusy
                && !(pause != null && pause.IsPaused) && !(result != null && result.IsResultOpen)
                && !(help != null && help.IsOpen) && !lesson.IsStageCleared;
            SetVisible(visible);
            if (!visible) return;
            if (lesson.IsPreparingTrial && (!wasPreparing || !lesson.HasResult)) { launched = false; landed = false; }
            wasPreparing = lesson.IsPreparingTrial;
            Layout();
            RefreshMeasurement();
            bool preparing = lesson.IsPreparingTrial;
            LaunchButton.gameObject.SetActive(preparing);
            LaunchButton.interactable = CanLaunch;
            launchLabel.text = CanLaunch ? (RecommendsMoreCharge ? "E  ·  발사 (약 2배 권장)" : "E  ·  발사")
                : HasPhysicalLaunchBlock ? "발사 준비 확인 · 위 안내 참고"
                : IsBallChargePlan ? "X로 공 전하량을 높여 보세요" : "왼쪽 끝까지 끌어 보세요";
            flightText.gameObject.SetActive(!preparing);
            flightText.text = landed ? "착지 완료 · 낙석이 비킬 때 이동" : "발사한 힘을 기록했습니다";
            RetryButton.interactable = true;
            DetailsButton.interactable = help != null && help.isActiveAndEnabled;
            UpdateGripHint(preparing && !IsBallChargePlan && !HasPhysicalLaunchBlock && RecommendsMoreCharge);
            UpdateForceArrow(preparing);
        }

        private void RefreshMeasurement()
        {
            if (!lesson.TryGetDisplaySample(out ElectricForceSample sample))
            {
                chargeText.text = "기준을 준비하고 있습니다."; ratioText.text = ""; numbersText.text = "";
                CurrentMeasurement = chargeText.text; ForceRatio = 0; return;
            }
            if (!lesson.IsPreparingTrial && lesson.HasResult) sample = lesson.ResultSample;
            ElectricForceSample baseline = lesson.HasBaseline ? lesson.BaselineSample : sample;
            BaselineSourceCharge = Mathf.Abs(baseline.SourceCharge);
            CurrentSourceCharge = Mathf.Abs(sample.SourceCharge);
            ForceRatio = baseline.ForceMagnitude > .0001f ? sample.ForceMagnitude / baseline.ForceMagnitude : 0;
            chargeText.text = IsBallChargePlan
                ? $"공 전하량  {Mathf.Abs(baseline.SignedCharge):0.##} → {Mathf.Abs(sample.SignedCharge):0.##}"
                : $"전극 전하량  {BaselineSourceCharge:0.##} → {CurrentSourceCharge:0.##}";
            ratioText.text = $"전기력  {ForceRatio:0.00}배";
            numbersText.text = $"기준 {baseline.ForceMagnitude:0.0} → {sample.ForceMagnitude:0.0} · 게임 단위";
            if (sample.IsLimited || baseline.IsLimited) numbersText.text += " · 제한 적용";
            CurrentMeasurement = chargeText.text + "\n" + ratioText.text + "\n" + numbersText.text;
        }

        private void BuildUi()
        {
            if (overlay != null) return;
            var root = new GameObject("Stage 3 Guidance", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.hideFlags = HideFlags.DontSave;
            SceneManager.MoveGameObjectToScene(root, gameObject.scene);
            root.transform.SetParent(transform, false);
            overlay = root.GetComponent<Canvas>(); overlay.renderMode = RenderMode.ScreenSpaceOverlay; overlay.sortingOrder = 30;
            raycaster = root.GetComponent<GraphicRaycaster>();
            root.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            RectTransform host = root.GetComponent<RectTransform>();
            card = Rectangle(host, "Stage3 Comparison Card", PanelColor).rectTransform;
            chargeText = Text(card, "Stage3 Electrode Charge", 18, Color.white);
            ratioText = Text(card, "Stage3 Force Ratio", 23, AccentColor);
            numbersText = Text(card, "Stage3 Force Values", 14, SecondaryColor);
            LaunchButton = MakeButton(card, "Stage3 Launch Button", "E  ·  발사", Launch, out launchLabel);
            RetryButton = MakeButton(card, "Stage3 Retry Button", "다시 실험", Retry, out retryLabel);
            DetailsButton = MakeButton(card, "Stage3 Details Button", "원리 · 고급 조작", OpenDetails, out detailsLabel);
            flightText = Text(card, "Stage3 Landing Tip", 16, SecondaryColor);

            dragHint = Empty(host, "Stage3 Drag Hint");
            gripCorners = new Image[8];
            for (int i = 0; i < gripCorners.Length; i++) gripCorners[i] = Rectangle(dragHint, "Grip Corner " + i, AccentColor);
            RectTransform caption = Rectangle(dragHint, "Drag Caption", PanelColor).rectTransform;
            dragText = Text(caption, "Drag Instruction", 16, AccentColor); dragText.text = "왼쪽 끝까지 드래그";
            Box(caption, -80, -70, 160, 28); Fill(dragText.rectTransform, 4, 3);

            forceArrow = Empty(host, "Stage3 Force Arrow");
            forceLine = Rectangle(forceArrow, "Force Shaft", AccentColor).rectTransform;
            forceTipA = Rectangle(forceArrow, "Force Head A", AccentColor).rectTransform;
            forceTipB = Rectangle(forceArrow, "Force Head B", AccentColor).rectTransform;
            arrowLabel = Text(forceArrow, "Force Preview Label", 15, new Color(.16f, .20f, .29f));
            arrowLabel.text = "발사 힘";
            SetVisible(false);
            Layout();
        }

        private void Layout()
        {
            if (layoutWidth == Screen.width && layoutHeight == Screen.height) return;
            layoutWidth = Screen.width; layoutHeight = Screen.height;
            float width = Mathf.Min(320, Screen.width - 24);
            float top = Screen.width < 1100 ? 164 : 156;
            Box(card, 16, top, width, 192);
            Box(chargeText.rectTransform, 12, 10, width - 24, 28);
            Box(ratioText.rectTransform, 12, 42, width - 24, 32);
            Box(numbersText.rectTransform, 12, 78, width - 24, 34);
            Box((RectTransform)LaunchButton.transform, 12, 112, width - 24, 32);
            Box(flightText.rectTransform, 12, 114, width - 24, 30);
            float half = (width - 32) * .5f;
            Box((RectTransform)RetryButton.transform, 12, 152, half, 30);
            Box((RectTransform)DetailsButton.transform, 20 + half, 152, half, 30);
        }

        private void UpdateGripHint(bool show)
        {
            if (grip == null || worldCamera == null) show = false;
            Vector3 point = show ? worldCamera.WorldToScreenPoint(gripRenderer != null ? gripRenderer.bounds.center : grip.position) : Vector3.zero;
            show &= point.z > 0 && point.x >= 28 && point.x <= Screen.width - 28 && point.y > 100 && point.y < Screen.height - 140;
            dragHint.gameObject.SetActive(show);
            if (!show) return;
            BottomPosition(dragHint, new Vector2(point.x, point.y));
            float half = 22 + 2 * Mathf.Sin(Time.unscaledTime * 3);
            float pulse = .75f + .25f * Mathf.Sin(Time.unscaledTime * 3);
            for (int i = 0; i < 4; i++)
            {
                float x = i % 2 == 0 ? -half : half - 10;
                float y = i < 2 ? -half : half - 3;
                BottomBox(gripCorners[i * 2].rectTransform, x, y, 10, 3);
                BottomBox(gripCorners[i * 2 + 1].rectTransform, i % 2 == 0 ? -half : half - 3, i < 2 ? -half : half - 10, 3, 10);
                Color color = AccentColor; color.a = pulse;
                gripCorners[i * 2].color = gripCorners[i * 2 + 1].color = color;
            }
        }

        private void UpdateForceArrow(bool show)
        {
            ElectricForceSample sample = default;
            show = show && worldCamera != null && lesson.TryGetDisplaySample(out sample) && sample.IsInRange && sample.Force.sqrMagnitude > .000001f;
            forceArrow.gameObject.SetActive(show);
            if (!show) return;
            Vector3 center3 = worldCamera.WorldToScreenPoint(sample.Position);
            Vector3 ahead = worldCamera.WorldToScreenPoint(sample.Position + sample.Force.normalized);
            Vector2 center = center3, direction = ((Vector2)ahead - center).normalized;
            if (center3.z <= 0 || direction.sqrMagnitude < .1f) { forceArrow.gameObject.SetActive(false); return; }
                                                                                                              
            float radius = Mathf.Abs(worldCamera.WorldToScreenPoint(sample.Position + Vector2.right * .6f).x - center.x);
            Vector2 start = center + direction * Mathf.Max(18, radius), end = start + direction * 64;
            if (end.y <= 85 || end.y >= Screen.height - 120 || end.x < 12 || end.x > Screen.width - 12) { forceArrow.gameObject.SetActive(false); return; }
            BottomPosition(forceArrow, Vector2.zero);
            Line(forceLine, start, end, 4);
            Vector2 side = new Vector2(-direction.y, direction.x);
            Line(forceTipA, end - direction * 13 + side * 8, end, 4);
            Line(forceTipB, end - direction * 13 - side * 8, end, 4);
            BottomBox(arrowLabel.rectTransform, end.x + 7, end.y + 4, 96, 24);
        }

        private void Launch()
        {
            if (!CanLaunch) return;
            ElectricSfxPlayer.Play(SfxCue.UiClick);
            lesson.RequestLaunch();
        }
        private void Retry()
        {
            if (!IsGuidanceVisible || lesson == null) return;
            ElectricSfxPlayer.Play(SfxCue.UiClick);
            lesson.PrepareTrial(); launched = landed = false;
        }
        private void OpenDetails()
        {
            if (!IsGuidanceVisible || help == null) return;
            ElectricSfxPlayer.Play(SfxCue.UiClick);
            help.SetOpen(true);
            SetVisible(false);
        }
        private void OnLaunched(ElectricForceSample sample) { launched = true; landed = false; }
        private void OnLanded(Vector2 point) { if (launched && lesson != null && !lesson.IsPreparingTrial) landed = true; }
        private void SetVisible(bool visible)
        {
            if (overlay != null) overlay.enabled = visible;
            if (raycaster != null) raycaster.enabled = visible;
        }

        private void OwnVectorDisplay(bool stageThree)
        {
            if (stageThree && !ownsVectorDisplay)
            {
                previousVectors = selector.GetComponent<ElectricVectorDisplay2D>();
                if (previousVectors != null) { previousVectorsEnabled = previousVectors.enabled; previousVectors.enabled = false; ownsVectorDisplay = true; }
            }
            else if (!stageThree && ownsVectorDisplay) RestoreVectors();
        }
        private void RestoreVectors()
        {
            if (ownsVectorDisplay && previousVectors != null) previousVectors.enabled = previousVectorsEnabled;
            ownsVectorDisplay = false;
        }
        private void Unbind()
        {
            if (launcher != null) launcher.Launched -= OnLaunched;
            if (movement != null) movement.LandedOnGround -= OnLanded;
            launcher = null; movement = null; grip = null; gripRenderer = null; plan = null;
            launched = landed = wasPreparing = false;
            RestoreVectors();
        }
        private void OnDisable() { SetVisible(false); RestoreVectors(); }
        private void OnDestroy() { Unbind(); if (overlay != null) Destroy(overlay.gameObject); }

        private static RectTransform Empty(Transform parent, string name)
        {
            var item = new GameObject(name, typeof(RectTransform)); item.hideFlags = HideFlags.DontSave;
            item.transform.SetParent(parent, false); return item.GetComponent<RectTransform>();
        }
        private static Image Rectangle(Transform parent, string name, Color color)
        {
            RectTransform rect = Empty(parent, name);
            Image image = rect.gameObject.AddComponent<Image>(); image.color = color; image.raycastTarget = false; return image;
        }
        private TMP_Text Text(Transform parent, string name, float size, Color color)
        {
            RectTransform rect = Empty(parent, name);
            TMP_Text label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.font = font; label.fontSize = size; label.color = color; label.raycastTarget = false;
            label.enableAutoSizing = false; label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Overflow; label.alignment = TextAlignmentOptions.TopLeft;
            label.margin = Vector4.zero; return label;
        }
        private Button MakeButton(Transform parent, string name, string caption, UnityEngine.Events.UnityAction action, out TMP_Text label)
        {
            Image background = Rectangle(parent, name, ButtonColor); background.raycastTarget = true;
            Button button = background.gameObject.AddComponent<Button>(); button.targetGraphic = background;
            ColorBlock colors = button.colors; colors.normalColor = Color.white; colors.highlightedColor = new Color(1.1f, 1.1f, 1.1f);
            colors.pressedColor = new Color(.75f, .82f, 1f); colors.disabledColor = new Color(.58f, .62f, .70f, .65f); button.colors = colors;
            button.onClick.AddListener(action);
            label = Text(button.transform, "Label", 16, Color.white); label.text = caption;
            label.alignment = TextAlignmentOptions.Center; Fill(label.rectTransform, 5, 2);
            return button;
        }
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
        private static void BottomPosition(RectTransform rect, Vector2 point) { BottomBox(rect, point.x, point.y, 0, 0); }
        private static void Line(RectTransform line, Vector2 start, Vector2 end, float width)
        {
            Vector2 delta = end - start;
            BottomBox(line, start.x, start.y, delta.magnitude, width); line.pivot = new Vector2(0, .5f);
            line.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        }
    }
}
