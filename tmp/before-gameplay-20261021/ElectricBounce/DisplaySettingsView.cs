using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace sung_wook
{
    /// <summary>저장된 두 설정 창을 공유 화면 설정과 연결한다.</summary>
    [DisallowMultipleComponent]
    public sealed class DisplaySettingsView : MonoBehaviour
    {
        [SerializeField] private Dropdown screenModeDropdown;
        [SerializeField] private Dropdown resolutionDropdown;

        private static readonly string[] ScreenModes = { "전체화면", "창 모드" };

        private void OnEnable()
        {
            DisplayPreferences.Changed += Refresh;
            if (screenModeDropdown != null) screenModeDropdown.onValueChanged.AddListener(OnScreenModeChanged);
            if (resolutionDropdown != null) resolutionDropdown.onValueChanged.AddListener(OnResolutionChanged);
            Refresh();
        }

        private void OnDisable()
        {
            DisplayPreferences.Changed -= Refresh;
            if (screenModeDropdown != null) screenModeDropdown.onValueChanged.RemoveListener(OnScreenModeChanged);
            if (resolutionDropdown != null) resolutionDropdown.onValueChanged.RemoveListener(OnResolutionChanged);
            DisplayPreferences.Save();
        }

        private void Refresh()
        {
            if (screenModeDropdown != null)
            {
                if (!HasOptions(screenModeDropdown, ScreenModes))
                {
                    screenModeDropdown.ClearOptions();
                    screenModeDropdown.AddOptions(new List<string>(ScreenModes));
                }
                screenModeDropdown.SetValueWithoutNotify(DisplayPreferences.IsFullscreen ? 0 : 1);
                screenModeDropdown.RefreshShownValue();
            }

            if (resolutionDropdown == null) return;
            IReadOnlyList<Vector2Int> resolutions = DisplayPreferences.Resolutions;
            bool optionsMatch = resolutionDropdown.options.Count == resolutions.Count;
            for (int index = 0; optionsMatch && index < resolutions.Count; index++)
                optionsMatch = resolutionDropdown.options[index].text == ResolutionLabel(resolutions[index]);
            // 공유 값이 바뀔 때 펼쳐진 목록을 불필요하게 다시 만들지 않는다.
            if (!optionsMatch)
            {
                var labels = new List<string>(resolutions.Count);
                for (int index = 0; index < resolutions.Count; index++) labels.Add(ResolutionLabel(resolutions[index]));
                // 열린 팝업은 이전 옵션의 인덱스를 보관하므로 목록 교체 전에 즉시 정리한다.
                bool wasEnabled = resolutionDropdown.enabled;
                resolutionDropdown.enabled = false;
                resolutionDropdown.ClearOptions();
                resolutionDropdown.AddOptions(labels);
                resolutionDropdown.enabled = wasEnabled;
            }
            resolutionDropdown.SetValueWithoutNotify(DisplayPreferences.SelectedResolutionIndex);
            resolutionDropdown.RefreshShownValue();
        }

        private static bool HasOptions(Dropdown dropdown, IReadOnlyList<string> labels)
        {
            if (dropdown.options.Count != labels.Count) return false;
            for (int index = 0; index < labels.Count; index++)
                if (dropdown.options[index].text != labels[index]) return false;
            return true;
        }

        private static string ResolutionLabel(Vector2Int resolution) => resolution.x + " × " + resolution.y;

        private void OnScreenModeChanged(int index)
        {
            DisplayPreferences.SetFullscreen(index == 0);
            ElectricSfxPlayer.Play(SfxCue.UiClick);
        }

        private void OnResolutionChanged(int index)
        {
            DisplayPreferences.SetResolutionIndex(index);
            ElectricSfxPlayer.Play(SfxCue.UiClick);
        }
    }
}
