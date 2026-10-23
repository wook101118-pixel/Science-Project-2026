using UnityEngine;

namespace sung_wook
{
                 
                                  
                                                                       
                                                                    
                  
    [DisallowMultipleComponent]
    public sealed class ElectricStageSelector : MonoBehaviour
    {
        private const int FirstStage = 1;
        private const int LastStage = 5;

                                                 
        [SerializeField] private ElectricBall2D ball;
        [SerializeField] private StageLever2D lever;
        [SerializeField] private GameObject[] stageRoots = new GameObject[5];
        [SerializeField] private Transform spawnPoint;
        [SerializeField, Range(FirstStage, LastStage)] private int selectedStage = 2;
        [SerializeField] private bool showInstructions = true;

        private ElectricStageSession2D session;
        private IElectricForceProvider activeForceProvider;
        private MonoBehaviour activeForceComponent;
        private Vector2 initialSpawnPosition;
        private bool hasInitialSpawn;

        public int SelectedStage => selectedStage;
        public ElectricBall2D Ball => ball;
        public StageLever2D Lever => lever;
        public GameObject[] StageRoots => stageRoots;
        public Transform SpawnPoint => spawnPoint;
        public bool ShowInstructions => showInstructions;
        public GameObject CurrentStageRoot => GetStageRoot(selectedStage);

                                                                       
        public void Configure(ElectricBall2D existingBall, StageLever2D existingLever,
            GameObject[] stages, Transform spawn)
        {
            if (ball != existingBall) hasInitialSpawn = false;
            ball = existingBall;
            lever = existingLever;
            stageRoots = stages;
            spawnPoint = spawn;
            selectedStage = Mathf.Clamp(selectedStage, FirstStage, LastStage);
            CacheInitialSpawn();
            SetActiveStage();
        }

                                                                            
        public void ConfigureScene(ElectricBall2D existingBall, StageLever2D existingLever,
            GameObject stageRoot, Transform spawn, int stageNumber)
        {
            if (stageNumber < FirstStage || stageNumber > LastStage || stageRoot == null)
                return;
            selectedStage = stageNumber;
            var stages = new GameObject[LastStage - FirstStage + 1];
            stages[stageNumber - FirstStage] = stageRoot;
            Configure(existingBall, existingLever, stages, spawn);
            EnsureLearningComponents();
        }

        private void Awake()
        {
            CacheInitialSpawn();
            SetActiveStage();
            EnsureLearningComponents();
        }

                                                               
        public void EnsureLearningComponents()
        {
            GetSession().Configure(this);
            if (!Application.isPlaying) return;
            var guidance = GetComponent<StageThreeGuidanceView>();
            if (guidance == null && selectedStage == 3 && ball != null && GetStageRoot(3) != null)
                guidance = gameObject.AddComponent<StageThreeGuidanceView>();
            if (guidance != null)
                guidance.Configure(this, GetComponent<ElectricLearningController2D>());
            var fieldGuidance = GetComponent<StageFourGuidanceView>();
            if (fieldGuidance == null && selectedStage == 4 && ball != null && GetStageRoot(4) != null)
                fieldGuidance = gameObject.AddComponent<StageFourGuidanceView>();
            if (fieldGuidance != null)
                fieldGuidance.Configure(this, GetComponent<ElectricLearningController2D>());
            var voltageGuidance = GetComponent<StageFiveGuidanceView>();
            if (voltageGuidance == null && selectedStage == 5 && ball != null && GetStageRoot(5) != null)
                voltageGuidance = gameObject.AddComponent<StageFiveGuidanceView>();
            if (voltageGuidance != null)
                voltageGuidance.Configure(this, GetComponent<ElectricLearningController2D>());
        }

                                                                               
        public void RefreshModules()
        {
            CacheForceProvider();
            EnsureLearningComponents();
        }

        private ElectricStageSession2D GetSession()
        {
            if (session == null) session = GetComponent<ElectricStageSession2D>();
            if (session == null) session = gameObject.AddComponent<ElectricStageSession2D>();
            return session;
        }

        public void SelectStage(int stageNumber)
        {
                                                         
            if (stageNumber < FirstStage || stageNumber > LastStage || GetStageRoot(stageNumber) == null) return;

            selectedStage = stageNumber;
            SetActiveStage();
            RestartStage();
        }

                                                                             
        public void RestartStage()
        {
            GetSession().RestartStage();
        }

                                                                  
        public bool RetryCheckpoint() => GetSession().RetryCheckpoint();

                                                                            
        public bool TryGetSpawnPosition(out Vector2 position)
        {
            if (spawnPoint != null)
            {
                position = spawnPoint.position;
                return true;
            }
            position = initialSpawnPosition;
            return hasInitialSpawn;
        }

        private void CacheInitialSpawn()
        {
            if (hasInitialSpawn || ball == null) return;
            initialSpawnPosition = ball.transform.position;
            hasInitialSpawn = true;
        }

                                                                          
        public T GetStageComponent<T>(int stageNumber) where T : Component
        {
            GameObject root = GetStageRoot(stageNumber);
            return root != null ? root.GetComponentInChildren<T>(true) : null;
        }

                                                                      
        public bool TryGetCurrentForceSample(out ElectricForceSample sample)
        {
            sample = default;
            return activeForceComponent != null && activeForceComponent.isActiveAndEnabled
                && activeForceProvider.TryGetSample(out sample);
        }

        private GameObject GetStageRoot(int stageNumber)
        {
            int index = StageRootIndex(stageNumber);
            return stageRoots != null && index >= 0 && index < stageRoots.Length ? stageRoots[index] : null;
        }

        private int StageRootIndex(int stageNumber)
        {
                                                                                        
                                                                                           
            return stageNumber - (stageRoots != null && stageRoots.Length == 4 ? 2 : FirstStage);
        }

        private void SetActiveStage()
        {
            if (stageRoots != null)
            {
                for (int i = 0; i < stageRoots.Length; i++)
                    if (stageRoots[i] != null) stageRoots[i].SetActive(i == StageRootIndex(selectedStage));
            }
            CacheForceProvider();
        }

                                                          
        private void CacheForceProvider()
        {
            activeForceProvider = null;
            activeForceComponent = null;
            GameObject root = GetStageRoot(selectedStage);
            if (root == null) return;

            foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (!(component is IElectricForceProvider provider)) continue;
                activeForceProvider = provider;
                activeForceComponent = component;
                break;
            }
        }
    }
}
