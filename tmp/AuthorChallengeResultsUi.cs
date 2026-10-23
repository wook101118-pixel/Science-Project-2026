// Temporary Editor-only authoring. Run only in the isolated clone; copy only the two reported saved assets back.
using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using sung_wook;
using Object = UnityEngine.Object;

public static class AuthorChallengeResultsUi
{
    private const string Root = "Assets/@Project/@sung_wook/@Game/";
    public static void Build()
    {
        if (!Application.isBatchMode || !Application.dataPath.Replace('\\', '/').Contains("CodexUnityVisualBuild/Assets"))
            throw new InvalidOperationException("Use only the isolated clone in batch mode.");
        BuildResults();
        BuildMenu();
        AssetDatabase.SaveAssets();
        Debug.Log("Challenge results UI authored: GameCompleteScreen.prefab and MainMenu.unity only.");
    }

    private static void SetRef(SerializedObject data, string name, Object value) => data.FindProperty(name).objectReferenceValue = value;
    private static T Ref<T>(SerializedObject data, string name) where T : Object => data.FindProperty(name).objectReferenceValue as T;
    private static Transform Named(Transform root, string name) => root.GetComponentsInChildren<Transform>(true).First(t => t.name.Trim() == name);
    private static void Rect(RectTransform rect, Vector2 min, Vector2 max, Vector2 position, Vector2 size)
    {
        rect.anchorMin = min; rect.anchorMax = max; rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = position; rect.sizeDelta = size;
    }
    private static void Place(Button button, float x, float y, float width = 540f, float height = 76f)
        => Rect((RectTransform)button.transform, new Vector2(.5f,.5f), new Vector2(.5f,.5f), new Vector2(x,y), new Vector2(width,height));
    private static void Label(Button button, string text)
    {
        Text legacy = button.GetComponentInChildren<Text>(true);
        if (legacy != null) { legacy.text = text; legacy.fontSize = Mathf.Min(legacy.fontSize, 38); }
        TMP_Text tmp = button.GetComponentInChildren<TMP_Text>(true);
        if (tmp != null) { tmp.text = text; tmp.enableAutoSizing = true; tmp.fontSizeMin = 20f; tmp.fontSizeMax = 36f; }
    }

