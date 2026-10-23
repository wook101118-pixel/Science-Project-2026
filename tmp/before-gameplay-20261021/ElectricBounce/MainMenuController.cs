using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace sung_wook
{
    /// <summary>씬에 저장된 기본 메뉴 UI의 시작·설정창·종료 동작을 연결한다.</summary>
    [DisallowMultipleComponent]
    public sealed class MainMenuController : MonoBehaviour
    {
        [SerializeField] private string firstStagePath = "Assets/@Project/@sung_wook/@Game/00_Scenes/Stage/Stage_02.unity";
        [SerializeField] private GameObject mainContent;
        [SerializeField] private GameObject settingsWindow;
        [SerializeField] private Button startButton;
        [SerializeField] private Button settingsButton;
        [SerializeField] private Button quitButton;
        [SerializeField] private Button closeSettingsButton;

        private bool quitting;
        public bool IsSettingsOpen => settingsWindow != null && settingsWindow.activeSelf;

        private void Awake() => SetSettingsOpen(false);

        private void OnEnable()
        {
            if (startButton != null) startButton.onClick.AddListener(StartGame);
            if (settingsButton != null) settingsButton.onClick.AddListener(OpenSettings);
            if (quitButton != null) quitButton.onClick.AddListener(QuitGame);
            if (closeSettingsButton != null) closeSettingsButton.onClick.AddListener(CloseSettings);
        }

        private void OnDisable()
        {
            if (startButton != null) startButton.onClick.RemoveListener(StartGame);
            if (settingsButton != null) settingsButton.onClick.RemoveListener(OpenSettings);
            if (quitButton != null) quitButton.onClick.RemoveListener(QuitGame);
            if (closeSettingsButton != null) closeSettingsButton.onClick.RemoveListener(CloseSettings);
        }

        private void Update()
        {
            bool available = !quitting && !SceneTransitionController.IsBusy;
            if (startButton != null) startButton.interactable = available;
            if (settingsButton != null) settingsButton.interactable = available;
            if (quitButton != null) quitButton.interactable = available;
            if (closeSettingsButton != null) closeSettingsButton.interactable = available;
            if (available && IsSettingsOpen && Keyboard.current != null
                && Keyboard.current.escapeKey.wasPressedThisFrame) CloseSettings();
        }

        public void StartGame()
        {
            if (quitting || IsSettingsOpen || SceneTransitionController.IsBusy) return;
            if (SceneTransitionController.LoadScene(firstStagePath))
                ElectricSfxPlayer.Play(SfxCue.UiClick);
        }

        public void OpenSettings()
        {
            if (quitting || IsSettingsOpen || SceneTransitionController.IsBusy) return;
            SetSettingsOpen(true);
            ElectricSfxPlayer.Play(SfxCue.UiClick);
        }

        public void CloseSettings()
        {
            if (quitting || !IsSettingsOpen || SceneTransitionController.IsBusy) return;
            SetSettingsOpen(false);
            ElectricSfxPlayer.Play(SfxCue.UiClick);
        }

        public void QuitGame()
        {
            if (quitting || SceneTransitionController.IsBusy) return;
            quitting = true;
            ElectricSfxPlayer.Play(SfxCue.UiClick);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void SetSettingsOpen(bool open)
        {
            if (mainContent != null) mainContent.SetActive(!open);
            if (settingsWindow != null) settingsWindow.SetActive(open);
            Button selected = open ? closeSettingsButton : startButton;
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(selected != null ? selected.gameObject : null);
        }
    }
}
