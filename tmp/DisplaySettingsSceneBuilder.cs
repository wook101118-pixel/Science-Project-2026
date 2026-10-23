// Temporary authoring utility: copy only into the isolated Unity validation project.
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

public static class DisplaySettingsSceneBuilder
{
    private const string Root = "Assets/@Project/@sung_wook/@Game/";
    private const string MenuPath = Root + "00_Scenes/MainMenu.unity";
    private const string PausePath = Root + "02_Prefabs/UI/PauseMenu.prefab";
    private const string ReportPath = "C:/Users/wook1/GitBub-Unity/Science-Project-2026/Logs/DisplaySettingsAuthoringAudit.json";
    private static Font font;
    private static DefaultControls.Resources resources;

    [Serializable] private sealed class Identity { public string key; public long id; }
    [Serializable] private sealed class BodyAudit
    {
        public string asset;
        public bool originalObjectIdsPreserved;
        public bool sfxReferencesPreserved;
        public bool allUiLayers;
        public bool originalDefaultColors;
        public bool localDisplayReferences;
        public List<Identity> originalObjects;
    }
    [Serializable] private sealed class Report
    {
        public string status = "running";
        public List<BodyAudit> bodies = new List<BodyAudit>();
        public List<string> errors = new List<string>();
        public string note = "Existing SFX objects, settings controller references, titles, close buttons and original object IDs are retained. Only settings Content positions/size and authored display controls are added. No new artwork or stage scene changes.";
    }

