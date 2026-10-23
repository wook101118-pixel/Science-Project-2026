// Temporary authoring utility: compile and run only in the isolated validation clone.
// Edits the saved audio widgets in MainMenu and the shared PauseMenu prefab.
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

public static class BgmSettingsAuthoring
{
    private const string MenuPath = "Assets/@Project/@sung_wook/@Game/00_Scenes/@InGame/MainMenu.unity";
    private const string PausePath = "Assets/@Project/@sung_wook/@Game/02_Prefabs/UI/PauseMenu.prefab";

    [Serializable] private sealed class Audit
    {
        public string status = "passed";
        public string layout = "Content 900x460; BGM header y180, controls y135; SFX header y65, controls y20. Existing display rows and surrounding menu/pause layout are unchanged.";
        public List<PanelAudit> panels = new List<PanelAudit>();
    }

    [Serializable] private sealed class PanelAudit
    {
        public string asset;
        public string bgmSlider;
        public string bgmValue;
        public string bgmMute;
        public string sfxSlider;
        public string sfxValue;
        public string sfxMute;
        public bool independentAudioControls;
        public bool originalSfxReferencesPreserved;
        public bool displayAndControllerBindingsPreserved;
        public int preservedRectTransforms;
        public int addedDirectChildren;
    }

    private sealed class RectState
    {
        public RectTransform rect;
        public string json;
    }

    [Serializable] private sealed class RectLayout
    {
        public int parent;
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 scale;
        public Vector2 anchorMin;
        public Vector2 anchorMax;
        public Vector2 anchoredPosition;
        public Vector2 sizeDelta;
        public Vector2 pivot;
    }

    public static void Build()
    {
        Require(Application.isBatchMode && Application.dataPath.Replace('\\', '/').Contains("/Temp/CodexUnityVisualBuild/"),
            "BGM UI authoring must run only in the isolated Unity validation clone.");
        var report = new Audit();

        Scene scene = EditorSceneManager.OpenScene(MenuPath, OpenSceneMode.Single);
        MainMenuController menu = Object.FindFirstObjectByType<MainMenuController>(FindObjectsInactive.Include);
        Require(menu != null, "The saved main menu controller is missing.");
        GameObject settingsWindow = Ref<GameObject>(menu, "settingsWindow");
        Require(settingsWindow != null, "The saved main menu settings window is missing.");
        RectTransform content = settingsWindow.transform.Find("Content") as RectTransform;
        Require(content != null, "The saved main menu Settings Window/Content is missing.");

        BGM music = Object.FindFirstObjectByType<BGM>(FindObjectsInactive.Include);
        Require(music != null, "The user's saved BGM manager is missing.");
        string musicBefore = EditorJsonUtility.ToJson(music);
        AudioSource musicSource = music.GetComponent<AudioSource>();
        Require(musicSource != null && musicSource.clip != null, "The user's saved BGM AudioSource or clip is missing.");
        string sourceBefore = EditorJsonUtility.ToJson(musicSource);
        PanelAudit menuAudit = Configure(content, menu, MenuPath, true);
        Require(musicBefore == EditorJsonUtility.ToJson(music) && sourceBefore == EditorJsonUtility.ToJson(musicSource),
            "Authoring changed the user's BGM manager or its AudioSource.");
        Require(EditorSceneManager.SaveScene(scene, MenuPath), "Saving the main menu failed.");
        report.panels.Add(menuAudit);

        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(PausePath);
        try
        {
            PauseMenuController pause = prefabRoot.GetComponentInChildren<PauseMenuController>(true);
            Require(pause != null, "The saved shared pause controller is missing.");
            GameObject pauseSettings = Ref<GameObject>(pause, "settingsWindow");
            Require(pauseSettings != null, "The saved pause settings window is missing.");
            RectTransform pauseContent = pauseSettings.transform.Find("Content") as RectTransform;
            Require(pauseContent != null, "The shared pause Settings Window/Content is missing.");
            PanelAudit pauseAudit = Configure(pauseContent, pause, PausePath, false);
            bool saved;
            PrefabUtility.SaveAsPrefabAsset(prefabRoot, PausePath, out saved);
            Require(saved, "Saving the shared pause prefab failed.");
            report.panels.Add(pauseAudit);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }

        AssetDatabase.SaveAssets();
        string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/BgmSettingsAuthoring.json"));
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        File.WriteAllText(output, JsonUtility.ToJson(report, true));
        Debug.Log("Authored independent BGM/SFX settings in the saved main menu and shared pause prefab: " + output);
    }

