using UnityEditor;
using UnityEngine;

namespace sung_wook
{
    public static class ElectricBatchPlayMode
    {
        public static void EnterAfterEditorStartup()
        {
            if (!Application.isBatchMode) return;
            EditorApplication.delayCall -= WaitForStartupCallbacks;
            EditorApplication.delayCall += WaitForStartupCallbacks;
        }

        private static void WaitForStartupCallbacks()
        {
            EditorApplication.delayCall -= EnterWhenIdle;
            EditorApplication.delayCall += EnterWhenIdle;
        }

        private static void EnterWhenIdle()
        {
            if (!Application.isBatchMode      || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += EnterWhenIdle;
                return;
            }
            EditorApplication.EnterPlaymode();
        }
    }
}
