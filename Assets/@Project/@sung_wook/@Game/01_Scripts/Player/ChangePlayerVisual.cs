using UnityEngine;

namespace sung_wook
{
                                                                 
    public class ChangePlayerVisual : ChangeVisual
    {
        [SerializeField, Min(0.001f)] private float fullChargeMagnitude = 1f;
        [SerializeField, Min(0f)] private float transitionDuration = 0.2f;

        private ElectricBall2D ball;
        private SpriteRenderer bodyRenderer;
        [SerializeField] private SpriteRenderer chargeRenderer;
        private TMPro.TMP_Text[] neutralLabels;
        private Color neutralColor;
        private Color positiveColor;
        private Color negativeColor;
        private float displayedCharge;
        private bool initialized;
        private bool usesPrefabVisuals;

        public override void Setup()
        {
                                                             
            if (chargeRenderer != null) chargeRenderer.enabled = false;
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
            if (existingNeutralVisual != null)
            {
                bodyRenderer = existingNeutralVisual.GetComponent<SpriteRenderer>();
                neutralLabels = existingNeutralVisual.GetComponentsInChildren<TMPro.TMP_Text>(true);
            }
                                                      
            usesPrefabVisuals = bodyRenderer == null || neutralLabels.Length == 0;
            if (usesPrefabVisuals)
            {
                initialized = true;
                base.Setup();
                return;
            }

            neutralColor = bodyRenderer.color;
            positiveColor = ReadVisualColor(_positiveVisual, Color.red);
            negativeColor = ReadVisualColor(_negativeVisual, new Color(0f, 0.48942327f, 1f, 1f));

            existingNeutralVisual.SetActive(true);
            UpdateChargeLabels(ReadSignedCharge());
            initialized = true;
        }

        public override void VisualChange(PlayerStateType newState)
        {
            Setup();
            if (usesPrefabVisuals)
            {
                base.VisualChange(newState);
                return;
            }
            CurrentVisualState = newState;
            UpdateChargeLabels(ReadSignedCharge());
        }

        private void LateUpdate() => UpdateChargeVisual(Time.deltaTime);

        private void UpdateChargeVisual(float deltaTime)
        {
            Setup();
            if (usesPrefabVisuals || bodyRenderer == null) return;

            float signedCharge = ReadSignedCharge();
            float target = Mathf.Clamp(signedCharge / Mathf.Max(0.001f, fullChargeMagnitude), -1f, 1f);
                                                               
            displayedCharge = transitionDuration > 0f
                ? Mathf.MoveTowards(displayedCharge, target, Mathf.Max(0f, deltaTime) / transitionDuration)
                : target;

            existingNeutralVisual.SetActive(true);
            Color chargedColor = displayedCharge > 0f ? positiveColor : negativeColor;
            chargedColor.a = neutralColor.a;
            bodyRenderer.color = Color.Lerp(neutralColor, chargedColor, Mathf.Abs(displayedCharge));
            UpdateChargeLabels(signedCharge);
        }

        private void OnDisable()
        {
            if (chargeRenderer != null) chargeRenderer.enabled = false;
        }

        private static Color ReadVisualColor(GameObject visual, Color fallback)
        {
            SpriteRenderer renderer = visual != null ? visual.GetComponentInChildren<SpriteRenderer>(true) : null;
            return renderer != null ? renderer.color : fallback;
        }

        private float ReadSignedCharge()
        {
            if (ball == null) ball = GetComponentInParent<ElectricBall2D>();
            float charge = ball != null ? ball.SignedCharge : ElectricBall2D.ChargeSign(CurrentVisualState);
            return float.IsNaN(charge) || float.IsInfinity(charge) ? 0f : charge;
        }

        private void UpdateChargeLabels(float signedCharge)
        {
            if (neutralLabels == null) return;
            string symbol = signedCharge > 0f ? "+" : signedCharge < 0f ? "-" : "0";
            foreach (TMPro.TMP_Text label in neutralLabels)
            {
                if (label == null) continue;
                label.text = symbol;
                label.enabled = true;
            }
        }
    }
}
