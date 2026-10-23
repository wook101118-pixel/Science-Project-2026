using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace sung_wook
{
    /// <summary>기존 스테이지의 지형이나 입력을 바꾸지 않고 학습 컨트롤러·화살표 참조를 연결한다.</summary>
    public static class ElectricLearningSceneInstaller
    {
        public const string PrototypePath = "Assets/@Project/@sung_wook/@Game/Scenes/sungwook_ElectricBounce.unity";
        private const string UndoLabel = "Connect Electric Bounce learning";

        [MenuItem("Tools/sung_wook/Electric Bounce/Connect Learning To Current Scene")]
        public static void ConnectCurrentScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            Scene scene = SceneManager.GetActiveScene();
            bool wasDirty = scene.isDirty;
            int count = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (var selector in root.GetComponentsInChildren<ElectricStageSelector>(true))
                {
                    Connect(selector);
                    count++;
                }
            if (count == 0)
                throw new InvalidOperationException("The current scene has no installed ElectricStageSelector.");
            EditorSceneManager.MarkSceneDirty(scene);
            if (!wasDirty && !string.IsNullOrEmpty(scene.path)) EditorSceneManager.SaveScene(scene);
        }

        /// <summary>반복 실행해도 컴포넌트를 중복 생성하지 않고 기존 에셋과 출구 조건을 연결한다.</summary>
        public static void Connect(ElectricStageSelector selector)
        {
            var lesson = selector.GetComponent<ElectricLearningController2D>();
            if (lesson == null) lesson = Undo.AddComponent<ElectricLearningController2D>(selector.gameObject);
            var vectors = selector.GetComponent<ElectricVectorDisplay2D>();
            if (vectors == null) vectors = Undo.AddComponent<ElectricVectorDisplay2D>(selector.gameObject);
            Undo.RecordObject(lesson, UndoLabel);
            Undo.RecordObject(vectors, UndoLabel);
            foreach (var goal in selector.GetComponentsInChildren<ElectricGoal2D>(true))
                Undo.RecordObject(goal, UndoLabel);
            lesson.Configure(selector);
            Sprite square = null;
            string path = AssetDatabase.GUIDToAssetPath("311925a002f4447b3a28927169b83ea6");
            if (!string.IsNullOrEmpty(path))
                foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (asset is Sprite sprite) { square = sprite; break; }
            Material material = AssetDatabase.LoadAssetAtPath<Material>(
                AssetDatabase.GUIDToAssetPath("a97c105638bdf8b4a8650670310a4cd3"));
            vectors.Configure(selector, square, material);
            EditorUtility.SetDirty(lesson);
            EditorUtility.SetDirty(vectors);
            foreach (var goal in selector.GetComponentsInChildren<ElectricGoal2D>(true))
                EditorUtility.SetDirty(goal);
        }

        public static void InstallPrototypeForBatch()
        {
            if (!Application.isBatchMode) return;
            EditorSceneManager.OpenScene(PrototypePath, OpenSceneMode.Single);
            ConnectCurrentScene();
            if (!EditorSceneManager.SaveScene(SceneManager.GetActiveScene()))
                throw new InvalidOperationException("Could not save the learning prototype scene.");
            Debug.Log("Electric Bounce learning connected to the existing prototype scene.");
        }
    }
}
