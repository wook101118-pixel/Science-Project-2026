using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace sung_wook
{
                                                               
    public static class ElectricCompletionScreenBuilder
    {
        public const string PrefabPath = "Assets/@Project/@sung_wook/@Game/02_Prefabs/UI/GameCompleteScreen.prefab";
        private const string HudPath = "Assets/@Project/@sung_wook/@Game/02_Prefabs/UI/StageHUD.prefab";
        private const string KoreanFontGuid = "152a366fc1cbb8541a6a10472a8f05cf";

        private static readonly Color Navy = new Color32(28, 43, 72, 255);
        private static readonly Color Card = new Color32(36, 55, 89, 255);
        private static readonly Color Chip = new Color32(45, 66, 103, 255);
        private static readonly Color Cyan = new Color32(131, 219, 237, 255);
        private static readonly Color Muted = new Color32(200, 219, 239, 255);

        public static GameObject CreatePrefab()
        {
            TMP_FontAsset font = FindFont();
            if (font == null) throw new InvalidOperationException("The shared HUD Korean TMP font is missing.");
            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
            AssetDatabase.Refresh();
            Scene preview = EditorSceneManager.NewPreviewScene();
            GameObject root = null;
            try
            {
                root = new GameObject("GameCompleteScreen", typeof(RectTransform));
                root.layer = LayerMask.NameToLayer("UI");
                root.SetActive(false);
                SceneManager.MoveGameObjectToScene(root, preview);
                RectTransform screen = (RectTransform)root.transform;
                Stretch(screen);
                var canvas = root.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.overrideSorting = true;
                canvas.sortingOrder = 1000;
                root.AddComponent<GraphicRaycaster>();
                var group = root.AddComponent<CanvasGroup>();
                var view = root.AddComponent<ElectricGameCompleteView>();
                var scrim = root.AddComponent<Image>();
                scrim.color = new Color(Navy.r, Navy.g, Navy.b, 0.97f);
                scrim.raycastTarget = true;

                RectTransform shadow = Image(screen, "Card Shadow", new Color(0f, 0f, 0f, 0.25f));
                Anchor(shadow, new Vector2(0.13f, 0.13f), new Vector2(0.87f, 0.87f));
                shadow.anchoredPosition = new Vector2(12f, -12f);
                RectTransform border = Image(screen, "Completion Frame", new Color32(77, 117, 147, 255));
                Anchor(border, new Vector2(0.13f, 0.13f), new Vector2(0.87f, 0.87f));
                RectTransform card = Image(border, "Completion Card", Card);
                Stretch(card);
                card.offsetMin = Vector2.one * 3f;
                card.offsetMax = Vector2.one * -3f;

                RectTransform accent = Image(card, "Top Accent", Cyan);
                Anchor(accent, new Vector2(0.43f, 0.94f), new Vector2(0.57f, 0.947f));
                Label(card, "Series Label", "ELECTRIC BOUNCE", font, Cyan, 27f,
                    new Vector2(0.12f, 0.84f), new Vector2(0.88f, 0.91f));
                Label(card, "Completion Title", "게임 완료", font, Color.white, 94f,
                    new Vector2(0.08f, 0.66f), new Vector2(0.92f, 0.84f));
                Label(card, "Completion Summary", "Stage 2~5의 별 수집과 학습 목표를 모두 달성했습니다.", font, Muted, 31f,
                    new Vector2(0.09f, 0.55f), new Vector2(0.91f, 0.66f));

                RectTransform concepts = Rect(card, "Completed Concepts");
                Anchor(concepts, new Vector2(0.1f, 0.29f), new Vector2(0.9f, 0.52f));
                Concept(concepts, "Contact Concept", "02", "접촉 대전", font,
                    new Vector2(0f, 0.54f), new Vector2(0.485f, 1f));
                Concept(concepts, "Force Concept", "03", "쿨롱의 전기력", font,
                    new Vector2(0.515f, 0.54f), new Vector2(1f, 1f));
                Concept(concepts, "Field Concept", "04", "평행판 전기장", font,
                    new Vector2(0f, 0f), new Vector2(0.485f, 0.46f));
                Concept(concepts, "Voltage Concept", "05", "반데그라프와 전압", font,
                    new Vector2(0.515f, 0f), new Vector2(1f, 0.46f));

                RectTransform buttonRect = Image(card, "Restart Button", Cyan);
                Anchor(buttonRect, new Vector2(0.26f, 0.12f), new Vector2(0.74f, 0.235f));
                var button = buttonRect.gameObject.AddComponent<Button>();
                button.targetGraphic = buttonRect.GetComponent<Image>();
                button.targetGraphic.raycastTarget = true;
                ColorBlock colors = button.colors;
                colors.normalColor = Color.white;
                colors.highlightedColor = new Color32(216, 250, 255, 255);
                colors.selectedColor = new Color32(216, 250, 255, 255);
                colors.pressedColor = new Color32(174, 221, 237, 255);
                colors.fadeDuration = 0.12f;
                button.colors = colors;
                Label(buttonRect, "Restart Label", "처음부터 다시 시작", font, Navy, 36f,
                    new Vector2(0.04f, 0.05f), new Vector2(0.96f, 0.95f));
                Label(card, "Restart Hint", "다시 시작하면 Stage 02부터 진행합니다.", font, Muted, 23f,
                    new Vector2(0.1f, 0.035f), new Vector2(0.9f, 0.1f));

                var data = new SerializedObject(view);
                data.FindProperty("restartButton").objectReferenceValue = button;
                data.FindProperty("canvasGroup").objectReferenceValue = group;
                data.ApplyModifiedPropertiesWithoutUndo();
                GameObject asset = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool success);
                if (!success || asset == null) throw new IOException("Could not save " + PrefabPath);
                return asset;
            }
            finally
            {
                if (root != null) Object.DestroyImmediate(root);
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        public static void InstallIntoHudPrefab()
        {
            GameObject completion = CreatePrefab();
            if (AssetDatabase.LoadAssetAtPath<GameObject>(HudPath) == null)
                throw new InvalidOperationException("Save the common StageHUD prefab before installing the completion screen.");
            GameObject hud = PrefabUtility.LoadPrefabContents(HudPath);
            try
            {
                Transform previous = hud.transform.Find("GameCompleteScreen");
                if (previous != null) Object.DestroyImmediate(previous.gameObject);
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(completion, hud.transform);
                instance.name = "GameCompleteScreen";
                Stretch((RectTransform)instance.transform);
                instance.transform.SetAsLastSibling();
                instance.SetActive(false);
                PrefabUtility.SaveAsPrefabAsset(hud, HudPath, out bool success);
                if (!success) throw new IOException("Could not save the completion screen inside " + HudPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(hud); }
        }

        private static TMP_FontAsset FindFont()
        {
            GameObject hud = AssetDatabase.LoadAssetAtPath<GameObject>(HudPath);
            if (hud != null)
                foreach (TMP_Text text in hud.GetComponentsInChildren<TMP_Text>(true))
                    if (text.font != null) return text.font;
            return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(KoreanFontGuid));
        }

        private static void Concept(Transform parent, string name, string number, string title, TMP_FontAsset font,
            Vector2 min, Vector2 max)
        {
            RectTransform chip = Image(parent, name, Chip);
            Anchor(chip, min, max);
            RectTransform stripe = Image(chip, name + " Accent", Cyan);
            Anchor(stripe, Vector2.zero, new Vector2(0.008f, 1f));
            Label(chip, name + " Number", number, font, Cyan, 31f,
                new Vector2(0.035f, 0.1f), new Vector2(0.17f, 0.9f));
            TMP_Text text = Label(chip, name + " Label", title, font, Color.white, 31f,
                new Vector2(0.21f, 0.1f), new Vector2(0.96f, 0.9f));
            text.alignment = TextAlignmentOptions.MidlineLeft;
        }

        private static TextMeshProUGUI Label(Transform parent, string name, string value, TMP_FontAsset font,
            Color color, float fontSize, Vector2 min, Vector2 max)
        {
            RectTransform rect = Rect(parent, name);
            Anchor(rect, min, max);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.text = value;
            text.color = color;
            text.fontSize = fontSize;
            text.enableAutoSizing = true;
            text.fontSizeMin = Mathf.Min(fontSize, 19f);
            text.fontSizeMax = fontSize;
            text.alignment = TextAlignmentOptions.Center;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            return text;
        }

        private static RectTransform Image(Transform parent, string name, Color color)
        {
            RectTransform rect = Rect(parent, name);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return rect;
        }

        private static RectTransform Rect(Transform parent, string name)
        {
            var obj = new GameObject(name, typeof(RectTransform));
            obj.layer = LayerMask.NameToLayer("UI");
            obj.transform.SetParent(parent, false);
            return (RectTransform)obj.transform;
        }

        private static void Stretch(RectTransform rect) => Anchor(rect, Vector2.zero, Vector2.one);

        private static void Anchor(RectTransform rect, Vector2 min, Vector2 max)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.pivot = Vector2.one * 0.5f;
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
        }
    }
}
