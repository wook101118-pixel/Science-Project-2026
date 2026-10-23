using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace sung_wook
{
    [DisallowMultipleComponent]
    public sealed class StageFiveGuidanceView : MonoBehaviour
    {
        private ElectricStageSelector selector;
        private ElectricLearningController2D lesson;
        private ElectricExperimentPlan2D plan;
        private ElectricStageSession2D session;
        private VoltageLauncher2D launcher;
        private PlayerMovement movement;
        private PauseMenuController pause;
        private ElectricGameCompleteView result;
        private CollapsiblePanel help;
        private Camera worldCamera;
        private SpriteRenderer grip;
        private Canvas overlay;
        private GraphicRaycaster raycaster;
        private Slider previousGauge;
        private bool previousGaugeActive, ownsGauge, launched, landed;
        private bool needsRelaunch;
        private int lastRetryCount, towerLandings;
        private float firstTowerSurface;
        private Vector2 firstTowerLanding;
        private RectTransform card, dragHint, worldLayer, launchRegion;
        private RectTransform[] zoneEdges;
        private Image[] gripCorners;
        private Image voltageFill;
        private TMP_Text voltageText, targetText, conditionText, recordText, dragText, regionText, launchLabel, positiveLabel;
        private TMP_FontAsset font;
        private int layoutWidth, layoutHeight;
        private float worldBottom;

        private static readonly Color PanelColor = new Color(.12f, .17f, .29f, .97f);
        private static readonly Color ButtonColor = new Color(.31f, .39f, .57f, 1f);
        private static readonly Color AccentColor = new Color(1f, .82f, .30f, 1f);
        private static readonly Color SecondaryColor = new Color(.80f, .86f, .95f, 1f);

        public bool IsGuidanceVisible => overlay != null && overlay.enabled;
        public bool IsDragHintVisible => IsGuidanceVisible && dragHint != null && dragHint.gameObject.activeSelf;
        public bool IsLaunchZoneVisible => IsGuidanceVisible && launchRegion != null && launchRegion.gameObject.activeSelf;
        public bool CanLaunch => IsGuidanceVisible && launcher != null && launcher.CanLaunch;
        public bool HasLandedAfterLaunch => landed;
        public bool NeedsRelaunch => needsRelaunch;
        public bool HasReachedSecondTower => towerLandings > 1;
        public float CurrentVoltage => launcher != null ? launcher.Voltage : 0f;
        public float TargetVoltage => plan != null ? plan.RecommendedVoltage : RequiredVoltage;
        public float LastLaunchVoltage => launcher != null && launcher.HasLaunchSample ? launcher.LastLaunchVoltage : 0f;
        public Button LaunchButton { get; private set; }
        public Button PositiveButton { get; private set; }
        public Button DetailsButton { get; private set; }
        public string CurrentMeasurement { get; private set; } = "발사 장치를 확인하고 있습니다.";
        private float RequiredVoltage => launcher != null ? launcher.TargetVoltage : 50f;
        private bool IsPreparingLaunch => lesson != null && (!lesson.VoltageChallengeSatisfied || needsRelaunch);

        public string CurrentInstruction
        {
            get
            {
                if (selector == null || lesson == null || selector.SelectedStage != 5) return "";
                if (lesson.IsStageCleared) return "별과 전압 발사 목표를 달성했어요!";
                if (help != null && help.IsOpen) return "원리를 확인한 뒤 도움말을 닫고 계속하세요.";
                if (launcher == null || selector.Ball == null) return "발사 장치를 확인하고 있습니다.";
                if (!IsPreparingLaunch)
                {
                    if (lesson.CollectedStars > 0) return "별을 모았어요. 오른쪽 아래 출구로 내려가세요.";
                    if (HasReachedSecondTower) return "두 탑을 건넜어요. D로 오른쪽 아래 별을 모으세요.";
                    return landed ? "D로 다음 탑을 건넌 뒤 오른쪽 아래 별을 모으세요."
                        : "D로 오른쪽 첫 번째 탑 위에 착지하세요.";
                }
                if (selector.Ball.SignedCharge <= 0f) return "양전하 (+) 버튼을 눌러 발사할 공을 준비하세요.";
                if (CurrentVoltage < TargetVoltage)
                    return selector.Lever != null && selector.Lever.Value > .01f
                        ? $"충전 중 · {TargetVoltage:0} V가 되면 손잡이를 중앙으로 돌리세요."
                        : needsRelaunch ? $"발사장으로 돌아왔어요. {TargetVoltage:0} V 이상 충전해 다시 발사하세요."
                            : $"레버를 왼쪽으로 끌어 {TargetVoltage:0} V 이상 충전하세요.";
                if (!launcher.IsInsideLaunchZone) return "A/D로 공을 표시된 두 전극 사이에 옮기세요.";
                if (!launcher.CanLaunch) return launcher.LaunchBlockReason;
                return "준비 완료 · E 또는 발사 버튼을 누르세요.";
            }
        }

        public string ProgressText => lesson == null ? ""
            : "전압 발사 " + (lesson.VoltageChallengeSatisfied ? "완료" : "대기")
                + " · 별 " + lesson.CollectedStars + "/1";

        public void Configure(ElectricStageSelector owner, ElectricLearningController2D learning)
        {
            if (selector != owner || lesson != learning) Unbind();
            selector = owner; lesson = learning;
            if (selector == null || selector.SelectedStage != 5) { SetVisible(false); return; }
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
            if (selector == null || selector.SelectedStage != 5) return;
            plan = selector.GetComponent<ElectricExperimentPlan2D>();
            session = selector.GetComponent<ElectricStageSession2D>();
            VoltageLauncher2D nextLauncher = selector.GetStageComponent<VoltageLauncher2D>(5);
            if (launcher != nextLauncher)
            {
                if (launcher != null) launcher.LaunchedWithVoltage -= OnLaunched;
                RestoreGauge(); launcher = nextLauncher;
                if (launcher != null) launcher.LaunchedWithVoltage += OnLaunched;
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
                    if (renderer.name == "LeverHead") { grip = renderer; break; }
            if (font == null) font = TMP_Settings.defaultFontAsset;
        }

        private void LateUpdate()
        {
            bool stageFive = selector != null && selector.SelectedStage == 5 && lesson != null && selector.Ball != null;
            if (!stageFive) { SetVisible(false); RestoreGauge(); return; }
            if (overlay == null) { ResolveParticipants(); BuildUi(); }
            OwnGauge();
            bool visible = isActiveAndEnabled && Time.timeScale > 0f && !SceneTransitionController.IsBusy
                && !(pause != null && pause.IsPaused) && !(result != null && result.IsResultOpen)
                && !(help != null && help.IsOpen) && !lesson.IsStageCleared;
            SetVisible(visible);
            if (!visible) return;
            if (launcher != null && !launcher.HasLaunchSample) ResetRouteState();
            if (session != null && session.RetryCount != lastRetryCount)
            {
                lastRetryCount = session.RetryCount;
                if (launcher != null && launcher.HasLaunchSample && IsOnLaunchSide(selector.Ball.Body.position))
                    RequireRelaunch();
            }
            Layout(); RefreshCard(); UpdateWorldGuide();
        }

        private void RefreshCard()
        {
            voltageText.text = $"충전 {CurrentVoltage:0.0} / {TargetVoltage:0} V";
            voltageFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(CurrentVoltage / Mathf.Max(.01f, TargetVoltage)), 1f);
            voltageFill.color = CurrentVoltage >= RequiredVoltage ? AccentColor : SecondaryColor;
            targetText.text = TargetVoltage > RequiredVoltage
                ? $"선택 목표 {TargetVoltage:0} V · 기본 {RequiredVoltage:0} V"
                : $"학습 목표: {RequiredVoltage:0} V 이상으로 발사";
            if (launcher == null) conditionText.text = "발사 장치를 확인하고 있습니다.";
            else if (!IsPreparingLaunch) conditionText.text = landed ? "발사 완료 · 탑을 건너 별로 이동" : "발사 완료 · D로 첫 탑에 착지";
            else if (!launcher.CanLaunch) conditionText.text = launcher.LaunchBlockReason;
            else if (CurrentVoltage < TargetVoltage) conditionText.text = $"발사 가능 · 선택 {TargetVoltage:0} V까지 충전 권장";
            else conditionText.text = "양전하 · 전극 사이 · 발사 준비 완료";
            LaunchButton.interactable = CanLaunch;
            launchLabel.text = CanLaunch ? "E  ·  발사" : "발사 준비 확인 · 위 안내 참고";
            bool positive = selector.Ball.SignedCharge > 0f;
            PositiveButton.interactable = !positive;
            positiveLabel.text = positive ? "양전하 (+) 선택됨" : "양전하 (+) 선택";
            DetailsButton.interactable = help != null && help.isActiveAndEnabled;
            recordText.text = launcher != null && launcher.HasLaunchSample ? $"마지막 발사: {LastLaunchVoltage:0.0} V" : "마지막 발사: 아직 없음";
            CurrentMeasurement = voltageText.text + "\n" + targetText.text + "\n" + conditionText.text + "\n" + recordText.text;
        }

        private void BuildUi()
        {
            var root = new GameObject("Stage 5 Guidance", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.hideFlags = HideFlags.DontSave;
            SceneManager.MoveGameObjectToScene(root, gameObject.scene); root.transform.SetParent(transform, false);
            overlay = root.GetComponent<Canvas>(); overlay.renderMode = RenderMode.ScreenSpaceOverlay; overlay.sortingOrder = 30;
            raycaster = root.GetComponent<GraphicRaycaster>();
            root.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            RectTransform host = root.GetComponent<RectTransform>();
            worldLayer = Empty(host, "Stage5 World Guide Mask"); worldLayer.gameObject.AddComponent<RectMask2D>();
            launchRegion = Empty(worldLayer, "Stage5 Launch Region");
            zoneEdges = new RectTransform[4];
            for (int index = 0; index < zoneEdges.Length; index++)
                zoneEdges[index] = Rectangle(launchRegion, "Launch Boundary " + index, new Color(.55f, .36f, .08f, .65f)).rectTransform;
            regionText = Text(launchRegion, "Stage5 Launch Region Label", 15, new Color(.38f, .23f, .02f)); regionText.text = "발사 영역";
            dragHint = Empty(worldLayer, "Stage5 Drag Hint");
            gripCorners = new Image[8];
            for (int index = 0; index < gripCorners.Length; index++) gripCorners[index] = Rectangle(dragHint, "Grip Corner " + index, AccentColor);
            RectTransform caption = Rectangle(dragHint, "Drag Caption", PanelColor).rectTransform;
            dragText = Text(caption, "Stage5 Drag Instruction", 15, AccentColor);
            Box(caption, -96, -70, 192, 28); Fill(dragText.rectTransform, 4, 3);
            card = Rectangle(host, "Stage5 Voltage Card", PanelColor).rectTransform;
            voltageText = Text(card, "Stage5 Current Voltage", 22, AccentColor);
            RectTransform track = Rectangle(card, "Stage5 Voltage Track", new Color(.27f, .34f, .46f)).rectTransform;
            voltageFill = Rectangle(track, "Stage5 Voltage Fill", SecondaryColor); Fill(voltageFill.rectTransform, 0, 0);
            targetText = Text(card, "Stage5 Target Voltage", 14, SecondaryColor);
            conditionText = Text(card, "Stage5 Launch Conditions", 15, Color.white);
            LaunchButton = Button(card, "Stage5 Launch Button", "E  ·  발사", Launch, out launchLabel);
            PositiveButton = Button(card, "Stage5 Positive Button", "양전하 (+) 선택", SelectPositive, out positiveLabel);
            DetailsButton = Button(card, "Stage5 Details Button", "원리 · 고급 조작", OpenDetails, out _);
            recordText = Text(card, "Stage5 Last Launch Voltage", 16, SecondaryColor);
            SetVisible(false); Layout();
        }

        private void Layout()
        {
            if (layoutWidth == Screen.width && layoutHeight == Screen.height) return;
            layoutWidth = Screen.width; layoutHeight = Screen.height;
            float width = Mathf.Min(320f, Screen.width - 24f), header = Screen.width < 1100 ? 116f : 108f;
            worldBottom = Screen.width < 1300 ? 82f : 72f;
            BottomBox(worldLayer, 0, worldBottom, Screen.width, Mathf.Max(0f, Screen.height - header - 8f - worldBottom));
            BottomBox(launchRegion, 0, 0, 0, 0);
            Box(card, 16, header + 48f, width, 240f);
            Box(voltageText.rectTransform, 12, 10, width - 24, 30);
            Box((RectTransform)voltageFill.transform.parent, 12, 46, width - 24, 8);
            Box(targetText.rectTransform, 12, 62, width - 24, 22);
            Box(conditionText.rectTransform, 12, 88, width - 24, 36);
            Box((RectTransform)LaunchButton.transform, 12, 130, width - 24, 32);
            float half = (width - 32) * .5f;
            Box((RectTransform)PositiveButton.transform, 12, 170, half, 30);
            Box((RectTransform)DetailsButton.transform, 20 + half, 170, half, 30);
            Box(recordText.rectTransform, 12, 210, width - 24, 24);
        }

        private void UpdateWorldGuide()
        {
            BoxCollider2D zone = launcher != null ? launcher.LaunchZone : null;
            bool showZone = worldCamera != null && zone != null && zone.isActiveAndEnabled && IsPreparingLaunch;
            launchRegion.gameObject.SetActive(showZone);
            if (showZone)
            {
                Vector2 half = zone.size * .5f, offset = zone.offset;
                Vector2[] corners = {
                    ScreenPoint(zone.transform.TransformPoint(offset + new Vector2(-half.x, -half.y))),
                    ScreenPoint(zone.transform.TransformPoint(offset + new Vector2(half.x, -half.y))),
                    ScreenPoint(zone.transform.TransformPoint(offset + new Vector2(half.x, half.y))),
                    ScreenPoint(zone.transform.TransformPoint(offset + new Vector2(-half.x, half.y))) };
                for (int index = 0; index < corners.Length; index++) Line(zoneEdges[index], corners[index], corners[(index + 1) % 4], 2f);
                BottomBox(regionText.rectTransform, corners[3].x, corners[3].y + 6, 120, 24);
            }
            bool showHint = worldCamera != null && grip != null && IsPreparingLaunch;
            Vector2 point = showHint ? ScreenPoint(grip.bounds.center) : Vector2.zero;
            showHint &= point.x > 28 && point.x < Screen.width - 28 && point.y > 28 && point.y < worldLayer.rect.height - 80;
            if (CurrentVoltage >= TargetVoltage && (selector.Lever == null || selector.Lever.Value <= .01f)) showHint = false;
            dragHint.gameObject.SetActive(showHint);
            if (!showHint) return;
            BottomBox(dragHint, point.x, point.y, 0, 0);
            dragText.text = CurrentVoltage >= TargetVoltage ? "중앙으로 돌려 충전 멈춤" : "왼쪽으로 끌어 충전";
            float size = 22f + 2f * Mathf.Sin(Time.unscaledTime * 3f);
            for (int index = 0; index < 4; index++)
            {
                float x = index % 2 == 0 ? -size : size - 10;
                float y = index < 2 ? -size : size - 3;
                BottomBox(gripCorners[index * 2].rectTransform, x, y, 10, 3);
                BottomBox(gripCorners[index * 2 + 1].rectTransform, index % 2 == 0 ? -size : size - 3, index < 2 ? -size : size - 10, 3, 10);
            }
        }

        private Vector2 ScreenPoint(Vector3 point)
        {
            Vector3 screen = worldCamera.WorldToScreenPoint(point); return new Vector2(screen.x, screen.y - worldBottom);
        }
        private void Launch()
        {
            if (!CanLaunch) return;
            ElectricSfxPlayer.Play(SfxCue.UiClick); lesson.RequestLaunch();
        }
        private void SelectPositive()
        {
            if (!IsGuidanceVisible || lesson == null) return;
            ElectricSfxPlayer.Play(SfxCue.UiClick); lesson.SetBallCharge(1f);
        }
        private void OpenDetails()
        {
            if (!IsGuidanceVisible || help == null) return;
            ElectricSfxPlayer.Play(SfxCue.UiClick); help.SetOpen(true); SetVisible(false);
        }
        private void OnLaunched(float voltage)
        {
            ResetRouteState(); launched = true;
        }
        private void OnLanded(Vector2 point)
        {
            if (!launched || launcher == null || launcher.LaunchZone == null) return;
            if (IsOnLaunchSide(point)) { RequireRelaunch(); return; }
            Collider2D collider = selector.Ball.GetComponent<Collider2D>();
            float surface = collider != null ? collider.bounds.min.y : point.y;
            if (surface <= launcher.LaunchZone.bounds.max.y) return;
            if (towerLandings == 0)
            {
                towerLandings = 1; firstTowerSurface = surface; firstTowerLanding = point; landed = true;
            }
            else if (surface > firstTowerSurface + .25f && point.x > firstTowerLanding.x)
                towerLandings = 2;
        }
        private bool IsOnLaunchSide(Vector2 point)
        {
            if (launcher == null || launcher.LaunchZone == null || selector == null || selector.Ball == null) return false;
            Collider2D collider = selector.Ball.GetComponent<Collider2D>();
            float radius = collider != null ? collider.bounds.extents.y : 0f;
            Bounds zone = launcher.LaunchZone.bounds;
            return point.x <= zone.max.x + radius && point.y - radius <= zone.max.y;
        }
        private void RequireRelaunch()
        {
            needsRelaunch = true; landed = false; towerLandings = 0;
        }
        private void ResetRouteState()
        {
            launched = landed = needsRelaunch = false; towerLandings = 0;
            firstTowerSurface = 0f; firstTowerLanding = Vector2.zero;
        }
        private void SetVisible(bool visible)
        {
            if (overlay != null) overlay.enabled = visible;
            if (raycaster != null) raycaster.enabled = visible;
        }
        private void OwnGauge()
        {
            if (ownsGauge || launcher == null || launcher.VoltageGauge == null) return;
            previousGauge = launcher.VoltageGauge; previousGaugeActive = previousGauge.gameObject.activeSelf;
            previousGauge.gameObject.SetActive(false); ownsGauge = true;
        }
        private void RestoreGauge()
        {
            if (ownsGauge && previousGauge != null) previousGauge.gameObject.SetActive(previousGaugeActive);
            ownsGauge = false; previousGauge = null;
        }
        private void Unbind()
        {
            if (launcher != null) launcher.LaunchedWithVoltage -= OnLaunched;
            if (movement != null) movement.LandedOnGround -= OnLanded;
            RestoreGauge(); launcher = null; movement = null; grip = null; plan = null; session = null;
            ResetRouteState(); lastRetryCount = 0;
        }
        private void OnDisable() { SetVisible(false); RestoreGauge(); }
        private void OnDestroy() { Unbind(); if (overlay != null) Destroy(overlay.gameObject); }

        private Button Button(Transform parent, string name, string caption, UnityEngine.Events.UnityAction action, out TMP_Text label)
        {
            Image background = Rectangle(parent, name, ButtonColor); background.raycastTarget = true;
            Button button = background.gameObject.AddComponent<Button>(); button.targetGraphic = background;
            button.onClick.AddListener(action);
            label = Text(button.transform, "Label", 16, Color.white); label.text = caption;
            label.alignment = TextAlignmentOptions.Center; Fill(label.rectTransform, 4, 2); return button;
        }
        private TMP_Text Text(Transform parent, string name, float size, Color color)
        {
            TMP_Text label = Empty(parent, name).gameObject.AddComponent<TextMeshProUGUI>();
            label.font = font; label.fontSize = size; label.color = color; label.raycastTarget = false;
            label.enableAutoSizing = false; label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Overflow; label.alignment = TextAlignmentOptions.TopLeft;
            label.margin = Vector4.zero; return label;
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
