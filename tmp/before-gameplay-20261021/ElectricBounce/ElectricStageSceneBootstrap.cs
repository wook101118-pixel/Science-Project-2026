using UnityEngine;

namespace sung_wook
{
    /// <summary>
    /// 분리한 Stage 씬의 기존 공·레버·실험 장치를 연결하는 시작 지점이다.
    /// 디자인이나 오브젝트를 생성하지 않고 Inspector 참조를 우선 사용한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ElectricStageSceneBootstrap : MonoBehaviour
    {
        [SerializeField, Range(2, 5)]
        private int stageNumber = 2;
        [SerializeField]
        private ElectricBall2D ball;
        [SerializeField]
        private StageLever2D lever;
        [SerializeField]
        private GameObject stageRoot;
        [SerializeField]
        private Transform spawnPoint;

        public int StageNumber => stageNumber;
        public ElectricBall2D Ball => ball;
        public StageLever2D Lever => lever;

        private void Reset()
        {
            // 에디터에서 컴포넌트를 붙일 때 같은 씬의 참조만 기본값으로 채운다.
            string sceneName = gameObject.scene.name;
            if (sceneName.StartsWith("Stage_") && int.TryParse(sceneName.Substring(6), out int number)
                && number >= 2 && number <= 5)
                stageNumber = number;
            ResolveReferences();
        }

        private void Awake()
        {
            ResolveReferences();
            if (ball == null || lever == null || stageRoot == null)
            {
                Debug.LogError("스테이지 연결이 비었습니다. Scene Bootstrap의 Ball, Lever, Stage Root를 확인하세요.", this);
                return;
            }

            // 장치가 저장한 이동점·전압·게이지·이벤트는 유지하고 참가자 연결만 보충한다.
            foreach (var charger in stageRoot.GetComponentsInChildren<ContactCharger2D>(true))
                charger.ConnectParticipants(ball, lever);
            foreach (var launcher in stageRoot.GetComponentsInChildren<DistanceLauncher2D>(true))
                launcher.ConnectParticipants(ball, lever);
            foreach (var field in stageRoot.GetComponentsInChildren<UniformField2D>(true))
                field.ConnectParticipants(ball, lever);
            foreach (var launcher in stageRoot.GetComponentsInChildren<VoltageLauncher2D>(true))
                launcher.ConnectParticipants(ball, lever);

            var selector = GetComponent<ElectricStageSelector>();
            if (selector == null) selector = gameObject.AddComponent<ElectricStageSelector>();
            selector.ConfigureScene(ball, lever, stageRoot, spawnPoint, stageNumber);
        }

        private void ResolveReferences()
        {
            if (ball == null) ball = FindSceneComponent<ElectricBall2D>();
            if (lever == null) lever = FindSceneComponent<StageLever2D>();
            if (stageRoot != null) return;
            string rootName = "Stage " + stageNumber.ToString("00");
            foreach (GameObject root in gameObject.scene.GetRootGameObjects())
            {
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                {
                    if (child.name != rootName) continue;
                    stageRoot = child.gameObject;
                    return;
                }
            }
        }

        /// <summary>추가 로드한 다른 씬의 공·레버를 잘못 가져오지 않도록 현재 씬 안에서만 찾는다.</summary>
        private T FindSceneComponent<T>() where T : Component
        {
            T found = null;
            foreach (GameObject root in gameObject.scene.GetRootGameObjects())
            {
                foreach (T candidate in root.GetComponentsInChildren<T>(true))
                {
                    if (found != null)
                    {
                        Debug.LogError("같은 씬에 " + typeof(T).Name + "이 둘 이상 있습니다. Inspector에서 대상을 연결하세요.", this);
                        return null;
                    }
                    found = candidate;
                }
            }
            return found;
        }
    }
}