    private static PanelAudit Configure(RectTransform content, MonoBehaviour controller, string asset, bool preserveExistingBgm)
    {
        Require(content.sizeDelta == new Vector2(900f, 460f), "Unexpected settings content dimensions in " + asset);
        SfxSettingsView sfxView = content.GetComponent<SfxSettingsView>();
        DisplaySettingsView display = content.GetComponent<DisplaySettingsView>();
        Require(sfxView != null && display != null, "Existing SFX/display settings components are missing in " + asset);
        string sfxBefore = EditorJsonUtility.ToJson(sfxView);
        string displayBefore = EditorJsonUtility.ToJson(display);
        string controllerBefore = EditorJsonUtility.ToJson(controller);

        Slider sfxSlider = Ref<Slider>(sfxView, "volumeSlider");
        Text sfxValue = Ref<Text>(sfxView, "volumeValueText");
        Toggle sfxMute = Ref<Toggle>(sfxView, "muteToggle");
        Text sfxLabel = Child<Text>(content, "SFX Volume Label");
        Require(sfxSlider != null && sfxValue != null && sfxMute != null && sfxLabel != null,
            "Existing saved SFX widget references are incomplete in " + asset);
        Require(sfxSlider.onValueChanged.GetPersistentEventCount() == 0 && sfxMute.onValueChanged.GetPersistentEventCount() == 0,
            "Unexpected persistent SFX callbacks; refusing to clone them in " + asset);

        BGMSettingView bgmView = content.GetComponent<BGMSettingView>();
        Slider bgmSlider = bgmView != null ? Ref<Slider>(bgmView, "volumeSlider") : null;
        Text bgmValue = bgmView != null ? Ref<Text>(bgmView, "volumeValueText") : null;
        Text bgmLabel = Child<Text>(content, "BGM Volume Label");
        if (preserveExistingBgm)
            Require(bgmView != null && bgmSlider != null && bgmValue != null && bgmLabel != null,
                "The user's existing BGM slider, label or value is missing; refusing to replace it.");

        var moved = new HashSet<RectTransform>
        {
            (RectTransform)sfxSlider.transform, (RectTransform)sfxValue.transform,
            (RectTransform)sfxMute.transform, (RectTransform)sfxLabel.transform
        };
        if (bgmSlider != null) moved.Add((RectTransform)bgmSlider.transform);
        if (bgmValue != null) moved.Add((RectTransform)bgmValue.transform);
        if (bgmLabel != null) moved.Add((RectTransform)bgmLabel.transform);
        Toggle ownBgmMute = Child<Toggle>(content, "BGM Mute");
        if (ownBgmMute != null) moved.Add((RectTransform)ownBgmMute.transform);

        var protectedRects = new List<RectState>();
        Transform root = content.root;
        foreach (RectTransform rect in root.GetComponentsInChildren<RectTransform>(true))
            if (!InsideAudioRow(rect, moved)) protectedRects.Add(new RectState { rect = rect, json = RectJson(rect) });

        int oldChildren = content.childCount;
        Slider originalBgmSlider = bgmSlider;
        Text originalBgmValue = bgmValue;
        if (bgmSlider == null) bgmSlider = Clone(sfxSlider, content, "BGM Volume");
        if (bgmValue == null) bgmValue = Clone(sfxValue, content, "BGM Volume Value");
        if (bgmLabel == null) bgmLabel = Clone(sfxLabel, content, "BGM Volume Label");
        if (ownBgmMute == null) ownBgmMute = Clone(sfxMute, content, "BGM Mute");
        Require(ownBgmMute != sfxMute && bgmSlider != sfxSlider && bgmValue != sfxValue,
            "BGM and SFX must have independent controls.");
        bgmLabel.text = "BGM 볼륨";
        SetMuteLabel(ownBgmMute, "BGM 음소거");
        SetMuteLabel(sfxMute, "효과음 음소거");

        Place((RectTransform)bgmLabel.transform, new Vector2(-65f, 180f), new Vector2(470f, 44f));
        Place((RectTransform)bgmValue.transform, new Vector2(240f, 180f), new Vector2(120f, 44f));
        Place((RectTransform)bgmSlider.transform, new Vector2(-100f, 135f), new Vector2(400f, 36f));
        Place((RectTransform)ownBgmMute.transform, new Vector2(250f, 135f), new Vector2(300f, 50f));
        Place((RectTransform)sfxLabel.transform, new Vector2(-65f, 65f), new Vector2(470f, 44f));
        Place((RectTransform)sfxValue.transform, new Vector2(240f, 65f), new Vector2(120f, 44f));
        Place((RectTransform)sfxSlider.transform, new Vector2(-100f, 20f), new Vector2(400f, 36f));
        Place((RectTransform)sfxMute.transform, new Vector2(250f, 20f), new Vector2(300f, 50f));

        if (bgmView == null) bgmView = content.gameObject.AddComponent<BGMSettingView>();
        var settings = new SerializedObject(bgmView);
        settings.FindProperty("volumeSlider").objectReferenceValue = bgmSlider;
        settings.FindProperty("volumeValueText").objectReferenceValue = bgmValue;
        settings.FindProperty("muteToggle").objectReferenceValue = ownBgmMute;
        settings.ApplyModifiedPropertiesWithoutUndo();

        Require(!preserveExistingBgm || (originalBgmSlider == bgmSlider && originalBgmValue == bgmValue),
            "Existing BGM slider/value instances were replaced.");
        Require(sfxBefore == EditorJsonUtility.ToJson(sfxView), "Original SfxSettingsView references/settings changed.");
        Require(displayBefore == EditorJsonUtility.ToJson(display), "Display settings bindings changed.");
        Require(controllerBefore == EditorJsonUtility.ToJson(controller), "Menu/pause controller bindings changed.");
        foreach (RectState state in protectedRects)
            Require(state.rect != null && state.json == RectJson(state.rect),
                "An unrelated UI RectTransform changed: " + (state.rect != null ? state.rect.name : "destroyed"));

        ValidateBounds(content, bgmLabel, bgmValue, bgmSlider, ownBgmMute, sfxLabel, sfxValue, sfxSlider, sfxMute);
        Require(Ref<Toggle>(bgmView, "muteToggle") == ownBgmMute && Ref<Toggle>(sfxView, "muteToggle") == sfxMute,
            "The saved audio mute bindings are wrong.");
        return new PanelAudit
        {
            asset = asset,
            bgmSlider = Describe(bgmSlider), bgmValue = Describe(bgmValue), bgmMute = Describe(ownBgmMute),
            sfxSlider = Describe(sfxSlider), sfxValue = Describe(sfxValue), sfxMute = Describe(sfxMute),
            independentAudioControls = true,
            originalSfxReferencesPreserved = true,
            displayAndControllerBindingsPreserved = true,
            preservedRectTransforms = protectedRects.Count,
            addedDirectChildren = content.childCount - oldChildren
        };
    }

