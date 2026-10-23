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
        private PlayerMovement movement;
        private ElectricStageChallenge2D challenge;
        private ElectricExperimentPlan2D experimentPlan;
        private Vector2 checkpointPosition;
        private bool hasCheckpoint;
        private float recoveryFeedbackUntil;

        public bool IsInitialized { get; private set; }
        public int RetryCount { get; private set; }
        public string MasteryHint => challenge != null ? challenge.Hint : "";
        public string ChallengeProgressText => challenge != null ? challenge.ProgressText : "";
        public string ChallengeCompactText => challenge != null ? challenge.CompactText : "";
        public string LatestResultText => challenge != null ? challenge.ResultSummaryText : "";
        public string RecoveryFeedback => Time.realtimeSinceStartup < recoveryFeedbackUntil
            ? "최근 발판부터 · 학습·별 기록은 유지됩니다" : "";
        public bool CanRetryCheckpoint => IsInitialized && hasCheckpoint && selector != null
            && selector.Ball != null && Time.timeScale > 0f && !SceneTransitionController.IsBusy
            && !(GetComponent<ElectricStageProgression>()?.BlocksGameplayInput ?? false);

        /// <summary>기존 컴포넌트를 우선 사용하고 비어 있는 기능 연결만 보충한다. UI나 플레이어는 생성하지 않는다.</summary>
        public void Configure(ElectricStageSelector owner)
        {
            selector = owner;
            learning = GetComponent<ElectricLearningController2D>();
            if (learning == null) learning = gameObject.AddComponent<ElectricLearningController2D>();
            learning.Configure(owner);
            PlayerMovement nextMovement = owner.Ball != null ? owner.Ball.GetComponent<PlayerMovement>() : null;
            if (movement != nextMovement)
            {
                if (movement != null) movement.LandedOnGround -= RememberLanding;
                movement = nextMovement;
                if (movement != null) movement.LandedOnGround += RememberLanding;
            }
            if (challenge == null) challenge = GetComponent<ElectricStageChallenge2D>();
            if (challenge == null) challenge = gameObject.AddComponent<ElectricStageChallenge2D>();
            challenge.Configure(owner, this);
            if (experimentPlan == null) experimentPlan = GetComponent<ElectricExperimentPlan2D>();
            if (experimentPlan == null) experimentPlan = gameObject.AddComponent<ElectricExperimentPlan2D>();
            experimentPlan.Configure(owner);

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

            selector.Ball.ResetAt(position, selector.SelectedStage <= 2 ? Polarity.Neutral : Polarity.Positive);
            selector.Lever?.UseLeverInput();
            learning?.ResetLesson();
            if (selector.SelectedStage == 1) GetComponent<TutorialController2D>()?.ResetTutorial();
            checkpointPosition = position;
            hasCheckpoint = true;
            RetryCount = 0;
            recoveryFeedbackUntil = 0f;
            challenge?.ResetAttempt();
            experimentPlan?.ResetAttempt();
        }

        private void RememberLanding(Vector2 position)
        {
            if (!IsInitialized || selector == null || selector.Ball == null
                || !selector.Ball.CanReceiveForce || learning.IsPreparingTrial) return;
            if (selector.TryGetSpawnPosition(out Vector2 spawn) && position.y < spawn.y - 1.5f) return;
            // 실제 착지 때의 공 중심을 재사용하고 바닥 안쪽으로 복귀하지 않게 한다.
            checkpointPosition = position + Vector2.up * 0.05f;
            hasCheckpoint = true;
        }

        public bool RetryCheckpoint()
        {
            if (!CanRetryCheckpoint) return false;
            RetryCount++;
            if (selector.SelectedStage == 3 && learning.IsPreparingTrial)
                movement.ResetMotion(); // 이미 고정된 실험은 기준·별을 지운 뒤 다시 준비하지 않는다.
            else
                selector.Ball.ReturnToCheckpoint(checkpointPosition);
            learning.ResetTransientObservations();
            recoveryFeedbackUntil = Time.realtimeSinceStartup + 2.5f;
            return true;
        }

        private void FixedUpdate()
        {
            if (CanRetryCheckpoint && !learning.IsPreparingTrial
                && selector.TryGetSpawnPosition(out Vector2 spawn)
                && selector.Ball.Body.position.y < spawn.y - 8f)
                RetryCheckpoint();
        }

        private void OnDestroy()
        {
            if (movement != null) movement.LandedOnGround -= RememberLanding;
        }
    }
}
