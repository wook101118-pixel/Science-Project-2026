using UnityEngine;

namespace sung_wook
{
    /// <summary>레버를 당긴 방향과 양에 따라 갈색 몸통 위에 전하 스프라이트를 서서히 표시한다.</summary>
    [DisallowMultipleComponent]
    public class ChangeGearLeverVisual : MonoBehaviour
    {
        [SerializeField] private global::LeverMover lever;
        [SerializeField] private SpriteRenderer bodyRenderer;
        [SerializeField] private Sprite positiveSprite;
        [SerializeField] private Sprite negativeSprite;

        [SerializeField] private SpriteRenderer chargeRenderer;
        private StageLever2D stageLever;

        private void Awake()
        {
            if (lever == null) lever = GetComponentInChildren<global::LeverMover>(true);
            stageLever = GetComponentInChildren<StageLever2D>(true);
            if (bodyRenderer == null) bodyRenderer = GetComponent<SpriteRenderer>();
            if (bodyRenderer == null || lever == null) return;

            if (chargeRenderer != null) chargeRenderer.enabled = false;
        }

        private void LateUpdate()
        {
            if (chargeRenderer == null || lever == null) return;

            float charge = stageLever != null ? stageLever.SignedValue : Mathf.Clamp(lever.Value, -1f, 1f);
            Sprite sprite = charge > 0f ? positiveSprite : negativeSprite;
            float amount = Mathf.Abs(charge);
            chargeRenderer.enabled = bodyRenderer.enabled && sprite != null && amount > 0f;
            if (!chargeRenderer.enabled) return;

            chargeRenderer.sprite = sprite;
            chargeRenderer.flipX = bodyRenderer.flipX;
            chargeRenderer.flipY = bodyRenderer.flipY;
            Color color = bodyRenderer.color;
            color.a *= amount;
            chargeRenderer.color = color;
        }

        private void OnDisable()
        {
            if (chargeRenderer != null) chargeRenderer.enabled = false;
        }

    }
}