    private static T Ref<T>(Object target, string field) where T : Object
    {
        var data = new SerializedObject(target);
        SerializedProperty property = data.FindProperty(field);
        Require(property != null, target.name + " has no serialized field " + field);
        return property.objectReferenceValue as T;
    }

    private static T Child<T>(Transform parent, string name) where T : Component
    {
        Transform child = parent.Find(name);
        return child != null ? child.GetComponent<T>() : null;
    }

    private static T Clone<T>(T original, Transform parent, string name) where T : Component
    {
        GameObject copy = Object.Instantiate(original.gameObject, parent, false);
        copy.name = name;
        return copy.GetComponent<T>();
    }

    private static void SetMuteLabel(Toggle toggle, string value)
    {
        Text label = Child<Text>(toggle.transform, "Label");
        Require(label != null, "A saved audio mute label is missing.");
        label.text = value;
    }

    private static void Place(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static void ValidateBounds(RectTransform content, params Component[] widgets)
    {
        foreach (Component widget in widgets)
        {
            RectTransform rect = (RectTransform)widget.transform;
            Vector2 min = rect.anchoredPosition - rect.sizeDelta * .5f;
            Vector2 max = rect.anchoredPosition + rect.sizeDelta * .5f;
            Require(min.x >= -450f && max.x <= 450f && min.y >= -230f && max.y <= 230f,
                "An audio widget falls outside the unchanged Content bounds: " + widget.name);
            Require(rect.parent == content, "An audio widget was moved out of Content.");
        }
    }

    private static string Describe(Object obj) => obj.name + " (instance " + obj.GetInstanceID() + ")";
    private static bool InsideAudioRow(RectTransform rect, HashSet<RectTransform> rows)
    {
        foreach (RectTransform row in rows)
            if (rect == row || rect.IsChildOf(row)) return true;
        return false;
    }
    private static string RectJson(RectTransform rect) => JsonUtility.ToJson(new RectLayout
    {
        parent = rect.parent != null ? rect.parent.GetInstanceID() : 0,
        position = rect.localPosition, rotation = rect.localRotation, scale = rect.localScale,
        anchorMin = rect.anchorMin, anchorMax = rect.anchorMax, anchoredPosition = rect.anchoredPosition,
        sizeDelta = rect.sizeDelta, pivot = rect.pivot
    });
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
