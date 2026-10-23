using UnityEngine;

namespace sung_wook
{
    /// <summary>실제 공급 전하에 비례해 접촉 전극 위에 Plus/Minus 텍스처를 섞는다.</summary>
    [DisallowMultipleComponent]
    public sealed class ContactChargeVisual2D : MonoBehaviour
    {
        [SerializeField] private ContactCharger2D charger;
        [SerializeField] private SpriteRenderer electrodeRenderer;
        [SerializeField] private Sprite positiveSprite;
        [SerializeField] private Sprite negativeSprite;
        [SerializeField] private SpriteRenderer highlightRenderer;
        [SerializeField] private SpriteRenderer horizontalStroke;
        [SerializeField] private SpriteRenderer verticalStroke;
        [SerializeField] private Color neutralColor = new Color(0.46f, 0.49f, 0.5f, 1f);
        [SerializeField] private Color neutralHighlight = new Color(0.8509804f, 0.8862745f, 0.8627451f, 1f);

        [SerializeField] private SpriteRenderer chargeRenderer;

        private void Reset()
        {
            charger = GetComponent<ContactCharger2D>();
            electrodeRenderer = GetComponent<SpriteRenderer>();
        }

        private void Awake()
        {
            if (charger == null) charger = GetComponent<ContactCharger2D>();
            if (electrodeRenderer == null) electrodeRenderer = GetComponent<SpriteRenderer>();
            RefreshDisplay();
        }

        private void OnEnable() => RefreshDisplay();
        private void LateUpdate() => RefreshDisplay();

        /// <summary>관측만 수행한다. 전하와 이동 및 충돌 상태는 변경하지 않는다.</summary>
        public void RefreshDisplay()
        {
            float charge = charger != null && charger.isActiveAndEnabled ? charger.ChargeFraction : 0f;
            if (float.IsNaN(charge) || float.IsInfinity(charge)) charge = 0f;
            charge = Mathf.Clamp(charge, -1f, 1f);
            float amount = Mathf.Abs(charge);

            if (electrodeRenderer != null)
                electrodeRenderer.color = neutralColor;
            if (highlightRenderer != null)
                highlightRenderer.color = neutralHighlight;

            // 텍스처에 부호가 포함되어 있으므로 기존 흰색 십자 표시는 겹치지 않는다.
            if (horizontalStroke != null) horizontalStroke.enabled = false;
            if (verticalStroke != null) verticalStroke.enabled = false;

            if (chargeRenderer == null) return;

            Sprite sprite = charge > 0f ? positiveSprite : negativeSprite;
            chargeRenderer.sprite = sprite;
            chargeRenderer.color = new Color(1f, 1f, 1f, neutralColor.a * amount);
            chargeRenderer.flipX = electrodeRenderer.flipX;
            chargeRenderer.flipY = electrodeRenderer.flipY;
            chargeRenderer.enabled = isActiveAndEnabled && electrodeRenderer.enabled
                && sprite != null && amount > 0f;
        }

        private void OnDisable()
        {
            if (chargeRenderer != null) chargeRenderer.enabled = false;
        }

    }
}
