using UnityEngine;

namespace sung_wook
{
    /// <summary>
    /// 기존 학습·표시·입력 컴포넌트의 연결과 한 번의 실험 수명주기를 관리한다.
    /// 씬 선택이나 입력 해석, 플레이어 이동, 학습 완료 판정은 직접 수행하지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ElectricStageSession2D : MonoBehaviour
    {
        private ElectricStageSelector selector;
        private ElectricLearningController2D learning;

        public bool IsInitialized { get; private set; }

        /// <summary>기존 컴포넌트를 우선 사용하고 비어 있는 기능 연결만 보충한다. UI나 플레이어는 생성하지 않는다.</summary>
        public void Configure(ElectricStageSelector owner)
        {
            selector = owner;
            learning = GetComponent<ElectricLearningController2D>();
            if (learning == null) learning = gameObject.AddComponent<ElectricLearningController2D>();
            learning.Configure(owner);

            var vectors = GetComponent<ElectricVectorDisplay2D>();
            // 전기력 수치는 기존 HUD로 관찰하며, 코드로 합성한 화살표는 만들지 않는다.
            if (vectors != null) vectors.enabled = false;

            if (owner.StageRoots != null)
            {
                foreach (GameObject root in owner.StageRoots)
                {
                    if (root == null) continue;
                    foreach (var view in root.GetComponentsInChildren<ElectricLearningStatusView>(true))
                        view.Configure(owner);
                }
            }

            // Only the split Stage scenes own cross-scene progression. The original
            // combined experiment scene keeps its existing stage-selection behavior.
            if (GetComponent<ElectricStageSceneBootstrap>() != null)
            {
                var progression = GetComponent<ElectricStageProgression>();
                if (progression == null) progression = gameObject.AddComponent<ElectricStageProgression>();
                progression.Configure(owner);
            }

            if (!Application.isPlaying) return;
            var learningInput = GetComponent<ElectricLearningInput2D>();
            if (learningInput == null) learningInput = gameObject.AddComponent<ElectricLearningInput2D>();
            learningInput.Configure(learning);

            var stageInput = GetComponent<ElectricStageInput2D>();
            if (stageInput == null) stageInput = gameObject.AddComponent<ElectricStageInput2D>();
            stageInput.Configure(owner);
        }

        private void Start()
        {
            // 씬의 모든 Awake가 끝난 다음 시작한다. 3번은 첫 실행부터 기준 저장 준비 상태여야 한다.
            RestartStage();
            IsInitialized = true;
        }

        /// <summary>현재 씬의 기록·수집물·발사 장치와 기존 공을 초기 상태로 되돌린다.</summary>
        public void RestartStage()
        {
            var progression = GetComponent<ElectricStageProgression>();
            if (progression != null && progression.BlocksGameplayInput) return;
            if (!Application.isPlaying || selector == null || selector.Ball == null
                || selector.CurrentStageRoot == null || !selector.TryGetSpawnPosition(out Vector2 position))
                return;

            GameObject stage = selector.CurrentStageRoot;
            foreach (var star in stage.GetComponentsInChildren<ElectricStar2D>(true)) star.ResetProgress();
            foreach (var goal in stage.GetComponentsInChildren<ElectricGoal2D>(true)) goal.ResetProgress();
            foreach (var launcher in stage.GetComponentsInChildren<DistanceLauncher2D>(true)) launcher.ResetLaunch();
            foreach (var launcher in stage.GetComponentsInChildren<VoltageLauncher2D>(true)) launcher.ResetCharge();
            foreach (var obstacle in stage.GetComponentsInChildren<ElectricObstacleMotion2D>(true)) obstacle.ResetMotion();

            selector.Ball.ResetAt(position, selector.SelectedStage == 2 ? Polarity.Neutral : Polarity.Positive);
            selector.Lever?.UseLeverInput();
            learning?.ResetLesson();
        }
    }
}
