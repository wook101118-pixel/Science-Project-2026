using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;
using sung_wook;
using Object = UnityEngine.Object;

/// <summary>Temporary clone-only authoring helper. All visual elements are saved into each scene.</summary>
public static class SciencePresentationUiAuthoring
{
    public const string FontPath = "Assets/@Project/@sung_wook/@Game/08_Presentation/Fonts/SciencePresentation SDF.asset";
    private const string FontSourcePath = "Assets/@Project/@sung_wook/@Game/08_Presentation/Fonts/NanumGothic-Regular.ttf";
    private const string ScriptFolder = "Assets/@Project/@sung_wook/@Game/01_Scripts/ElectricBounce/";
    private static TMP_FontAsset cachedFont;
    public static TMP_FontAsset Font => cachedFont != null ? cachedFont : cachedFont = EnsureFont();

    private static readonly Color Paper = Hex("F5F6F2");
    private static readonly Color Ink = Hex("24343B");
    private static readonly Color Teal = Hex("287B79");
    private static readonly Color Muted = Hex("61716F");
    private static readonly Color Rule = Hex("CCD7D0");
    private static readonly Color Card = Hex("E9EEE8");

    public static void Apply(Scene scene, int stage)
    {
        if (!scene.IsValid() || stage < 1 || stage > 5) throw new ArgumentException("A loaded stage 1-5 is required.");
        TMP_FontAsset font = Font;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == "Science Presentation") { Object.DestroyImmediate(root); continue; }
            foreach (ElectricStageHudView old in root.GetComponentsInChildren<ElectricStageHudView>(true))
            {
                old.enabled = false;
            }
            foreach (Transform item in root.GetComponentsInChildren<Transform>(true))
                if (item.name == "HUD" || item.name == "HDD" || item.name == "LearningObjectivePanel" || item.name == "Learning_Objective_Panel")
                    item.gameObject.SetActive(false);
        }

        var canvasObject = new GameObject("Science Presentation", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        SceneManager.MoveGameObjectToScene(canvasObject, scene);
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1600, 900);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = .5f;
        RectTransform host = canvasObject.GetComponent<RectTransform>();

        // These opaque strips frame the camera without painting over the experiment itself.
        RectTransform header = Panel(host, "Question Header", 0, 0, 1600, 118, Paper);
        Panel(header, "Header Rule", 24, 116, 1552, 1, Rule);
        TMP_Text chapter = Label(header, "Chapter", 32, 14, 1160, 26, 19, Teal, font, SciencePresentationView.Chapter(stage));
        TMP_Text question = Label(header, "Experiment Question", 32, 48, 1498, 56, 34, Ink, font, SciencePresentationView.Question(stage));
        Label(header, "Series", 1330, 17, 238, 23, 15, Muted, font, "ELECTRIC BOUNCE  /  SCIENCE").alignment = TextAlignmentOptions.TopRight;

        RectTransform right = Panel(host, "Observation Margin", 1232, 118, 368, 683, Paper);
        Panel(right, "Observation Rule", 0, 16, 1, 632, Rule);
        RectTransform card = Panel(right, "Observation Card", 18, 24, 332, 310, Card);
        TMP_Text heading = Label(card, "Observation Heading", 22, 22, 288, 28, 18, Teal, font, "지금 관찰하는 것");
        TMP_Text value = Label(card, "Observation Value", 22, 66, 288, 100, 40, Ink, font, "중성  0");
        TMP_Text detail = Label(card, "Observation Detail", 22, 184, 288, 104, 19, Ink, font, "측정값을 읽고 변화를 비교하세요.");
        TMP_Text principle = Label(right, "Scientific Principle", 40, 360, 288, 84, 23, Ink, font, SciencePresentationView.Principle(stage));
        Panel(right, "Note Rule", 40, 452, 288, 1, Rule);
        TMP_Text note = Label(right, "Model Scope", 40, 470, 288, 112, 16, Muted, font, SciencePresentationView.ModelNote(stage));
        TMP_Text progress = Label(right, "Observed Progress", 40, 602, 288, 64, 18, Teal, font, "실험 → 관찰 → 별 → 출구");

        RectTransform bottom = Panel(host, "Action Footer", 0, 801, 1600, 99, Paper);
        Panel(bottom, "Footer Rule", 24, 0, 1552, 1, Rule);
        Panel(bottom, "Action Accent", 32, 17, 4, 30, Teal);
        TMP_Text action = Label(bottom, "Current Action", 50, 16, 1518, 34, 23, Ink, font, "실험을 준비하세요.");
        TMP_Text controls = Label(bottom, "Keyboard Guide", 50, 62, 1518, 26, 16, Muted, font, SciencePresentationView.Controls(stage));

        var view = canvasObject.AddComponent<SciencePresentationView>();
        view.Configure(stage, chapter, question, heading, value, detail, principle, note, progress, action, controls);
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Camera camera in root.GetComponentsInChildren<Camera>(true))
                if (camera.CompareTag("MainCamera")) camera.rect = new Rect(0, .11f, .77f, .75f);
        EditorUtility.SetDirty(view);
    }

    private static TMP_FontAsset EnsureFont()
    {
        TMP_FontAsset fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (fontAsset == null)
        {
            Font source = AssetDatabase.LoadAssetAtPath<Font>(FontSourcePath);
            if (source == null) throw new InvalidOperationException("The presentation's Nanum Gothic source font is missing: " + FontSourcePath);
            fontAsset = TMP_FontAsset.CreateFontAsset(source, 48, 5, GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
            fontAsset.name = "SciencePresentation SDF";
            AssetDatabase.CreateAsset(fontAsset, FontPath);
            if (fontAsset.material != null && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(fontAsset.material)))
                AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
        }
        fontAsset.atlasPopulationMode = AtlasPopulationMode.Dynamic;
        fontAsset.isMultiAtlasTexturesEnabled = true;
        // Prewarm all authored/runtime Korean strings so player builds do not need an OS font.
        string characters = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ +-=/.,:()[]%°·μ²½∝↑↓→✓○";
        foreach (string path in new[] { "SciencePresentationView.cs", "ScienceApparatusView.cs", "TutorialController2D.cs", "ElectricLearningController2D.cs", "ElectricStageHudView.cs" })
            if (File.Exists(ScriptFolder + path)) characters += File.ReadAllText(ScriptFolder + path);
        foreach (string path in new[] { "tmp/ScienceCampaignAuthoring.cs", "Assets/Editor/ScienceCampaignAuthoring.cs" })
            if (File.Exists(path)) characters += File.ReadAllText(path);
        for (int stage = 1; stage <= 5; stage++)
            characters += SciencePresentationView.Chapter(stage) + SciencePresentationView.Question(stage)
                + SciencePresentationView.Principle(stage) + SciencePresentationView.ModelNote(stage) + SciencePresentationView.Controls(stage);
        fontAsset.TryAddCharacters(new string(characters.Distinct().ToArray()), out string missing);
        // Some comment/control characters are not printable; no missing Korean characters are accepted.
        if (!string.IsNullOrEmpty(missing) && missing.Any(character => character >= '\uAC00' && character <= '\uD7A3'))
            throw new InvalidOperationException("Presentation font is missing Korean glyphs: " + missing);
        foreach (Texture2D atlas in fontAsset.atlasTextures)
            if (atlas != null && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(atlas))) AssetDatabase.AddObjectToAsset(atlas, fontAsset);
        EditorUtility.SetDirty(fontAsset);
        AssetDatabase.SaveAssets();
        return fontAsset;
    }

    private static RectTransform Panel(Transform parent, string name, float x, float y, float width, float height, Color color)
    {
        var item = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        item.transform.SetParent(parent, false);
        var rect = item.GetComponent<RectTransform>();
        Position(rect, x, y, width, height);
        Image image = item.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return rect;
    }

    private static TMP_Text Label(Transform parent, string name, float x, float y, float width, float height,
        float size, Color color, TMP_FontAsset font, string value)
    {
        var item = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        item.transform.SetParent(parent, false);
        var label = item.GetComponent<TextMeshProUGUI>();
        Position(label.rectTransform, x, y, width, height);
        label.font = font;
        label.fontSize = size;
        label.fontStyle = FontStyles.Normal;
        label.color = color;
        label.raycastTarget = false;
        label.enableAutoSizing = false;
        label.textWrappingMode = TextWrappingModes.Normal;
        label.overflowMode = TextOverflowModes.Overflow;
        label.alignment = TextAlignmentOptions.TopLeft;
        label.margin = Vector4.zero;
        label.lineSpacing = 3;
        label.text = value;
        return label;
    }

    private static void Position(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
    }

    private static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString("#" + hex, out Color color);
        return color;
    }
}
