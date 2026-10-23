using UnityEngine;
using UnityEngine.UI;

namespace sung_wook
{
    
        /// <summary>씬에 저장된 설정 UI와 유지되는 효과음 설정을 연결한다.</summary>
        [DisallowMultipleComponent]
        public sealed class BGMSettingView : MonoBehaviour
        {
            [SerializeField] private Slider volumeSlider;
            [SerializeField] private Text volumeValueText;
            [SerializeField] private Toggle muteToggle;

            private void OnEnable()
            {
                BGMPreferences.Changed += Refresh;
                if (volumeSlider != null) volumeSlider.onValueChanged.AddListener(OnVolumeChanged);
                if (muteToggle != null) muteToggle.onValueChanged.AddListener(OnMuteChanged);
                Refresh();
            }

            private void OnDisable()
            {
                BGMPreferences.Changed -= Refresh;
                if (volumeSlider != null) volumeSlider.onValueChanged.RemoveListener(OnVolumeChanged);
                if (muteToggle != null) muteToggle.onValueChanged.RemoveListener(OnMuteChanged);
                BGMPreferences.Save();
            }

            private void Refresh()
            {
                float volume = BGMPreferences.Volume;
                if (volumeSlider != null) volumeSlider.SetValueWithoutNotify(volume);
                if (volumeValueText != null) volumeValueText.text = Mathf.RoundToInt(volume * 100f) + "%";
                if (muteToggle != null) muteToggle.SetIsOnWithoutNotify(BGMPreferences.IsMuted);
            }

            private void OnVolumeChanged(float volume) => BGMPreferences.SetVolume(volume);

            private void OnMuteChanged(bool muted)
            {
                BGMPreferences.SetMuted(muted);
                ElectricSfxPlayer.Play(SfxCue.UiClick);
            }
        }
    }

