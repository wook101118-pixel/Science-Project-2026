// Temporary authoring utility: copy only to the isolated Unity validation project.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using sung_wook;
using Object = UnityEngine.Object;

public static class PauseSettingsSceneBuilder
{
    private const string Root = "Assets/@Project/@sung_wook/@Game/";
    private const string PausePath = Root + "02_Prefabs/UI/PauseMenu.prefab";
    private const string MenuPath = Root + "00_Scenes/MainMenu.unity";
    private const string ReportPath = "C:/Users/wook1/GitBub-Unity/Science-Project-2026/Logs/PauseSettingsAuthoringAudit.json";
    private static DefaultControls.Resources resources;
    private static Font font;

    [Serializable] private sealed class ObjectIdentity
    {
        public string key;
        public long fileId;
    }

    [Serializable] private sealed class Report
    {
        public string status = "running";
        public string prefab = PausePath;
        public string copiedSettingsSource = MenuPath + "/Main Menu Canvas/Settings Window/Content";
        public bool originalFileIdsPreserved;
        public bool allLayersAreUi;
        public bool referencesInternal;
        public bool defaultColorsPreserved;
        public bool settingsInitiallyHidden;
        public List<ObjectIdentity> originalObjects = new List<ObjectIdentity>();
        public List<string> errors = new List<string>();
    }

