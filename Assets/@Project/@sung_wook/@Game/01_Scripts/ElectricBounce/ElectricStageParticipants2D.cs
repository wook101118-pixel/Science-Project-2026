using UnityEngine;
using UnityEngine.SceneManagement;

namespace sung_wook
{
                 
                                               
                                                        
                  
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
