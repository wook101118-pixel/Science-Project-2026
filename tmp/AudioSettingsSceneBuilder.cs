// Temporary authoring utility. Run only inside the isolated Unity validation clone.
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using sung_wook;
using Object = UnityEngine.Object;

public static class AudioSettingsSceneBuilder
{
    private const string MenuPath = "Assets/@Project/@sung_wook/@Game/00_Scenes/MainMenu.unity";
    private static Font font;
    private static DefaultControls.Resources resources;

    [Serializable]
    private sealed class Audit
    {
        public string scene;
        public string contentGameObject;
        public string contentRectTransform;
        public string settingsView;
        public string slider;
        public string valueText;
        public string muteToggle;
        public int contentChildCount;
        public bool preservesDefaultColors;
        public string note = "Only the existing Settings Window/Content and newly authored descendants/component were edited. Existing background, title, close button and main menu layout are preserved.";
    }

    public static void Build()
    {
        if (!Application.isBatchMode || !Application.dataPath.Replace('\\', '/').Contains("/Temp/CodexUnityVisualBuild/"))
            throw new InvalidOperationException("Author only in the isolated Unity validation project.");

        resources = new DefaultControls.Resources
        {
            standard = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"),
            background = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd"),
            knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd"),
            checkmark = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Checkmark.psd")
        };
        font = AssetDatabase.LoadAssetAtPath<Font>(AssetDatabase.GUIDToAssetPath("41554645f27a73046866b978f0374dd4"));
        if (font == null || resources.standard == null || resources.background == null || resources.knob == null || resources.checkmark == null)
            throw new InvalidOperationException("Existing font or built-in UI resource was unavailable.");

        Scene scene = EditorSceneManager.OpenScene(MenuPath, OpenSceneMode.Single);
        MainMenuController menu = Object.FindFirstObjectByType<MainMenuController>(FindObjectsInactive.Include);
        if (menu == null) throw new InvalidOperationException("The authored MainMenuController is missing.");
        var menuSettings = new SerializedObject(menu);
        GameObject settingsWindow = menuSettings.FindProperty("settingsWindow").objectReferenceValue as GameObject;
        RectTransform content = settingsWindow != null ? settingsWindow.transform.Find("Content") as RectTransform : null;
        if (content == null) throw new InvalidOperationException("The existing Settings Window/Content is missing.");
        SfxSettingsView existingView = content.GetComponent<SfxSettingsView>();
        if (existingView == null && content.childCount != 0)
            throw new InvalidOperationException("Refusing to replace unrelated settings content.");
        if (existingView != null)
        {
            // Allow a repeat in the disposable clone while preserving the source scene's old objects.
            for (int i = content.childCount - 1; i >= 0; i--) Object.DestroyImmediate(content.GetChild(i).gameObject);
            Object.DestroyImmediate(existingView);
        }

        Text volumeLabel = CreateText("SFX Volume Label", "SFX 볼륨", content, TextAnchor.MiddleLeft);
        Place((RectTransform)volumeLabel.transform, new Vector2(-65f, 75f), new Vector2(470f, 60f));
        Text volumeValue = CreateText("SFX Volume Value", "100%", content, TextAnchor.MiddleRight);
        Place((RectTransform)volumeValue.transform, new Vector2(240f, 75f), new Vector2(120f, 60f));

        GameObject sliderObject = DefaultControls.CreateSlider(resources);
        sliderObject.name = "SFX Volume";
        sliderObject.transform.SetParent(content, false);
        SetUiLayer(sliderObject);
        Place((RectTransform)sliderObject.transform, new Vector2(0f, 5f), new Vector2(600f, 36f));
        Slider slider = sliderObject.GetComponent<Slider>();
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.wholeNumbers = false;
        slider.value = 1f;
        slider.transition = Selectable.Transition.None;
        slider.handleRect.sizeDelta = new Vector2(36f, 0f);
        ((RectTransform)slider.handleRect.parent).sizeDelta = new Vector2(-36f, 0f);

        GameObject toggleObject = DefaultControls.CreateToggle(resources);
        toggleObject.name = "SFX Mute";
        toggleObject.transform.SetParent(content, false);
        SetUiLayer(toggleObject);
        Place((RectTransform)toggleObject.transform, new Vector2(0f, -95f), new Vector2(300f, 60f));
        Toggle mute = toggleObject.GetComponent<Toggle>();
        mute.isOn = false;
        mute.transition = Selectable.Transition.None;
        RectTransform checkbox = toggleObject.transform.Find("Background") as RectTransform;
        Place(checkbox, new Vector2(-110f, 0f), new Vector2(40f, 40f));
        RectTransform checkmark = checkbox.Find("Checkmark") as RectTransform;
        Place(checkmark, Vector2.zero, new Vector2(30f, 30f));
        Text muteLabel = toggleObject.transform.Find("Label").GetComponent<Text>();
        muteLabel.font = font;
        muteLabel.fontSize = 36;
        muteLabel.text = "음소거";
        muteLabel.alignment = TextAnchor.MiddleLeft;
        RectTransform muteLabelRect = (RectTransform)muteLabel.transform;
        muteLabelRect.anchorMin = Vector2.zero;
        muteLabelRect.anchorMax = Vector2.one;
        muteLabelRect.offsetMin = new Vector2(72f, 0f);
        muteLabelRect.offsetMax = Vector2.zero;

        SfxSettingsView view = content.gameObject.AddComponent<SfxSettingsView>();
        var viewSettings = new SerializedObject(view);
        viewSettings.FindProperty("volumeSlider").objectReferenceValue = slider;
        viewSettings.FindProperty("volumeValueText").objectReferenceValue = volumeValue;
        viewSettings.FindProperty("muteToggle").objectReferenceValue = mute;
        viewSettings.ApplyModifiedPropertiesWithoutUndo();

        bool originalColors = true;
        foreach (Image image in content.GetComponentsInChildren<Image>(true)) originalColors &= image.color == Color.white;
        Color defaultText = new Color(50f / 255f, 50f / 255f, 50f / 255f, 1f);
        foreach (Text text in content.GetComponentsInChildren<Text>(true)) originalColors &= text.color == defaultText;
        if (!originalColors) throw new InvalidOperationException("A UI element no longer has its original DefaultControls color.");
        if (!EditorSceneManager.SaveScene(scene, MenuPath)) throw new InvalidOperationException("Main menu settings scene save failed.");

        var audit = new Audit
        {
            scene = MenuPath,
            contentGameObject = GlobalObjectId.GetGlobalObjectIdSlow(content.gameObject).ToString(),
            contentRectTransform = GlobalObjectId.GetGlobalObjectIdSlow(content).ToString(),
            settingsView = GlobalObjectId.GetGlobalObjectIdSlow(view).ToString(),
            slider = GlobalObjectId.GetGlobalObjectIdSlow(slider).ToString(),
            valueText = GlobalObjectId.GetGlobalObjectIdSlow(volumeValue).ToString(),
            muteToggle = GlobalObjectId.GetGlobalObjectIdSlow(mute).ToString(),
            contentChildCount = content.childCount,
            preservesDefaultColors = originalColors
        };
        string logs = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Logs");
        Directory.CreateDirectory(logs);
        File.WriteAllText(Path.Combine(logs, "AudioSettingsAuthoringAudit.json"), JsonUtility.ToJson(audit, true));
        Debug.Log("Authored SFX volume and mute in existing Settings Content: " + JsonUtility.ToJson(audit));
    }

    private static Text CreateText(string name, string value, Transform parent, TextAnchor alignment)
    {
        GameObject root = DefaultControls.CreateText(resources);
        root.name = name;
        root.transform.SetParent(parent, false);
        SetUiLayer(root);
        Text text = root.GetComponent<Text>();
        text.font = font;
        text.fontSize = 36;
        text.text = value;
        text.alignment = alignment;
        text.raycastTarget = false;
        return text;
    }

    private static void SetUiLayer(GameObject root)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 5;
    }

    private static void Place(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }
}
