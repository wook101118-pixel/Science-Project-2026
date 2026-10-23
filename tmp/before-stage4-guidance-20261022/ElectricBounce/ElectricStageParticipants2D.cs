using UnityEngine;
using UnityEngine.SceneManagement;

namespace sung_wook
{
    /// <summary>
    /// 프리팹에 저장할 수 없는 공·레버 참조만 같은 씬의 참가자로 보충한다.
    /// 명시한 Inspector 연결은 보존하며, 여러 후보가 있으면 임의로 선택하지 않는다.
    /// </summary>
    internal static class ElectricStageParticipants2D
    {
        public static void ResolveMissing(Component owner, ref ElectricBall2D ball, ref StageLever2D lever)
        {
            if (ball != null && lever != null) return;

            Scene scene = owner.gameObject.scene;
            if (!scene.IsValid() || !scene.isLoaded) return;

            ElectricStageSceneBootstrap bootstrap = FindUnique<ElectricStageSceneBootstrap>(scene);
            if (ball == null)
                ball = bootstrap != null && bootstrap.Ball != null
                    ? bootstrap.Ball : FindUnique<ElectricBall2D>(scene);
            if (lever == null)
                lever = bootstrap != null && bootstrap.Lever != null
                    ? bootstrap.Lever : FindUnique<StageLever2D>(scene);

            // 런타임에 추가한 장치도 기존 관측/학습 이벤트에 연결한다. 실험은 재시작하지 않는다.
            ElectricStageSelector selector = bootstrap != null
                ? bootstrap.GetComponent<ElectricStageSelector>() : FindUnique<ElectricStageSelector>(scene);
            if (ball != null && lever != null && selector != null && selector.CurrentStageRoot != null
                && owner.transform.IsChildOf(selector.CurrentStageRoot.transform))
                selector.RefreshModules();
        }

        private static T FindUnique<T>(Scene scene) where T : Component
        {
            T found = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (T candidate in root.GetComponentsInChildren<T>(true))
                {
                    if (found != null) return null;
                    found = candidate;
                }
            }
            return found;
        }
    }
}
