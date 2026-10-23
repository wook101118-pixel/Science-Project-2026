using UnityEngine;
using UnityEngine.UI;

namespace sung_wook
{
                                                           
    [DisallowMultipleComponent]
    public sealed class SfxSettingsView : MonoBehaviour
    {
        [SerializeField] private Slider volumeSlider;
        [SerializeField] private Text volumeValueText;
        [SerializeField] private Toggle muteToggle;

        private void OnEnable()
        {
            SfxPreferences.Changed += Refresh;
            if (volumeSlider != null) volumeSlider.onValueChanged.AddListener(OnVolumeChanged);
            if (muteToggle != null) muteToggle.onValueChanged.AddListener(OnMuteChanged);
            Refresh();
        }

        private void OnDisable()
        {
            SfxPreferences.Changed -= Refresh;
            if (volumeSlider != null) volumeSlider.onValueChanged.RemoveListener(OnVolumeChanged);
            if (muteToggle != null) muteToggle.onValueChanged.RemoveListener(OnMuteChanged);
            SfxPreferences.Save();
        }

        private void Refresh()
        {
            float volume = SfxPreferences.Volume;
            if (volumeSlider != null) volumeSlider.SetValueWithoutNotify(volume);
            if (volumeValueText != null) volumeValueText.text = Mathf.RoundToInt(volume * 100f) + "%";
            if (muteToggle != null) muteToggle.SetIsOnWithoutNotify(SfxPreferences.IsMuted);
        }

        private void OnVolumeChanged(float volume) => SfxPreferences.SetVolume(volume);

        private void OnMuteChanged(bool muted)
        {
            SfxPreferences.SetMuted(muted);
            ElectricSfxPlayer.Play(SfxCue.UiClick);
        }
    }
}
