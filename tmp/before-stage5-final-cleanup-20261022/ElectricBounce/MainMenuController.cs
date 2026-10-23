using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.Events;

namespace sung_wook
{
    /// <summary>씬에 저장된 기본 메뉴 UI의 시작·설정창·종료 동작을 연결한다.</summary>
    [DisallowMultipleComponent]
    public sealed class MainMenuController : MonoBehaviour
    {
        [SerializeField] private string firstStagePath = ElectricGameCompleteView.FirstStagePath;
        [SerializeField] private GameObject mainContent;
        [SerializeField] private GameObject settingsWindow;
        [SerializeField] private Button startButton;
        [SerializeField] private Button settingsButton;
        [SerializeField] private Button quitButton;
        [SerializeField] private Button closeSettingsButton;
        [SerializeField] private Button practiceButton;
        [SerializeField] private GameObject practiceWindow;
        [SerializeField] private Button closePracticeButton;
        [SerializeField] private Button[] stageButtons = new Button[5];
        private UnityAction[] practiceActions;

        private bool quitting;
        public bool IsSettingsOpen => settingsWindow != null && settingsWindow.activeSelf;
        public bool IsPracticeOpen => practiceWindow != null && practiceWindow.activeSelf;

        private void Awake()
        {
            firstStagePath = ElectricGameCompleteView.FirstStagePath;
            if (practiceWindow != null) practiceWindow.SetActive(false);
            SetSettingsOpen(false);
        }

        private void OnEnable()
        {
            if (startButton != null) startButton.onClick.AddListener(StartGame);
            if (settingsButton != null) settingsButton.onClick.AddListener(OpenSettings);
            if (quitButton != null) quitButton.onClick.AddListener(QuitGame);
            if (closeSettingsButton != null) closeSettingsButton.onClick.AddListener(CloseSettings);
            if (practiceButton != null) practiceButton.onClick.AddListener(OpenPractice);
            if (closePracticeButton != null) closePracticeButton.onClick.AddListener(ClosePractice);
            practiceActions = new UnityAction[stageButtons.Length];
            for (int index = 0; index < stageButtons.Length; index++)
            {
                int stage = index + 1;
                practiceActions[index] = () => { PracticeStage(stage); };
                if (stageButtons[index] != null) stageButtons[index].onClick.AddListener(practiceActions[index]);
            }
        }

        private void OnDisable()
        {
            if (startButton != null) startButton.onClick.RemoveListener(StartGame);
            if (settingsButton != null) settingsButton.onClick.RemoveListener(OpenSettings);
            if (quitButton != null) quitButton.onClick.RemoveListener(QuitGame);
            if (closeSettingsButton != null) closeSettingsButton.onClick.RemoveListener(CloseSettings);
            if (practiceButton != null) practiceButton.onClick.RemoveListener(OpenPractice);
            if (closePracticeButton != null) closePracticeButton.onClick.RemoveListener(ClosePractice);
            if (practiceActions != null)
                for (int index = 0; index < practiceActions.Length; index++)
                    if (stageButtons[index] != null) stageButtons[index].onClick.RemoveListener(practiceActions[index]);
        }

        private void Update()
        {
            bool available = !quitting && !SceneTransitionController.IsBusy;
            if (startButton != null) startButton.interactable = available;
            if (settingsButton != null) settingsButton.interactable = available;
            if (quitButton != null) quitButton.interactable = available;
            if (closeSettingsButton != null) closeSettingsButton.interactable = available;
            if (practiceButton != null) practiceButton.interactable = available;
            if (closePracticeButton != null) closePracticeButton.interactable = available;
            if (IsPracticeOpen) RefreshPractice(available);
            if (available && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                if (IsPracticeOpen) ClosePractice();
                else if (IsSettingsOpen) CloseSettings();
            }
        }

        public void StartGame()
        {
            if (quitting || IsSettingsOpen || IsPracticeOpen || SceneTransitionController.IsBusy) return;
            if (SceneTransitionController.LoadScene(firstStagePath))
                ElectricSfxPlayer.Play(SfxCue.UiClick);
        }

        public void OpenSettings()
        {
            if (quitting || IsSettingsOpen || IsPracticeOpen || SceneTransitionController.IsBusy) return;
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

        public void OpenPractice()
        {
            if (quitting || IsSettingsOpen || IsPracticeOpen || SceneTransitionController.IsBusy || practiceWindow == null) return;
            if (mainContent != null) mainContent.SetActive(false);
            practiceWindow.SetActive(true);
            RefreshPractice(true);
            if (stageButtons.Length > 0 && stageButtons[0] != null) stageButtons[0].Select();
            ElectricSfxPlayer.Play(SfxCue.UiClick);
        }

        public void ClosePractice()
        {
            if (quitting || !IsPracticeOpen || SceneTransitionController.IsBusy) return;
            practiceWindow.SetActive(false);
            if (mainContent != null) mainContent.SetActive(true);
            if (practiceButton != null) practiceButton.Select();
            ElectricSfxPlayer.Play(SfxCue.UiClick);
        }

        public bool PracticeStage(int stage)
        {
            if (quitting || !IsPracticeOpen || SceneTransitionController.IsBusy || !ElectricStageProgression.IsStageUnlocked(stage)) return false;
            bool accepted = SceneTransitionController.LoadScene(ElectricStageProgression.StageScenePath(stage));
            if (accepted) ElectricSfxPlayer.Play(SfxCue.UiClick);
            return accepted;
        }

        private void RefreshPractice(bool available)
        {
            string[] names = { "튜토리얼", "접촉 대전", "전기력 비교", "평행판 전기장", "전압 발사" };
            for (int index = 0; index < stageButtons.Length && index < names.Length; index++)
            {
                Button button = stageButtons[index];
                if (button == null) continue;
                bool unlocked = ElectricStageProgression.IsStageUnlocked(index + 1);
                button.interactable = available && unlocked;
                Text label = button.GetComponentInChildren<Text>(true);
                if (label != null)
                {
                    label.text = (index + 1) + ". " + names[index] + (unlocked ? " · 연습" : " · 잠김");
                    Canvas canvas = label.canvas;
                    float scale = canvas != null ? Mathf.Max(.01f,canvas.rootCanvas.scaleFactor) : 1f;
                    label.fontSize = Mathf.Max(38,Mathf.CeilToInt(14f/scale));
                }
            }
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
