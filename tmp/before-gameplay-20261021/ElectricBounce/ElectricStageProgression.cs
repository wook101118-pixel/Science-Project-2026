using System.Collections;
using UnityEngine;

namespace sung_wook
{
    /// <summary>현재 씬의 출구 완료 이벤트를 다음 씬 또는 마지막 완료 화면에 연결한다.</summary>
    [DisallowMultipleComponent]
    public sealed class ElectricStageProgression : MonoBehaviour
    {
        private const string SceneFolder = "Assets/@Project/@sung_wook/@Game/00_Scenes/Stage/";
        [SerializeField, Min(0f)] private float transitionDelay = 0.5f;
        [SerializeField] private ElectricGameCompleteView completionView;

        private ElectricStageSelector selector;
        private ElectricGoal2D goal;
        private Coroutine transition;

        public bool IsTransitioning { get; private set; }
        public bool IsGameComplete { get; private set; }
        public bool BlocksGameplayInput => IsTransitioning || IsGameComplete;

        public static string StageScenePath(int stage) => SceneFolder + "Stage_" + stage.ToString("00") + ".unity";

        public void Configure(ElectricStageSelector owner)
        {
            if (selector != owner)
            {
                IsTransitioning = false;
                IsGameComplete = false;
            }
            selector = owner;
            BindGoal();
        }

        private void OnEnable() => BindGoal();

        private void OnDisable()
        {
            UnbindGoal();
            if (transition != null)
            {
                StopCoroutine(transition);
                transition = null;
            }
        }

        private void BindGoal()
        {
            UnbindGoal();
            if (!isActiveAndEnabled || selector == null || selector.CurrentStageRoot == null) return;
            goal = selector.GetStageComponent<ElectricGoal2D>(selector.SelectedStage);
            if (goal != null) goal.OnCleared.AddListener(OnGoalCleared);
        }

        private void UnbindGoal()
        {
            if (goal != null) goal.OnCleared.RemoveListener(OnGoalCleared);
            goal = null;
        }

        private void OnGoalCleared()
        {
            if (BlocksGameplayInput || selector == null || goal == null || !goal.IsCleared) return;
            ElectricLearningController2D lesson = selector.GetComponent<ElectricLearningController2D>();
            if (selector.Ball == null || selector.Ball.Stars < goal.RequiredStars || lesson == null
                || !lesson.isActiveAndEnabled || !lesson.CanClearGoal(selector.Ball)) return;

            if (selector.SelectedStage == 5)
            {
                ResolveCompletionView();
                if (completionView == null)
                {
                    Debug.LogError("Stage 5 is missing its saved GameCompleteScreen UI.", this);
                    return;
                }
                IsGameComplete = true;
                BlockGameplayInputs();
                completionView.Show();
                return;
            }

            string nextScene = StageScenePath(selector.SelectedStage + 1);
            if (!Application.CanStreamedLevelBeLoaded(nextScene))
            {
                Debug.LogError("The next stage is missing from Build Settings: " + nextScene, this);
                return;
            }
            IsTransitioning = true;
            BlockGameplayInputs();
            transition = StartCoroutine(LoadNextStage(nextScene));
        }

        private IEnumerator LoadNextStage(string scenePath)
        {
            // Let the clear state be visible even if the user paused gameplay.
            yield return new WaitForSecondsRealtime(transitionDelay);
            SceneTransitionController.LoadScene(scenePath);
            transition = null;
        }

        private void ResolveCompletionView()
        {
            if (completionView != null) return;
            foreach (GameObject root in gameObject.scene.GetRootGameObjects())
            {
                completionView = root.GetComponentInChildren<ElectricGameCompleteView>(true);
                if (completionView != null) return;
            }
        }

        private void BlockGameplayInputs()
        {
            // The next scene provides fresh enabled input components. During the clear
            // delay, T/R/charge commands must not revive the already cleared player.
            ElectricStageInput2D stageInput = GetComponent<ElectricStageInput2D>();
            ElectricLearningInput2D learningInput = GetComponent<ElectricLearningInput2D>();
            if (stageInput != null) stageInput.enabled = false;
            if (learningInput != null) learningInput.enabled = false;
            PlayerController playerInput = selector.Ball.GetComponent<PlayerController>();
            PlayerInputReader reader = selector.Ball.GetComponent<PlayerInputReader>();
            if (playerInput != null) playerInput.enabled = false;
            if (reader != null) reader.enabled = false;
            if (selector.Lever != null)
            {
                GearLeverController leverInput = selector.Lever.GetComponent<GearLeverController>();
                if (leverInput != null) leverInput.enabled = false;
                if (selector.Lever.Reader != null) selector.Lever.Reader.enabled = false;
                foreach (global::LeverMover mover in selector.Lever.GetComponentsInChildren<global::LeverMover>(true))
                    mover.enabled = false;
            }
        }
    }
}
