using UnityEngine;

namespace sung_wook
{
                 
                                                 
                                                     
                  
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
        public bool IsDead { get; private set; }
        public float FallDeathY { get; private set; }
        public string MasteryHint => challenge != null ? challenge.Hint : "";
        public string ChallengeProgressText => challenge != null ? challenge.ProgressText : "";
        public string ChallengeCompactText => challenge != null ? challenge.CompactText : "";
        public string LatestResultText => challenge != null ? challenge.ResultSummaryText : "";
        public string RecoveryFeedback => Time.realtimeSinceStartup < recoveryFeedbackUntil
            ? selector != null && selector.SelectedStage == 3 && learning != null && learning.IsPreparingTrial
                ? "발사 위치로 복귀 · 기준·학습·별 유지"
                : "최근 발판부터 · 학습·별 기록은 유지됩니다"
            : "";
        public bool CanRetryCheckpoint => !IsDead && IsInitialized && hasCheckpoint && selector != null
            && selector.Ball != null && Time.timeScale > 0f && !SceneTransitionController.IsBusy
            && !(GetComponent<ElectricStageProgression>()?.BlocksGameplayInput ?? false);

                                                                                      
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
                                                                    
            RestartStage();
            ConfigureFallBoundary();
            IsInitialized = true;
        }

                                                                     
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
            IsDead = false;
            recoveryFeedbackUntil = 0f;
            challenge?.ResetAttempt();
            experimentPlan?.ResetAttempt();
        }

        private void RememberLanding(Vector2 position)
        {
            if (!IsInitialized || selector == null || selector.Ball == null
                || !selector.Ball.CanReceiveForce || learning.IsPreparingTrial) return;
            if (selector.TryGetSpawnPosition(out Vector2 spawn) && position.y < spawn.y - 1.5f) return;
                                                       
            checkpointPosition = position + Vector2.up * 0.05f;
            hasCheckpoint = true;
        }

                                                                                  
        internal void ResetComparisonCheckpoint(Vector2 position)
        {
            if (selector == null || selector.SelectedStage != 3) return;
            checkpointPosition = position;
            hasCheckpoint = true;
            recoveryFeedbackUntil = 0f;
        }

        public bool RetryCheckpoint()
        {
            if (!CanRetryCheckpoint) return false;
            RetryCount++;
            if (selector.SelectedStage == 3 && learning.IsPreparingTrial)
                movement.ResetMotion();                                      
            else if (selector.SelectedStage == 3 && (!learning.ComparisonChallengeSatisfied
                || (selector.TryGetSpawnPosition(out Vector2 spawn)
                    && Vector2.Distance(checkpointPosition, spawn) < 0.3f)))
                learning.RetryComparisonPosition();
            else
                selector.Ball.ReturnToCheckpoint(checkpointPosition);
            learning.ResetTransientObservations();
            recoveryFeedbackUntil = Time.realtimeSinceStartup + 2.5f;
            return true;
        }

        private void ConfigureFallBoundary()
        {
            float lowest = float.PositiveInfinity;
            foreach (GameObject root in gameObject.scene.GetRootGameObjects())
                foreach (Collider2D ground in root.GetComponentsInChildren<Collider2D>())
                {
                    if (!ground.enabled || ground.isTrigger || ground.shapeCount == 0
                        || !ground.CompareTag("Ground")
                        || (ground.attachedRigidbody != null && ground.attachedRigidbody.bodyType != RigidbodyType2D.Static)) continue;
                    lowest = Mathf.Min(lowest, ground.bounds.min.y);
                }
            FallDeathY = float.IsPositiveInfinity(lowest)
                ? selector.Ball.Body.position.y - 8f : lowest - .25f;
        }

        private void FixedUpdate()
        {
            if (!IsInitialized || IsDead || selector == null || selector.Ball == null
                || Time.timeScale <= 0f || SceneTransitionController.IsBusy || learning.IsPreparingTrial
                || (GetComponent<ElectricStageProgression>()?.BlocksGameplayInput ?? false)) return;
            if (selector.Ball.Body.position.y >= FallDeathY) return;
            IsDead = true;
            movement.ResetMotion();
            selector.Ball.Body.simulated = false;
            GetComponent<ElectricStageProgression>()?.ShowFallDeath();
        }

        private void OnDestroy()
        {
            if (movement != null) movement.LandedOnGround -= RememberLanding;
        }
    }
}