    private static void BuildResults()
    {
        string path = Root + "02_Prefabs/UI/GameCompleteScreen.prefab";
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var view = root.GetComponent<ElectricGameCompleteView>();
            var data = new SerializedObject(view);
            Button primary = Ref<Button>(data, "restartButton");
            if (primary == null) throw new InvalidOperationException("Missing authored primary result button.");
            Transform parent = primary.transform.parent;
            Button retry = Ref<Button>(data, "retryCurrentButton");
            if (retry == null) { retry = Object.Instantiate(primary, parent); retry.name = "Retry Current Stage Button"; }
            Button menu = Ref<Button>(data, "mainMenuButton");
            if (menu == null) { menu = Object.Instantiate(primary, parent); menu.name = "Return Main Menu Button"; }
            foreach (Button button in new[] { primary, retry, menu })
                button.onClick.RemoveAllListeners();
            Rect((RectTransform)primary.transform, new Vector2(.19f,.12f), new Vector2(.19f,.12f), Vector2.zero, new Vector2(290f,72f));
            Rect((RectTransform)retry.transform, new Vector2(.5f,.12f), new Vector2(.5f,.12f), Vector2.zero, new Vector2(290f,72f));
            Rect((RectTransform)menu.transform, new Vector2(.81f,.12f), new Vector2(.81f,.12f), Vector2.zero, new Vector2(290f,72f));
            Label(primary, "다음 단계"); Label(retry, "현재 스테이지 재도전"); Label(menu, "메인 메뉴");
            TMP_Text title = Named(root.transform, "Completion Title").GetComponent<TMP_Text>();
            TMP_Text summary = Named(root.transform, "Completion Summary").GetComponent<TMP_Text>();
            TMP_Text hint = Named(root.transform, "Restart Hint").GetComponent<TMP_Text>();
            Named(root.transform, "Completed Concepts").gameObject.SetActive(false);
            Named(root.transform, "Series Label").gameObject.SetActive(false);
            Rect(title.rectTransform, new Vector2(.06f,.80f), new Vector2(.94f,.94f), Vector2.zero, Vector2.zero);
            title.text = "스테이지 완료"; title.fontSize = 64f; title.enableAutoSizing = true; title.fontSizeMin = 36f; title.fontSizeMax = 64f;
            Rect(summary.rectTransform, new Vector2(.07f,.24f), new Vector2(.93f,.78f), Vector2.zero, Vector2.zero);
            summary.text = "이번 도전 결과와 개인 기록이 여기에 표시됩니다.";
            summary.alignment = TextAlignmentOptions.TopLeft; summary.fontSize = 32f;
            summary.enableAutoSizing = true; summary.fontSizeMin = 22f; summary.fontSizeMax = 32f;
            summary.textWrappingMode = TextWrappingModes.Normal;
            Rect(hint.rectTransform, new Vector2(.06f,.02f), new Vector2(.94f,.07f), Vector2.zero, Vector2.zero);
            hint.text = "기록 자동 저장 · Esc 메인 메뉴";
            hint.enableAutoSizing = true; hint.fontSizeMin = 18f; hint.fontSizeMax = 23f;
            SetRef(data, "retryCurrentButton", retry); SetRef(data, "mainMenuButton", menu);
            SetRef(data, "resultTitle", title); SetRef(data, "resultSummary", summary);
            SetRef(data, "primaryLabel", primary.GetComponentInChildren<TMP_Text>(true));
            SetRef(data, "resultHint", hint); SetRef(data, "resultFrame", Named(root.transform,"Completion Frame"));
            data.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static void BuildMenu()
    {
        string path = Root + "00_Scenes/MainMenu.unity";
        var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        MainMenuController menu = Object.FindFirstObjectByType<MainMenuController>();
        var data = new SerializedObject(menu);
        GameObject content = Ref<GameObject>(data, "mainContent");
        GameObject settings = Ref<GameObject>(data, "settingsWindow");
        Button start = Ref<Button>(data, "startButton"), settingsButton = Ref<Button>(data, "settingsButton"), quit = Ref<Button>(data, "quitButton");
        if (content == null || settings == null || start == null) throw new InvalidOperationException("Missing authored menu references.");
        Button practice = Ref<Button>(data, "practiceButton");
        if (practice == null) { practice = Object.Instantiate(start, start.transform.parent); practice.name = "Practice Button"; }
        Place(start,0,150); Place(practice,0,50); Place(settingsButton,0,-50); Place(quit,0,-150);
        Label(start,"처음부터 시작"); Label(practice,"스테이지 연습");

        GameObject window = Ref<GameObject>(data, "practiceWindow");
        if (window == null)
        {
            window = Object.Instantiate(settings, settings.transform.parent); window.name = "PracticeWindow";
            foreach (Transform child in window.transform.Cast<Transform>().ToArray()) Object.DestroyImmediate(child.gameObject);
        }
        foreach (MonoBehaviour component in window.GetComponents<MonoBehaviour>())
            if (!(component is Image)) Object.DestroyImmediate(component);
        Rect((RectTransform)window.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var stages = data.FindProperty("stageButtons"); stages.arraySize = 5;
        for (int index = 0; index < 5; index++)
        {
            Button button = stages.GetArrayElementAtIndex(index).objectReferenceValue as Button;
            if (button == null) { button = Object.Instantiate(start, window.transform); button.name = "Practice Stage " + (index+1); }
            Place(button,0,180 - index*90,680,72); Label(button,(index+1)+". 스테이지 연습");
            stages.GetArrayElementAtIndex(index).objectReferenceValue = button;
        }
        Button close = Ref<Button>(data,"closePracticeButton");
        if (close == null) { close = Object.Instantiate(Ref<Button>(data,"closeSettingsButton"),window.transform); close.name = "Close Practice Button"; }
        Place(close,0,-330,400,72); Label(close,"메인 메뉴");
        Text title = window.GetComponentsInChildren<Text>(true).FirstOrDefault(t => t.gameObject.name == "Practice Title");
        if (title == null)
        {
            Text source = settings.GetComponentsInChildren<Text>(true).OrderByDescending(t => t.fontSize).First();
            title = Object.Instantiate(source,window.transform); title.name = "Practice Title";
        }
        Rect(title.rectTransform,new Vector2(.5f,.5f),new Vector2(.5f,.5f),new Vector2(0,320),new Vector2(1000,100));
        title.text = "열린 스테이지 연습"; title.fontSize = 54; title.alignment = TextAnchor.MiddleCenter;
        SetRef(data,"practiceButton",practice); SetRef(data,"practiceWindow",window); SetRef(data,"closePracticeButton",close);
        data.FindProperty("firstStagePath").stringValue = ElectricGameCompleteView.FirstStagePath;
        data.ApplyModifiedPropertiesWithoutUndo();
        window.SetActive(false); settings.SetActive(false); content.SetActive(true);
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene,path);
    }
}
