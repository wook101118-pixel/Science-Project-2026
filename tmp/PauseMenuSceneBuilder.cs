// Temporary authoring utility: run only in the isolated Unity validation project.
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using sung_wook;
using Object = UnityEngine.Object;

public static class PauseMenuSceneBuilder
{
    private const string Root = "Assets/@Project/@sung_wook/@Game/";
    private const string PausePrefab = Root + "02_Prefabs/UI/PauseMenu.prefab";
    private static Font font;
    private static DefaultControls.Resources resources;

    public static void Build()
    {
        if (!Application.isBatchMode || !Application.dataPath.Replace('\\', '/').Contains("/Temp/CodexUnityVisualBuild/"))
            throw new InvalidOperationException("Author only in the isolated validation project.");

        resources = new DefaultControls.Resources
        {
            standard = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"),
            background = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd")
        };
        font = AssetDatabase.LoadAssetAtPath<Font>(AssetDatabase.GUIDToAssetPath("41554645f27a73046866b978f0374dd4"));
        if (font == null || resources.standard == null || resources.background == null)
            throw new InvalidOperationException("Existing default UI resources or Korean font missing.");

        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        GameObject pauseMenu = CreatePauseMenu();
        GameObject pausePrefab = PrefabUtility.SaveAsPrefabAsset(pauseMenu, PausePrefab);
        if (pausePrefab == null) throw new InvalidOperationException("Could not save pause menu prefab.");
        Object.DestroyImmediate(pauseMenu);

        for (int stage = 1; stage <= 5; stage++)
        {
            string path = Root + "00_Scenes/Stage/Stage_" + stage.ToString("00") + ".unity";
            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            bool hasPauseMenu = false;
            foreach (GameObject root in scene.GetRootGameObjects())
                hasPauseMenu |= root.GetComponentInChildren<PauseMenuController>(true) != null;
            if (!hasPauseMenu) PrefabUtility.InstantiatePrefab(pausePrefab, scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Stage save failed: " + path);
        }

        AssetDatabase.SaveAssets();
        Debug.Log("Authored default-color pause menu prefab and instances in Stage_01 through Stage_05.");
    }

    private static GameObject CreatePauseMenu()
    {
        GameObject root = new GameObject("Pause Menu", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.layer = 5;
        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;
        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1600f, 900f);
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform menu = NewRect("Menu Root", root.transform);
        Stretch(menu);
        Image backdrop = menu.gameObject.AddComponent<Image>();
        backdrop.sprite = resources.background;
        backdrop.type = Image.Type.Sliced;
        backdrop.raycastTarget = true;

        Text title = CreateText("Title", "일시정지", menu, 72);
        Place((RectTransform)title.transform, new Vector2(0f, 245f), new Vector2(1050f, 145f));
        Button resume = CreateButton("Resume", "계속하기", menu, new Vector2(0f, 70f));
        Button restart = CreateButton("Restart", "다시 시작", menu, new Vector2(0f, -38f));
        Button mainMenu = CreateButton("Main Menu", "메인 메뉴로 돌아가기", menu, new Vector2(0f, -146f));
        menu.gameObject.SetActive(false);

        PauseMenuController controller = root.AddComponent<PauseMenuController>();
        var settings = new SerializedObject(controller);
        settings.FindProperty("menuRoot").objectReferenceValue = menu.gameObject;
        settings.FindProperty("resumeButton").objectReferenceValue = resume;
        settings.FindProperty("restartButton").objectReferenceValue = restart;
        settings.FindProperty("mainMenuButton").objectReferenceValue = mainMenu;
        settings.ApplyModifiedPropertiesWithoutUndo();
        return root;
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        var root = new GameObject(name, typeof(RectTransform));
        root.layer = 5;
        root.transform.SetParent(parent, false);
        return (RectTransform)root.transform;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void Place(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static Text CreateText(string name, string label, Transform parent, int size)
    {
        GameObject root = DefaultControls.CreateText(resources);
        root.name = name;
        root.transform.SetParent(parent, false);
        Text text = root.GetComponent<Text>();
        text.font = font;
        text.text = label;
        text.fontSize = size;
        text.alignment = TextAnchor.MiddleCenter;
        text.raycastTarget = false;
        return text;
    }

    private static Button CreateButton(string name, string label, Transform parent, Vector2 position)
    {
        GameObject root = DefaultControls.CreateButton(resources);
        root.name = name;
        root.transform.SetParent(parent, false);
        Place((RectTransform)root.transform, position, new Vector2(560f, 80f));
        Button button = root.GetComponent<Button>();
        button.transition = Selectable.Transition.None;
        Text text = root.GetComponentInChildren<Text>();
        text.font = font;
        text.fontSize = 42;
        text.text = label;
        return button;
    }
}