    public static void Build()
    {
        var report = new Report();
        GameObject prefabContents = null;
        Scene sourceScene = default;
        bool ownsSourceScene = false;
        try
        {
            if (!Application.isBatchMode || Application.dataPath.Replace('\\', '/').IndexOf("/Temp/CodexUnityVisualBuild/", StringComparison.OrdinalIgnoreCase) < 0)
                throw new InvalidOperationException("Author only in the isolated CodexUnityVisualBuild project.");
            resources = new DefaultControls.Resources
            {
                standard = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"),
                background = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd")
            };
            font = AssetDatabase.LoadAssetAtPath<Font>(AssetDatabase.GUIDToAssetPath("41554645f27a73046866b978f0374dd4"));
            if (font == null || resources.standard == null || resources.background == null)
                throw new InvalidOperationException("Existing default sprites and Korean font must be available.");
            GameObject original = AssetDatabase.LoadAssetAtPath<GameObject>(PausePath);
            if (original == null) throw new InvalidOperationException("Existing pause prefab is missing.");
            report.originalObjects = ReadIdentities(original);

            // The saved main-menu controls already have the correct local SfxSettingsView references.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            sourceScene = SceneManager.GetSceneByPath(MenuPath);
            if (!sourceScene.IsValid() || !sourceScene.isLoaded)
            {
                sourceScene = EditorSceneManager.OpenScene(MenuPath, OpenSceneMode.Additive);
                ownsSourceScene = true;
            }
            GameObject sourceContent = null;
            foreach (GameObject sceneRoot in sourceScene.GetRootGameObjects())
                foreach (SfxSettingsView view in sceneRoot.GetComponentsInChildren<SfxSettingsView>(true))
                {
                    if (sourceContent != null) throw new InvalidOperationException("Main menu contains multiple SFX settings bodies.");
                    sourceContent = view.gameObject;
                }
            if (sourceContent == null || sourceContent.name != "Content")
                throw new InvalidOperationException("The main-menu Content/SfxSettingsView body was not found.");

            prefabContents = PrefabUtility.LoadPrefabContents(PausePath);
            PauseMenuController controller = prefabContents.GetComponent<PauseMenuController>();
            var serialized = new SerializedObject(controller);
            GameObject menuRoot = serialized.FindProperty("menuRoot").objectReferenceValue as GameObject;
            Button resume = serialized.FindProperty("resumeButton").objectReferenceValue as Button;
            Button restart = serialized.FindProperty("restartButton").objectReferenceValue as Button;
            Button mainMenu = serialized.FindProperty("mainMenuButton").objectReferenceValue as Button;
            if (menuRoot == null || resume == null || restart == null || mainMenu == null)
                throw new InvalidOperationException("Existing pause references must be retained.");
            if (prefabContents.transform.Find("Settings Window") != null || menuRoot.transform.Find("Settings") != null)
                throw new InvalidOperationException("Pause settings are already authored; refusing to duplicate controls.");

            MoveExisting(resume, 110f);
            MoveExisting(restart, 2f);
            MoveExisting(mainMenu, -214f);
            Button settings = CreateButton("Settings", "설정", menuRoot.transform, -106f, 560f);
            settings.transform.SetSiblingIndex(mainMenu.transform.GetSiblingIndex());

            RectTransform window = NewRect("Settings Window", prefabContents.transform);
            window.gameObject.SetActive(false);
            Stretch(window);
            Image background = window.gameObject.AddComponent<Image>();
            background.sprite = resources.background;
            background.type = Image.Type.Sliced;
            background.color = Color.white;
            Text title = CreateText("Title", "설정", window, 64);
            Place((RectTransform)title.transform, new Vector2(0f, 250f), new Vector2(1050f, 145f));
            GameObject content = Object.Instantiate(sourceContent, window, false);
            content.name = "Content";
            Place((RectTransform)content.transform, Vector2.zero, new Vector2(900f, 360f));
            content.SetActive(true);
            Button close = CreateButton("Close Settings", "뒤로", window, -250f, 360f);

            serialized.FindProperty("settingsButton").objectReferenceValue = settings;
            serialized.FindProperty("closeSettingsButton").objectReferenceValue = close;
            serialized.FindProperty("settingsWindow").objectReferenceValue = window.gameObject;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            SetUiLayer(prefabContents.transform);
            CheckSettingsReferences(content.GetComponent<SfxSettingsView>(), content.transform);
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(prefabContents, PausePath);
            if (saved == null) throw new InvalidOperationException("Pause prefab save failed.");
            AssetDatabase.SaveAssets();

            GameObject savedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PausePath);
            List<ObjectIdentity> after = ReadIdentities(savedPrefab);
            foreach (ObjectIdentity before in report.originalObjects)
                if (!after.Exists(item => item.key == before.key && item.fileId == before.fileId))
                    throw new InvalidOperationException("An existing saved object identity changed: " + before.key + " / " + before.fileId);
            report.originalFileIdsPreserved = true;
            report.allLayersAreUi = true;
            foreach (Transform child in savedPrefab.GetComponentsInChildren<Transform>(true))
                report.allLayersAreUi &= child.gameObject.layer == 5;
            if (!report.allLayersAreUi) throw new InvalidOperationException("All pause UI objects must use the UI layer.");

            var savedController = new SerializedObject(savedPrefab.GetComponent<PauseMenuController>());
            var savedSettings = savedController.FindProperty("settingsButton").objectReferenceValue as Button;
            var savedClose = savedController.FindProperty("closeSettingsButton").objectReferenceValue as Button;
            var savedWindow = savedController.FindProperty("settingsWindow").objectReferenceValue as GameObject;
            if (savedSettings == null || savedClose == null || savedWindow == null)
                throw new InvalidOperationException("The saved controller settings references are incomplete.");
            foreach (string field in new[] { "menuRoot", "resumeButton", "restartButton", "mainMenuButton", "settingsButton", "closeSettingsButton", "settingsWindow" })
            {
                Object reference = savedController.FindProperty(field).objectReferenceValue;
                Transform transform = reference is GameObject go ? go.transform : (reference as Component)?.transform;
                if (transform == null || !transform.IsChildOf(savedPrefab.transform))
                    throw new InvalidOperationException("Controller field points outside its saved prefab: " + field);
            }
            CheckSettingsReferences(savedWindow.GetComponentInChildren<SfxSettingsView>(true), savedWindow.transform);
            report.referencesInternal = true;
            report.settingsInitiallyHidden = !savedWindow.activeSelf;
            report.defaultColorsPreserved = CheckDefaultButton(savedSettings) && CheckDefaultButton(savedClose)
                && savedWindow.GetComponent<Image>().color == Color.white
                && savedWindow.GetComponent<Image>().sprite == resources.background;
            if (!report.settingsInitiallyHidden || !report.defaultColorsPreserved)
                throw new InvalidOperationException("The authored settings window visibility or basic appearance differs from the required defaults.");
            report.status = "passed";
            Debug.Log("Added saved pause settings using the existing main-menu SFX controls; original pause object IDs retained.");
        }
        catch (Exception exception)
        {
            report.status = "failed";
            report.errors.Add(exception.ToString());
            throw;
        }
        finally
        {
            if (prefabContents != null) PrefabUtility.UnloadPrefabContents(prefabContents);
            if (ownsSourceScene && sourceScene.IsValid() && sourceScene.isLoaded) EditorSceneManager.CloseScene(sourceScene, true);
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, JsonUtility.ToJson(report, true));
        }
    }

    private static void CheckSettingsReferences(SfxSettingsView view, Transform root)
    {
        if (view == null) throw new InvalidOperationException("SfxSettingsView is missing from the copied body.");
        var settings = new SerializedObject(view);
        foreach (string field in new[] { "volumeSlider", "volumeValueText", "muteToggle" })
        {
            Component target = settings.FindProperty(field).objectReferenceValue as Component;
            if (target == null || !target.transform.IsChildOf(root))
                throw new InvalidOperationException("Cloned settings reference is not local: " + field);
        }
    }

    private static List<ObjectIdentity> ReadIdentities(GameObject root)
    {
        var entries = new List<ObjectIdentity>();
        foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
        {
            string path = transform == root.transform ? "." : AnimationUtility.CalculateTransformPath(transform, root.transform);
            AddIdentity(entries, path + "/GameObject", transform.gameObject);
            Component[] components = transform.GetComponents<Component>();
            for (int index = 0; index < components.Length; index++)
                if (components[index] != null)
                    AddIdentity(entries, path + "/" + components[index].GetType().FullName + "#" + index, components[index]);
        }
        return entries;
    }

    private static void AddIdentity(List<ObjectIdentity> list, string key, Object target)
    {
        if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(target, out string _, out long fileId))
            throw new InvalidOperationException("Saved local file ID is unavailable: " + key);
        list.Add(new ObjectIdentity { key = key, fileId = fileId });
    }

    private static bool CheckDefaultButton(Button button)
    {
        Text label = button.GetComponentInChildren<Text>();
        return button.transition == Selectable.Transition.None && button.GetComponent<Image>().color == Color.white
            && button.GetComponent<Image>().sprite == resources.standard && label.font == font
            && label.fontSize == 42 && label.color == new Color(50f / 255f, 50f / 255f, 50f / 255f, 1f);
    }

    private static void MoveExisting(Button button, float y)
    {
        RectTransform rect = (RectTransform)button.transform;
        rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, y);
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        var root = new GameObject(name, typeof(RectTransform));
        root.transform.SetParent(parent, false);
        return (RectTransform)root.transform;
    }
    private static void SetUiLayer(Transform transform)
    {
        transform.gameObject.layer = 5;
        foreach (Transform child in transform) SetUiLayer(child);
    }
    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
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
    private static Button CreateButton(string name, string label, Transform parent, float y, float width)
    {
        GameObject root = DefaultControls.CreateButton(resources);
        root.name = name;
        root.transform.SetParent(parent, false);
        Place((RectTransform)root.transform, new Vector2(0f, y), new Vector2(width, 80f));
        Button button = root.GetComponent<Button>();
        button.transition = Selectable.Transition.None;
        Text text = root.GetComponentInChildren<Text>();
        text.font = font;
        text.fontSize = 42;
        text.text = label;
        return button;
    }
}
