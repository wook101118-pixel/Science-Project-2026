using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace sung_wook
{
    /// <summary>저장된 일시정지 UI와 게임 시간·입력의 정지 및 복원을 담당한다.</summary>
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public sealed class PauseMenuController : MonoBehaviour
    {
        public const string MainMenuPath = "Assets/@Project/@sung_wook/@Game/00_Scenes/MainMenu.unity";

        [SerializeField] private GameObject menuRoot;
        [SerializeField] private Button resumeButton;
        [SerializeField] private Button restartButton;
        [SerializeField] private Button mainMenuButton;
        [SerializeField] private Button settingsButton;
        [SerializeField] private Button closeSettingsButton;
        [SerializeField] private GameObject settingsWindow;

        private readonly List<MonoBehaviour> suspendedInputs = new List<MonoBehaviour>();
        private bool ownsPause;
        private float previousTimeScale = 1f;

        public bool IsPaused { get; private set; }
        public bool IsSettingsOpen => settingsWindow != null && settingsWindow.activeSelf;

        private void Awake()
        {
            if (menuRoot != null) menuRoot.SetActive(false);
            if (settingsWindow != null) settingsWindow.SetActive(false);
        }

        private void OnEnable()
        {
            if (resumeButton != null) resumeButton.onClick.AddListener(Resume);
            if (restartButton != null) restartButton.onClick.AddListener(RestartStage);
            if (mainMenuButton != null) mainMenuButton.onClick.AddListener(ReturnToMainMenu);
            if (settingsButton != null) settingsButton.onClick.AddListener(OpenSettings);
            if (closeSettingsButton != null) closeSettingsButton.onClick.AddListener(CloseSettings);
        }

        private void OnDisable()
        {
            if (resumeButton != null) resumeButton.onClick.RemoveListener(Resume);
            if (restartButton != null) restartButton.onClick.RemoveListener(RestartStage);
            if (mainMenuButton != null) mainMenuButton.onClick.RemoveListener(ReturnToMainMenu);
            if (settingsButton != null) settingsButton.onClick.RemoveListener(OpenSettings);
            if (closeSettingsButton != null) closeSettingsButton.onClick.RemoveListener(CloseSettings);
            // 씬 전환과 완료 화면이 소유한 정지 상태를 덮어쓰지 않는다.
            if (!SceneTransitionController.IsBusy && !ProgressionBlocksInput()) RestoreGameplay();
            ownsPause = false;
            IsPaused = false;
            suspendedInputs.Clear();
            if (menuRoot != null) menuRoot.SetActive(false);
            if (settingsWindow != null) settingsWindow.SetActive(false);
        }

        private void Update()
        {
            if (SceneTransitionController.IsBusy) return;
            // 로더가 시작 후 실패해 원래 씬에 남은 경우 메뉴를 다시 조작할 수 있다.
            if (IsPaused && !ownsPause)
            {
                ownsPause = true;
                SetButtonsInteractable(true);
            }
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                TogglePause();
        }

        public void TogglePause()
        {
            if (IsSettingsOpen) CloseSettings();
            else if (IsPaused) Resume();
            else Pause();
        }

        public void Pause()
        {
            if (IsPaused || menuRoot == null || SceneTransitionController.IsBusy
                || ProgressionBlocksInput() || Time.timeScale <= 0f) return;

            previousTimeScale = Time.timeScale;
            ownsPause = true;
            IsPaused = true;
            SuspendGameplayInputs();
            Time.timeScale = 0f;
            ElectricSfxPlayer.SetGameplayPaused(true);
            if (settingsWindow != null) settingsWindow.SetActive(false);
            menuRoot.SetActive(true);
            if (resumeButton != null) resumeButton.Select();
            ElectricSfxPlayer.Play(SfxCue.UiClick);
        }

        public void Resume()
        {
            if (!IsPaused || IsSettingsOpen || SceneTransitionController.IsBusy || ProgressionBlocksInput()) return;
            menuRoot.SetActive(false);
            if (settingsWindow != null) settingsWindow.SetActive(false);
            IsPaused = false;
            RestoreGameplay();
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            ElectricSfxPlayer.Play(SfxCue.UiClick);
        }

        public void OpenSettings()
        {
            if (!IsPaused || IsSettingsOpen || settingsWindow == null
                || SceneTransitionController.IsBusy || ProgressionBlocksInput()) return;
            menuRoot.SetActive(false);
            settingsWindow.SetActive(true);
            Slider volume = settingsWindow.GetComponentInChildren<Slider>();
            if (volume != null) volume.Select();
            else if (closeSettingsButton != null) closeSettingsButton.Select();
            ElectricSfxPlayer.Play(SfxCue.UiClick);
        }

        public void CloseSettings()
        {
            if (!IsPaused || !IsSettingsOpen || SceneTransitionController.IsBusy || ProgressionBlocksInput()) return;
            settingsWindow.SetActive(false);
            menuRoot.SetActive(true);
            if (settingsButton != null) settingsButton.Select();
            ElectricSfxPlayer.Play(SfxCue.UiClick);
        }

        public void RestartStage() => LoadScene(gameObject.scene.path);

        public void ReturnToMainMenu() => LoadScene(MainMenuPath);

        private void LoadScene(string path)
        {
            if (!IsPaused || IsSettingsOpen || SceneTransitionController.IsBusy || ProgressionBlocksInput()) return;
            if (!SceneTransitionController.LoadScene(path)) return;
            ElectricSfxPlayer.Play(SfxCue.UiClick);
            // 전환기가 unscaled 시간으로 페이드하며 새 씬의 시간을 재개한다.
            // 이전 씬이 파괴될 때 OnDisable에서 시간을 복원하지 않는다.
            ownsPause = false;
            SetButtonsInteractable(false);
        }

        private void SetButtonsInteractable(bool interactable)
        {
            if (resumeButton != null) resumeButton.interactable = interactable;
            if (restartButton != null) restartButton.interactable = interactable;
            if (mainMenuButton != null) mainMenuButton.interactable = interactable;
            if (settingsButton != null) settingsButton.interactable = interactable;
            if (closeSettingsButton != null) closeSettingsButton.interactable = interactable;
        }

        private bool ProgressionBlocksInput()
        {
            if (!gameObject.scene.IsValid()) return false;
            foreach (GameObject root in gameObject.scene.GetRootGameObjects())
                foreach (ElectricStageProgression progression in root.GetComponentsInChildren<ElectricStageProgression>())
                    if (progression.BlocksGameplayInput) return true;
            return false;
        }

        private void SuspendGameplayInputs()
        {
            suspendedInputs.Clear();
            foreach (GameObject root in gameObject.scene.GetRootGameObjects())
            {
                foreach (MonoBehaviour component in root.GetComponentsInChildren<MonoBehaviour>())
                {
                    if (component == null || !component.isActiveAndEnabled || !IsGameplayInput(component)) continue;
                    suspendedInputs.Add(component);
                    component.enabled = false;
                }
                foreach (PlayerMovement movement in root.GetComponentsInChildren<PlayerMovement>())
                    movement.SetMovex(0f);
            }
        }

        private static bool IsGameplayInput(MonoBehaviour component) =>
            component is PlayerController || component is PlayerInputReader
            || component is ElectricStageInput2D || component is ElectricLearningInput2D
            || component is GearLeverController || component is global::LeverReader
            || component is global::LeverMover || component is DistanceLauncher2D
            || component is VoltageLauncher2D;

        private void RestoreGameplay()
        {
            if (!ownsPause) return;
            ownsPause = false;
            Time.timeScale = previousTimeScale;
            ElectricSfxPlayer.SetGameplayPaused(false);
            foreach (MonoBehaviour component in suspendedInputs)
                if (component != null) component.enabled = true;
            suspendedInputs.Clear();
        }
    }
}
