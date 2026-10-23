// Temporary authoring utility: run only in the isolated Unity validation project.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using sung_wook;
using Object = UnityEngine.Object;

public static class MainMenuSceneBuilder
{
    private const string Root = "Assets/@Project/@sung_wook/@Game/";
    private const string Menu = Root + "00_Scenes/MainMenu.unity";
    private const string Fade = Root + "02_Prefabs/UI/SceneTransition.prefab";
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
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        GameObject transition = CreateTransition();
        GameObject fadePrefab = PrefabUtility.SaveAsPrefabAsset(transition, Fade);
        if (fadePrefab == null) throw new InvalidOperationException("Could not save transition prefab.");
        Object.DestroyImmediate(transition);

        var camera = new GameObject("Main Camera", typeof(Camera));
        camera.tag = "MainCamera";
        camera.transform.position = new Vector3(0f, 0f, -10f);
        camera.GetComponent<Camera>().orthographic = true;
        GameObject canvasObject = NewCanvas("Main Menu", 0);
        Image backdrop = NewRect("Background", canvasObject.transform).gameObject.AddComponent<Image>();
        backdrop.sprite = resources.background;
        backdrop.type = Image.Type.Sliced;
        backdrop.raycastTarget = false;
        Stretch((RectTransform)backdrop.transform);
        RectTransform main = NewRect("Main Content", canvasObject.transform);
        Stretch(main);
        Text title = Text("Title", "ELECTRIC BOUNCE", main, 72);
        Place((RectTransform)title.transform, new Vector2(0f, 245f), new Vector2(1050f, 145f));
        Button start = Button("Start", "시작", main, new Vector2(0f, 70f));
        Button settings = Button("Settings", "설정", main, new Vector2(0f, -38f));
        Button quit = Button("Quit", "나가기", main, new Vector2(0f, -146f));

        RectTransform settingsWindow = NewRect("Settings Window", canvasObject.transform);
        Stretch(settingsWindow);
        Image settingsBackground = settingsWindow.gameObject.AddComponent<Image>();
        settingsBackground.sprite = resources.background;
        settingsBackground.type = Image.Type.Sliced;
        Text settingsTitle = Text("Title", "설정", settingsWindow, 64);
        Place((RectTransform)settingsTitle.transform, new Vector2(0f, 250f), new Vector2(700f, 110f));
        RectTransform content = NewRect("Content", settingsWindow);
        Place(content, Vector2.zero, new Vector2(900f, 360f));
        Button close = Button("Close Settings", "닫기", settingsWindow, new Vector2(0f, -250f));
        settingsWindow.gameObject.SetActive(false);

        MainMenuController controller = canvasObject.AddComponent<MainMenuController>();
        var menuSettings = new SerializedObject(controller);
        menuSettings.FindProperty("mainContent").objectReferenceValue = main.gameObject;
        menuSettings.FindProperty("settingsWindow").objectReferenceValue = settingsWindow.gameObject;
        menuSettings.FindProperty("startButton").objectReferenceValue = start;
        menuSettings.FindProperty("settingsButton").objectReferenceValue = settings;
        menuSettings.FindProperty("quitButton").objectReferenceValue = quit;
        menuSettings.FindProperty("closeSettingsButton").objectReferenceValue = close;
        menuSettings.ApplyModifiedPropertiesWithoutUndo();

        var events = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        events.GetComponent<EventSystem>().firstSelectedGameObject = start.gameObject;
        PrefabUtility.InstantiatePrefab(fadePrefab, scene);
        if (!EditorSceneManager.SaveScene(scene, Menu)) throw new InvalidOperationException("Menu save failed.");

        for (int stage = 1; stage <= 5; stage++)
        {
            string path = Root + "00_Scenes/Stage/Stage_" + stage.ToString("00") + ".unity";
            Scene stageScene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            bool hasTransition = false;
            foreach (GameObject root in stageScene.GetRootGameObjects())
                hasTransition |= root.GetComponentInChildren<SceneTransitionController>(true) != null;
            if (!hasTransition) PrefabUtility.InstantiatePrefab(fadePrefab, stageScene);
            if (!EditorSceneManager.SaveScene(stageScene)) throw new InvalidOperationException("Stage save failed: " + path);
        }
        var scenes = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(Menu, true) };
        foreach (EditorBuildSettingsScene entry in EditorBuildSettings.scenes)
            if (entry.path != Menu) scenes.Add(entry);
        EditorBuildSettings.scenes = scenes.ToArray();
        AssetDatabase.SaveAssets();
        Debug.Log("Created default-color menu, blank settings content, and authored transition instances. Build first: " + Menu);
    }

    private static GameObject CreateTransition()
    {
        GameObject root = NewCanvas("Scene Transition", 32000);
        CanvasGroup group = root.AddComponent<CanvasGroup>();
        group.alpha = 1f;
        group.interactable = false;
        group.blocksRaycasts = true;
        RectTransform cover = NewRect("Fade Overlay", root.transform);
        Stretch(cover);
        Image image = cover.gameObject.AddComponent<Image>();
        image.color = Color.black; // Functional fade cover; menu sprites retain their default colors.
        image.raycastTarget = true;
        SceneTransitionController transition = root.AddComponent<SceneTransitionController>();
        var settings = new SerializedObject(transition);
        settings.FindProperty("fadeOverlay").objectReferenceValue = group;
        settings.FindProperty("fadeDuration").floatValue = 0.35f;
        settings.ApplyModifiedPropertiesWithoutUndo();
        return root;
    }

    private static GameObject NewCanvas(string name, int order)
    {
        var root = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.layer = 5;
        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = order;
        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1600f, 900f);
        scaler.matchWidthOrHeight = 0.5f;
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

    private static Text Text(string name, string label, Transform parent, int size)
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

    private static Button Button(string name, string label, Transform parent, Vector2 position)
    {
        GameObject root = DefaultControls.CreateButton(resources);
        root.name = name;
        root.transform.SetParent(parent, false);
        Place((RectTransform)root.transform, position, new Vector2(360f, 80f));
        Button button = root.GetComponent<Button>();
        button.transition = Selectable.Transition.None;
        Text text = root.GetComponentInChildren<Text>();
        text.font = font;
        text.fontSize = 42;
        text.text = label;
        return button;
    }
}
