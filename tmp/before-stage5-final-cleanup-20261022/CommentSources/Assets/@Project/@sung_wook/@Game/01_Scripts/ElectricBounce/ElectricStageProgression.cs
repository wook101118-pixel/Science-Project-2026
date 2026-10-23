using UnityEngine;

namespace sung_wook
{
    /// <summary>현재 씬의 출구 완료 이벤트를 다음 씬 또는 마지막 완료 화면에 연결한다.</summary>
    [DisallowMultipleComponent]
    public sealed class ElectricStageProgression : MonoBehaviour
    {
        private const string SceneFolder = "Assets/@Project/@sung_wook/@Game/00_Scenes/@InGame/Stage/";
        public const string UnlockKey = "ElectricBounce.Progression.UnlockedStage";
        [SerializeField, Min(0f)] private float transitionDelay = 0.5f;
        [SerializeField] private ElectricGameCompleteView completionView;

        private ElectricStageSelector selector;
        private ElectricGoal2D goal;

        public bool IsTransitioning { get; private set; }
        public bool IsGameComplete { get; private set; }
        public bool IsAwaitingContinue { get; private set; }
        public bool BlocksGameplayInput => IsTransitioning || IsGameComplete || IsAwaitingContinue;

        public static string StageScenePath(int stage) => stage >= 1 && stage <= 5
            ? SceneFolder + "Stage_" + stage.ToString("00") + ".unity" : "";
        public static int HighestUnlockedStage => Mathf.Clamp(PlayerPrefs.GetInt(UnlockKey, 1), 1, 5);
        public static bool IsStageUnlocked(int stage) => stage >= 1 && stage <= HighestUnlockedStage;

        public static void UnlockStage(int stage)
        {
            if (stage < 1 || stage > 5 || stage <= HighestUnlockedStage) return;
            PlayerPrefs.SetInt(UnlockKey, stage);
            PlayerPrefs.Save();
        }

        public void Configure(ElectricStageSelector owner)
        {
            if (selector != owner)
            {
                IsTransitioning = false;
                IsGameComplete = false;
                IsAwaitingContinue = false;
            }
            selector = owner;
            BindGoal();
        }

        private void OnEnable() => BindGoal();

        private void OnDisable()
        {
            UnbindGoal();
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

            ResolveCompletionView();
            if (completionView == null)
            {
                Debug.LogError("The stage is missing its saved GameCompleteScreen result UI.", this);
                return;
            }
            GetComponent<ElectricStageChallenge2D>()?.RecordCompletion();
            GetComponent<ElectricExperimentPlan2D>()?.RecordClear();
            UnlockStage(Mathf.Min(5, selector.SelectedStage + 1));
            IsGameComplete = selector.SelectedStage == 5;
            IsAwaitingContinue = true;
            BlockGameplayInputs();
            string summary = GetComponent<ElectricStageSession2D>()?.LatestResultText ?? "학습 목표와 별 수집을 완료했습니다.";
            completionView.ShowStageResult(selector.SelectedStage, summary);
        }

        public void ContinueNextStage() => completionView?.ContinueNextStage();
        public void RetryCurrentStage() => completionView?.RetryCurrentStage();

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
