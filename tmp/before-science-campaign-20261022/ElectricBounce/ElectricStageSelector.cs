using UnityEngine;

namespace sung_wook
{
    /// <summary>
    /// 현재 스테이지의 참조와 관측 공급자를 관리한다.
    /// 씬 연결은 ElectricStageSceneBootstrap, 초기화는 ElectricStageSession2D,
    /// 키보드 명령은 ElectricStageInput2D가 담당한다. 기존 직렬화 연결과 공개 진입점은 유지한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ElectricStageSelector : MonoBehaviour
    {
        private const int FirstStage = 1;
        private const int LastStage = 5;

        // 기존 씬과 프리팹 연결을 유지하기 위해 직렬화 필드 이름을 보존한다.
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

        /// <summary>설치기가 기존 공·레버·스테이지를 연결한다. 공 위치는 변경하지 않는다.</summary>
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

        /// <summary>분리 씬에 존재하는 스테이지만 등록한다. 다른 씬이나 플레이어를 생성하지 않는다.</summary>
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

        /// <summary>기존 호출부를 보존하며 실제 연결 책임은 세션에 위임한다.</summary>
        public void EnsureLearningComponents()
        {
            GetSession().Configure(this);
        }

        /// <summary>추가/교체한 프리팹의 관측·학습 연결만 갱신하며 현재 실험 기록과 공 위치는 유지한다.</summary>
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
            // 분리 씬에서 존재하지 않는 맵 번호를 선택하면 현재 실험을 그대로 유지한다.
            if (stageNumber < FirstStage || stageNumber > LastStage || GetStageRoot(stageNumber) == null) return;

            selectedStage = stageNumber;
            SetActiveStage();
            RestartStage();
        }

        /// <summary>Inspector/기존 호출부의 재시작 연결을 보존한다. 초기화는 세션이 수행한다.</summary>
        public void RestartStage()
        {
            GetSession().RestartStage();
        }

        /// <summary>현재 학습·별을 유지한 채 마지막 실제 착지 발판으로 돌아간다.</summary>
        public bool RetryCheckpoint() => GetSession().RetryCheckpoint();

        /// <summary>별도 Spawn 오브젝트가 없는 분리 씬은 시작 때의 플레이어 위치를 재사용한다.</summary>
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

        /// <summary>비활성 스테이지도 조회하여 준비/재시작 시 별도 검색 로직을 반복하지 않는다.</summary>
        public T GetStageComponent<T>(int stageNumber) where T : Component
        {
            GameObject root = GetStageRoot(stageNumber);
            return root != null ? root.GetComponentInChildren<T>(true) : null;
        }

        /// <summary>물리 공급자의 관측값을 공유한다. 이 호출은 공에 힘을 가하지 않는다.</summary>
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
            // Older combined experiment scenes serialize four entries for Stage 02..05.
            // New split-scene bindings use five entries for the tutorial and Stage 02..05.
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

        // 공급자는 스테이지 전환 때만 검색한다. 관측값/화살표 조회는 기존 참조를 재사용한다.
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