    public static void Build()
    {
        var report = new Report();
        GameObject pauseContents = null;
        try
        {
            if (!Application.isBatchMode || Application.dataPath.Replace('\\', '/').IndexOf("/Temp/CodexUnityVisualBuild/", StringComparison.OrdinalIgnoreCase) < 0)
                throw new InvalidOperationException("Author only in the isolated CodexUnityVisualBuild clone.");
            resources = new DefaultControls.Resources
            {
                standard = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"),
                background = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd"),
                checkmark = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Checkmark.psd"),
                dropdown = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/DropdownArrow.psd"),
                mask = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UIMask.psd")
            };
            font = AssetDatabase.LoadAssetAtPath<Font>(AssetDatabase.GUIDToAssetPath("41554645f27a73046866b978f0374dd4"));
            if (font == null || resources.standard == null || resources.background == null || resources.checkmark == null || resources.dropdown == null || resources.mask == null)
                throw new InvalidOperationException("Existing built-in UI sprites or Korean font are unavailable.");

            Scene scene = EditorSceneManager.OpenScene(MenuPath, OpenSceneMode.Single);
            MainMenuController menu = Object.FindFirstObjectByType<MainMenuController>(FindObjectsInactive.Include);
            if (menu == null) throw new InvalidOperationException("Main menu controller is unavailable.");
            GameObject window = new SerializedObject(menu).FindProperty("settingsWindow").objectReferenceValue as GameObject;
            RectTransform content = window != null ? window.transform.Find("Content") as RectTransform : null;
            List<Identity> originalMenu = ReadSceneIdentities(scene);
            BodyAudit menuAudit = AuthorBody(content, MenuPath);
            if (!EditorSceneManager.SaveScene(scene, MenuPath)) throw new InvalidOperationException("Main menu scene save failed.");
            AssertIdentities(originalMenu, ReadSceneIdentities(scene));
            menuAudit.originalObjects = originalMenu;
            menuAudit.originalObjectIdsPreserved = true;
            report.bodies.Add(menuAudit);

            GameObject originalPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PausePath);
            if (originalPrefab == null) throw new InvalidOperationException("Existing pause prefab is unavailable.");
            List<Identity> originalPause = ReadIdentities(originalPrefab);
            pauseContents = PrefabUtility.LoadPrefabContents(PausePath);
            PauseMenuController pause = pauseContents.GetComponent<PauseMenuController>();
            window = new SerializedObject(pause).FindProperty("settingsWindow").objectReferenceValue as GameObject;
            content = window != null ? window.transform.Find("Content") as RectTransform : null;
            BodyAudit pauseAudit = AuthorBody(content, PausePath);
            if (PrefabUtility.SaveAsPrefabAsset(pauseContents, PausePath) == null)
                throw new InvalidOperationException("Pause prefab save failed.");
            AssetDatabase.SaveAssets();
            AssertIdentities(originalPause, ReadIdentities(AssetDatabase.LoadAssetAtPath<GameObject>(PausePath)));
            pauseAudit.originalObjects = originalPause;
            pauseAudit.originalObjectIdsPreserved = true;
            report.bodies.Add(pauseAudit);
            report.status = "passed";
            Debug.Log("Authored shared display controls in both saved settings windows while retaining all existing objects and SFX bindings.");
        }
        catch (Exception exception)
        {
            report.status = "failed";
            report.errors.Add(exception.ToString());
            throw;
        }
        finally
        {
            if (pauseContents != null) PrefabUtility.UnloadPrefabContents(pauseContents);
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, JsonUtility.ToJson(report, true));
        }
    }

    private static BodyAudit AuthorBody(RectTransform content, string asset)
    {
        if (content == null) throw new InvalidOperationException("The saved settings Content is missing: " + asset);
        if (content.GetComponent<DisplaySettingsView>() != null || content.Find("Screen Mode") != null || content.Find("Resolution") != null)
            throw new InvalidOperationException("Display settings are already authored; refusing to duplicate: " + asset);
        SfxSettingsView sfx = content.GetComponent<SfxSettingsView>();
        if (sfx == null) throw new InvalidOperationException("The existing SFX view must remain in place: " + asset);
        var sfxSerialized = new SerializedObject(sfx);
        Object oldSlider = sfxSerialized.FindProperty("volumeSlider").objectReferenceValue;
        Object oldValue = sfxSerialized.FindProperty("volumeValueText").objectReferenceValue;
        Object oldMute = sfxSerialized.FindProperty("muteToggle").objectReferenceValue;
        if (oldSlider == null || oldValue == null || oldMute == null)
            throw new InvalidOperationException("Existing SFX bindings are incomplete: " + asset);

        Place(content, Vector2.zero, new Vector2(900f, 460f));
        Move(content, "SFX Volume Label", 165f);
        Move(content, "SFX Volume Value", 165f);
        Move(content, "SFX Volume", 110f);
        Move(content, "SFX Mute", 30f);
        Text modeLabel = CreateLabel("Screen Mode Label", "화면 모드", content);
        Place((RectTransform)modeLabel.transform, new Vector2(-190f, -60f), new Vector2(220f, 52f));
        Dropdown mode = CreateDropdown("Screen Mode", content, -60f);
        mode.ClearOptions();
        mode.AddOptions(new List<string> { "전체화면", "창 모드" });
        mode.SetValueWithoutNotify(0);
        Text resolutionLabel = CreateLabel("Resolution Label", "해상도", content);
        Place((RectTransform)resolutionLabel.transform, new Vector2(-190f, -145f), new Vector2(220f, 52f));
        Dropdown resolution = CreateDropdown("Resolution", content, -145f);
        resolution.ClearOptions();
        resolution.AddOptions(new List<string> { "1920 × 1080" });
        resolution.SetValueWithoutNotify(0);

        DisplaySettingsView display = content.gameObject.AddComponent<DisplaySettingsView>();
        var settings = new SerializedObject(display);
        settings.FindProperty("screenModeDropdown").objectReferenceValue = mode;
        settings.FindProperty("resolutionDropdown").objectReferenceValue = resolution;
        settings.ApplyModifiedPropertiesWithoutUndo();

        bool defaultColors = true;
        bool uiLayers = true;
        Color defaultText = new Color(50f / 255f, 50f / 255f, 50f / 255f, 1f);
        foreach (Image image in content.GetComponentsInChildren<Image>(true)) defaultColors &= image.color == Color.white;
        foreach (Text text in content.GetComponentsInChildren<Text>(true)) defaultColors &= text.color == defaultText;
        foreach (Selectable selectable in content.GetComponentsInChildren<Selectable>(true))
            defaultColors &= selectable.transition == Selectable.Transition.None;
        foreach (Transform transform in content.GetComponentsInChildren<Transform>(true)) uiLayers &= transform.gameObject.layer == 5;
        bool retained = oldSlider == sfxSerialized.FindProperty("volumeSlider").objectReferenceValue
            && oldValue == sfxSerialized.FindProperty("volumeValueText").objectReferenceValue
            && oldMute == sfxSerialized.FindProperty("muteToggle").objectReferenceValue;
        bool local = mode.transform.IsChildOf(content) && resolution.transform.IsChildOf(content);
        if (!defaultColors || !uiLayers || !retained || !local)
            throw new InvalidOperationException("Existing appearance, local display references or SFX bindings changed: " + asset);
        return new BodyAudit { asset = asset, sfxReferencesPreserved = retained, allUiLayers = uiLayers, originalDefaultColors = defaultColors, localDisplayReferences = local };
    }

    private static Dropdown CreateDropdown(string name, Transform parent, float y)
    {
        GameObject root = DefaultControls.CreateDropdown(resources);
        root.name = name;
        root.transform.SetParent(parent, false);
        Place((RectTransform)root.transform, new Vector2(130f, y), new Vector2(340f, 52f));
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 5;
        foreach (Image image in root.GetComponentsInChildren<Image>(true)) image.color = Color.white;
        foreach (Selectable selectable in root.GetComponentsInChildren<Selectable>(true)) selectable.transition = Selectable.Transition.None;
        foreach (Text text in root.GetComponentsInChildren<Text>(true))
        {
            text.font = font;
            text.fontSize = 30;
            text.raycastTarget = false;
            text.color = new Color(50f / 255f, 50f / 255f, 50f / 255f, 1f);
        }
        Dropdown dropdown = root.GetComponent<Dropdown>();
        dropdown.template.sizeDelta = new Vector2(0f, 200f);
        RectTransform content = dropdown.template.Find("Viewport/Content") as RectTransform;
        RectTransform item = content.Find("Item") as RectTransform;
        content.sizeDelta = new Vector2(0f, 52f);
        item.sizeDelta = new Vector2(0f, 44f);
        return dropdown;
    }

    private static Text CreateLabel(string name, string label, Transform parent)
    {
        GameObject root = DefaultControls.CreateText(resources);
        root.name = name;
        root.transform.SetParent(parent, false);
        root.layer = 5;
        Text text = root.GetComponent<Text>();
        text.font = font;
        text.fontSize = 36;
        text.text = label;
        text.alignment = TextAnchor.MiddleLeft;
        text.raycastTarget = false;
        return text;
    }

    private static void Move(Transform parent, string name, float y)
    {
        RectTransform target = parent.Find(name) as RectTransform;
        if (target == null) throw new InvalidOperationException("Existing SFX object is missing: " + name);
        target.anchoredPosition = new Vector2(target.anchoredPosition.x, y);
    }
    private static void Place(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }
    private static List<Identity> ReadSceneIdentities(Scene scene)
    {
        var list = new List<Identity>();
        foreach (GameObject root in scene.GetRootGameObjects()) list.AddRange(ReadIdentities(root, root.name));
        return list;
    }
    private static List<Identity> ReadIdentities(GameObject root, string prefix = ".")
    {
        var list = new List<Identity>();
        foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
        {
            string path = prefix + (transform == root.transform ? "" : "/" + AnimationUtility.CalculateTransformPath(transform, root.transform));
            AddIdentity(list, path + "/GameObject", transform.gameObject);
            Component[] components = transform.GetComponents<Component>();
            for (int index = 0; index < components.Length; index++)
                if (components[index] != null) AddIdentity(list, path + "/" + components[index].GetType().FullName + "#" + index, components[index]);
        }
        return list;
    }
    private static void AddIdentity(List<Identity> list, string key, Object target)
    {
        if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(target, out string _, out long id))
        {
            // Loaded scene objects are not persistent assets; their serialized local
            // IDs are exposed through GlobalObjectId rather than AssetDatabase.
            id = unchecked((long)GlobalObjectId.GetGlobalObjectIdSlow(target).targetObjectId);
            if (id == 0) throw new InvalidOperationException("Saved identity is unavailable: " + key);
        }
        list.Add(new Identity { key = key, id = id });
    }
    private static void AssertIdentities(List<Identity> original, List<Identity> saved)
    {
        foreach (Identity before in original)
            if (!saved.Exists(after => after.key == before.key && after.id == before.id))
                throw new InvalidOperationException("An original object identity changed: " + before.key + ": " + before.id);
    }
}
