using TMPro;
using UnityEngine;

namespace sung_wook
{
    /// <summary>Saved apparatus markings follow the same electrical state used by the experiment.</summary>
    public sealed class ScienceApparatusView : MonoBehaviour
    {
        [SerializeField] private ContactCharger2D contact;
        [SerializeField] private DistanceLauncher2D launcher;
        [SerializeField] private UniformField2D field;
        [SerializeField] private SpriteRenderer electrode;
        [SerializeField] private TMP_Text chargeLabel;
        [SerializeField] private Transform[] fieldArrows;
        [SerializeField] private TMP_Text upperSign;
        [SerializeField] private TMP_Text lowerSign;

        private void LateUpdate()
        {
            if (electrode != null)
            {
                float charge = contact != null ? contact.SignedCharge : launcher != null ? launcher.SourceChargeMagnitude : 0f;
                electrode.color = charge > .001f ? new Color(.80f,.28f,.23f) : charge < -.001f
                    ? new Color(.20f,.42f,.76f) : new Color(.48f,.53f,.54f);
                if (chargeLabel != null) chargeLabel.text = charge > .001f ? "+" : charge < -.001f ? "−" : "0";
            }
            if (field == null) return;
            bool down = field.FieldDirection.y < 0f;
            if (upperSign != null) upperSign.text = down ? "+  +  +  +  +  +  +" : "−  −  −  −  −  −  −";
            if (lowerSign != null) lowerSign.text = down ? "−  −  −  −  −  −  −" : "+  +  +  +  +  +  +";
            if (fieldArrows == null) return;
            foreach (Transform arrow in fieldArrows)
                if (arrow != null) arrow.localRotation = Quaternion.Euler(0,0,down ? 0 : 180);
        }
    }
}
