using UnityEngine;

namespace sung_wook
{
    /// <summary>플레이어가 보유한 전하량에 따라 회색 몸통 위에 Plus/Minus 텍스처를 섞는다.</summary>
    public class ChangePlayerVisual : ChangeVisual
    {
        [SerializeField] private Sprite positiveSprite;
        [SerializeField] private Sprite negativeSprite;
        [SerializeField, Min(0.001f)] private float fullChargeMagnitude = 1f;
        [SerializeField, Min(0f)] private float transitionDuration = 0.2f;

        private ElectricBall2D ball;
        private SpriteRenderer bodyRenderer;
        [SerializeField] private SpriteRenderer chargeRenderer;
        private TMPro.TMP_Text[] neutralLabels;
        private float displayedCharge;
        private bool initialized;

        private bool UsesChargeTextures => positiveSprite != null && negativeSprite != null;

        public override void Setup()
        {
            // 전하 텍스처를 지정하지 않은 기존 프리팹/도구는 원래 표시 경로를 유지한다.
            if (!UsesChargeTextures)
            {
                base.Setup();
                return;
            }
            if (initialized) return;
            if (root == null) root = gameObject;

            if (existingNeutralVisual == null)
            {
                Transform authoredVisual = root.transform.Find("Visual");
                if (authoredVisual != null) existingNeutralVisual = authoredVisual.gameObject;
                else if (_neutralVisual != null)
                {
                    existingNeutralVisual = Instantiate(_neutralVisual, root.transform);
                    existingNeutralVisual.name = "NeutralVisual";
                }
            }
            if (existingNeutralVisual == null) return;
            bodyRenderer = existingNeutralVisual.GetComponentInChildren<SpriteRenderer>(true);
            if (bodyRenderer == null) return;
            neutralLabels = existingNeutralVisual.GetComponentsInChildren<TMPro.TMP_Text>(true);

            existingNeutralVisual.SetActive(true);
            SetNeutralLabelsVisible(Mathf.Abs(displayedCharge) <= 0f);
            ball = GetComponentInParent<ElectricBall2D>();
            // 표시 오브젝트는 씬에 배치하고, 코드는 기존 스프라이트의 전환만 담당한다.
            if (chargeRenderer != null) chargeRenderer.enabled = false;
            initialized = true;
        }

        public override void VisualChange(PlayerStateType newState)
        {
            if (!UsesChargeTextures)
            {
                base.VisualChange(newState);
                return;
            }
            Setup();
            CurrentVisualState = newState;
        }

        private void LateUpdate() => UpdateChargeVisual(Time.deltaTime);

        private void UpdateChargeVisual(float deltaTime)
        {
            if (!UsesChargeTextures) return;
            Setup();
            if (chargeRenderer == null || bodyRenderer == null) return;
            if (ball == null) ball = GetComponentInParent<ElectricBall2D>();

            float signedCharge = ball != null ? ball.SignedCharge
                : ElectricBall2D.ChargeSign(CurrentVisualState);
            if (float.IsNaN(signedCharge) || float.IsInfinity(signedCharge)) signedCharge = 0f;
            float target = Mathf.Clamp(signedCharge / Mathf.Max(0.001f, fullChargeMagnitude), -1f, 1f);
            // 물리 전하는 즉시 반영하며 화면만 짧게 보간한다. 부호가 바뀌면 중성을 거쳐 전환한다.
            displayedCharge = transitionDuration > 0f
                ? Mathf.MoveTowards(displayedCharge, target, Mathf.Max(0f, deltaTime) / transitionDuration)
                : target;

            existingNeutralVisual.SetActive(true);
            SetNeutralLabelsVisible(Mathf.Abs(displayedCharge) <= 0f);
            chargeRenderer.sprite = displayedCharge > 0f ? positiveSprite : negativeSprite;
            chargeRenderer.color = new Color(1f, 1f, 1f, bodyRenderer.color.a * Mathf.Abs(displayedCharge));
            chargeRenderer.flipX = bodyRenderer.flipX;
            chargeRenderer.flipY = bodyRenderer.flipY;
            chargeRenderer.enabled = isActiveAndEnabled && bodyRenderer.enabled
                && Mathf.Abs(displayedCharge) > 0f;
        }

        private void OnDisable()
        {
            if (chargeRenderer != null) chargeRenderer.enabled = false;
            SetNeutralLabelsVisible(true);
        }

        private void OnDestroy()
        {
            SetNeutralLabelsVisible(true);
        }

        private void SetNeutralLabelsVisible(bool visible)
        {
            if (neutralLabels == null) return;
            foreach (TMPro.TMP_Text label in neutralLabels)
                if (label != null) label.enabled = visible;
        }
    }
}
