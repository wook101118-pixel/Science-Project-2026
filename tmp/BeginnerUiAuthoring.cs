using System;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using sung_wook;

public static class BeginnerUiAuthoring
{
    private const string HudPath = "Assets/@Project/@sung_wook/@Game/02_Prefabs/UI/StageHUD.prefab";
    public static void Build()
    {
        if (!Application.isBatchMode || !Application.dataPath.Replace('\\','/').StartsWith("C:/Users/wook1/AppData/Local/Temp/CodexUnityVisualBuild/"))
            throw new InvalidOperationException("Author stored UI only in validation clone.");
        AuthorChallengeResultsUi.Build();
        GameObject root = PrefabUtility.LoadPrefabContents(HudPath);
        try
        {
            var hud = root.GetComponentInChildren<ElectricStageHudView>(true);
            var card = root.GetComponentInChildren<CollapsiblePanel>(true);
            if (hud == null || card == null) throw new InvalidOperationException("Saved HUD or help card missing.");
            var cardData = new SerializedObject(card);
            var content = (LayoutElement)cardData.FindProperty("content").objectReferenceValue;
            var template = (Button)cardData.FindProperty("toggleButton").objectReferenceValue;
            Transform previous = content.transform.Find("ExperimentPlanChoices");
            if (previous != null) UnityEngine.Object.DestroyImmediate(previous.gameObject);
            var container = new GameObject("ExperimentPlanChoices", typeof(RectTransform));
            container.transform.SetParent(content.transform, false);
            var rect = container.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0,0); rect.anchorMax = new Vector2(1,0);
            rect.pivot = new Vector2(.5f,0); rect.anchoredPosition = new Vector2(0,8); rect.sizeDelta = new Vector2(-16,80);
            var view = container.AddComponent<ElectricExperimentPlanView>();
            var viewData = new SerializedObject(view);
            for (int plan=0; plan<2; plan++)
            {
                Button button = UnityEngine.Object.Instantiate(template, container.transform);
                button.name = plan == 0 ? "FirstPlanButton" : "SecondPlanButton";
                button.onClick = new Button.ButtonClickedEvent();
                TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
                label.text = plan == 0 ? "기본 풀이" : "다른 풀이";
                label.enableAutoSizing = false;
                var buttonRect = (RectTransform)button.transform;
                buttonRect.anchorMin = new Vector2(0,0); buttonRect.anchorMax = new Vector2(1,0);
                buttonRect.pivot = new Vector2(.5f,0); buttonRect.sizeDelta = new Vector2(0,36);
                buttonRect.anchoredPosition = new Vector2(0,plan == 0 ? 40 : 0);
                viewData.FindProperty(plan == 0 ? "firstPlanButton" : "secondPlanButton").objectReferenceValue = button;
                viewData.FindProperty(plan == 0 ? "firstPlanLabel" : "secondPlanLabel").objectReferenceValue = label;
            }
            viewData.ApplyModifiedPropertiesWithoutUndo();
            var hudData = new SerializedObject(hud);
            hudData.FindProperty("planChoices").objectReferenceValue = rect;
            hudData.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root,HudPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssets();
        Debug.Log("Beginner UI authored using only existing button, Image, font, material, and color values.");
    }
}
